using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Application.Flow;
using Game.Application.Ports;
using Game.Domain.Reactive;
using Game.Domain.Session;
using Game.Net.Transport;
using Netcode.Transports;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace Game.Platform.Steam
{
    /// <summary>Steam Networking Sockets (P2P over Steam Datagram Relay): no IPs, no port forwarding.</summary>
    public sealed class SteamTransportProvider : INetworkTransportProvider
    {
        public SteamTransportProvider(NetworkManager networkManager)
        {
            SteamTransport = networkManager.gameObject.AddComponent<SteamNetworkingSocketsTransport>();
        }

        public SteamNetworkingSocketsTransport SteamTransport { get; }

        public NetworkTransport Transport => SteamTransport;
    }

    /// <summary>
    /// Steam lobby = discovery/invites; the actual game connection is Netcode over Steam P2P to the lobby
    /// owner's SteamID. The lobby mirrors the network session: joinable flag and member data are updated
    /// by the host, and everyone leaves the lobby when the session ends, so the lobby dies with the host.
    /// </summary>
    public sealed class SteamLobbyMatchmaking : IMatchmakingService, IDisposable
    {
        private const string KeyName = "name";
        private const string KeyVersion = "version";
        private const string KeyHost = "host";
        private const string KeyPlayers = "players";
        private const string KeyPhase = "phase";
        private const string ConnectArg = "+connect_lobby";

        private readonly SteamTransportProvider _transport;
        private readonly AppSettings _settings;
        private readonly IUserNotifier _notifier;
        private readonly ReactiveProperty<string> _sessionCode = new ReactiveProperty<string>(string.Empty);
        private readonly Callback<GameLobbyJoinRequested_t> _lobbyJoinRequested;
        private readonly Callback<GameRichPresenceJoinRequested_t> _richPresenceJoinRequested;
        private readonly Callback<LobbyChatUpdate_t> _lobbyChatUpdate;
        private CSteamID _lobby = CSteamID.Nil;
        private CSteamID _lobbyHost = CSteamID.Nil;
        private bool _isLobbyOwner;
        private string _launchTarget;

        public SteamLobbyMatchmaking(SteamTransportProvider transport, AppSettings settings, IUserNotifier notifier)
        {
            _transport = transport;
            _settings = settings;
            _notifier = notifier;
            _lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnLobbyJoinRequested);
            _richPresenceJoinRequested = Callback<GameRichPresenceJoinRequested_t>.Create(OnRichPresenceJoinRequested);
            _lobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
            _launchTarget = ParseConnectArgument(Environment.GetCommandLineArgs());
        }

        public MatchmakingKind Kind => MatchmakingKind.SteamLobby;
        public string DefaultJoinTarget => string.Empty;
        public IReadOnlyReactiveProperty<string> SessionCode => _sessionCode;
        public bool SupportsInvites => true;
        public CSteamID CurrentLobby => _lobby;

        public event Action<string> JoinRequested;

        public async UniTask<DisconnectReason> PrepareHostAsync(HostRequest request, CancellationToken cancellationToken)
        {
            ELobbyType type = request.Visibility == LobbyVisibility.Private
                ? ELobbyType.k_ELobbyTypePrivate
                : ELobbyType.k_ELobbyTypeFriendsOnly;

            LobbyCreated_t created = await SteamCallResults.AwaitResult<LobbyCreated_t>(
                SteamMatchmaking.CreateLobby(type, request.MaxPlayers), cancellationToken);
            if (created.m_eResult != EResult.k_EResultOK)
            {
                Debug.LogWarning($"[Steam] CreateLobby failed: {created.m_eResult}");
                return DisconnectReason.PlatformError;
            }

            _lobby = new CSteamID(created.m_ulSteamIDLobby);
            _lobbyHost = SteamUser.GetSteamID();
            _isLobbyOwner = true;
            SteamMatchmaking.SetLobbyData(_lobby, KeyName, request.SessionName);
            SteamMatchmaking.SetLobbyData(_lobby, KeyVersion, _settings.BuildVersion);
            SteamMatchmaking.SetLobbyData(_lobby, KeyHost, _lobbyHost.m_SteamID.ToString());
            SteamMatchmaking.SetLobbyJoinable(_lobby, true);
            _sessionCode.Value = _lobby.m_SteamID.ToString();
            Debug.Log($"[Steam] Lobby {_lobby} created ({type}, max {request.MaxPlayers}).");
            return DisconnectReason.None;
        }

        public async UniTask<DisconnectReason> PrepareJoinAsync(string target, CancellationToken cancellationToken)
        {
            if (!ulong.TryParse(target?.Trim(), out ulong lobbyId))
            {
                return DisconnectReason.PlatformError;
            }

            LobbyEnter_t entered = await SteamCallResults.AwaitResult<LobbyEnter_t>(
                SteamMatchmaking.JoinLobby(new CSteamID(lobbyId)), cancellationToken);
            var response = (EChatRoomEnterResponse)entered.m_EChatRoomEnterResponse;
            if (response != EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Debug.LogWarning($"[Steam] JoinLobby failed: {response}");
                return response == EChatRoomEnterResponse.k_EChatRoomEnterResponseFull
                    ? DisconnectReason.ServerFull
                    : DisconnectReason.PlatformError;
            }

            _lobby = new CSteamID(entered.m_ulSteamIDLobby);
            _isLobbyOwner = false;
            _lobbyHost = SteamMatchmaking.GetLobbyOwner(_lobby);
            if (!_lobbyHost.IsValid() || _lobbyHost == SteamUser.GetSteamID())
            {
                Leave();
                return DisconnectReason.PlatformError;
            }

            // Netcode connects peer-to-peer to the lobby owner's SteamID.
            _transport.SteamTransport.ConnectToSteamID = _lobbyHost.m_SteamID;
            _sessionCode.Value = _lobby.m_SteamID.ToString();
            Debug.Log($"[Steam] Joined lobby {_lobby}, host {_lobbyHost}.");
            return DisconnectReason.None;
        }

        public void PublishSessionState(PublicSessionState state)
        {
            if (!_isLobbyOwner || !_lobby.IsValid())
            {
                return;
            }

            SteamMatchmaking.SetLobbyJoinable(_lobby, state.IsJoinable);
            SteamMatchmaking.SetLobbyMemberLimit(_lobby, state.MaxPlayers);
            SteamMatchmaking.SetLobbyData(_lobby, KeyPlayers, state.PlayerCount.ToString());
            SteamMatchmaking.SetLobbyData(_lobby, KeyPhase, state.Phase.ToString());
        }

        public void Leave()
        {
            if (!_lobby.IsValid() || !SteamClient.IsInitialized)
            {
                return;
            }

            if (_isLobbyOwner)
            {
                // Stop new joins immediately; members leave on their own when the session drops.
                SteamMatchmaking.SetLobbyJoinable(_lobby, false);
            }

            SteamMatchmaking.LeaveLobby(_lobby);
            Debug.Log($"[Steam] Left lobby {_lobby}.");
            _lobby = CSteamID.Nil;
            _lobbyHost = CSteamID.Nil;
            _isLobbyOwner = false;
            _sessionCode.Value = string.Empty;
        }

        public void OpenInviteDialog()
        {
            if (!_lobby.IsValid())
            {
                return;
            }

            if (SteamUtils.IsOverlayEnabled())
            {
                SteamFriends.ActivateGameOverlayInviteDialog(_lobby);
                return;
            }

            // Steam injects the overlay only into games it launches itself (or that init Steam before the
            // graphics device). A test build started directly from its exe with AppID 480 has no overlay,
            // so fall back to inviting through the Steam client / sharing the lobby id.
            GUIUtility.systemCopyBuffer = _lobby.m_SteamID.ToString();
            Debug.Log("[Steam] Overlay unavailable; lobby id copied to clipboard.");
            _notifier.ShowMessage("Invite",
                "Steam overlay is not available for this launch.\n" +
                "Invite from the Steam friends list (right click > Invite to game),\n" +
                "or let the friend use \"Join Game\" on you.\n" +
                $"Lobby ID {_lobby.m_SteamID} is copied to the clipboard.");
        }

        public bool TryConsumeLaunchJoinTarget(out string target)
        {
            target = _launchTarget;
            _launchTarget = null;
            return !string.IsNullOrEmpty(target);
        }

        public void Dispose()
        {
            Leave();
            _lobbyJoinRequested.Dispose();
            _richPresenceJoinRequested.Dispose();
            _lobbyChatUpdate.Dispose();
        }

        private void OnLobbyJoinRequested(GameLobbyJoinRequested_t request)
        {
            JoinRequested?.Invoke(request.m_steamIDLobby.m_SteamID.ToString());
        }

        private void OnRichPresenceJoinRequested(GameRichPresenceJoinRequested_t request)
        {
            string target = ParseConnectArgument(request.m_rgchConnect.Split(' '));
            if (!string.IsNullOrEmpty(target))
            {
                JoinRequested?.Invoke(target);
            }
        }

        private void OnLobbyChatUpdate(LobbyChatUpdate_t update)
        {
            const uint leftMask = (uint)(EChatMemberStateChange.k_EChatMemberStateChangeLeft
                                         | EChatMemberStateChange.k_EChatMemberStateChangeDisconnected);
            bool hostLeft = update.m_ulSteamIDLobby == _lobby.m_SteamID
                            && update.m_ulSteamIDUserChanged == _lobbyHost.m_SteamID
                            && (update.m_rgfChatMemberStateChange & leftMask) != 0;
            if (hostLeft && !_isLobbyOwner)
            {
                // The lobby must not outlive its host (Steam would otherwise promote a member to owner).
                Leave();
            }
        }

        private static string ParseConnectArgument(string[] args)
        {
            if (args == null)
            {
                return null;
            }

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == ConnectArg)
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
