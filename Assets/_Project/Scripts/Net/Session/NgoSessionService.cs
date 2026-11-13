using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Application.Flow;
using Game.Application.Ports;
using Game.Domain.Reactive;
using Game.Domain.Session;
using Game.Net.Scenes;
using Game.Net.Transport;
using Unity.Netcode;
using UnityEngine;

namespace Game.Net.Session
{
    /// <summary>
    /// <see cref="ISessionService"/> on top of Netcode for GameObjects. Owns the connection state machine
    /// (Offline -> StartingHost/Connecting -> Hosting/Connected -> ShuttingDown -> Offline) and translates
    /// every way a session can end into a single <see cref="Ended"/> event with a <see cref="DisconnectReason"/>.
    /// Transport-agnostic: the transport comes from <see cref="INetworkTransportProvider"/> and endpoint
    /// resolution (IP or Steam lobby) from <see cref="IMatchmakingService"/>.
    /// </summary>
    public sealed class NgoSessionService : ISessionService, IDisposable
    {
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan HostShutdownGrace = TimeSpan.FromMilliseconds(300);

        private readonly NetworkManager _networkManager;
        private readonly INetworkTransportProvider _transportProvider;
        private readonly IMatchmakingService _matchmaking;
        private readonly IPlayerIdentity _identity;
        private readonly AppSettings _settings;
        private readonly SessionStateHolder _holder;
        private readonly ServerPlayerRegistry _registry;
        private readonly ConnectionApprovalHandler _approval;
        private readonly NetworkSceneFlow _sceneFlow;
        private readonly ServerSessionEvents _serverEvents;
        private readonly NetworkObject _sessionStatePrefab;

        private readonly ReactiveProperty<ConnectionStatus> _status = new ReactiveProperty<ConnectionStatus>(ConnectionStatus.Offline);
        private readonly ReactiveProperty<SessionPhase> _phase = new ReactiveProperty<SessionPhase>(SessionPhase.None);

        private CancellationTokenSource _sessionCts;
        private UniTaskCompletionSource<DisconnectReason> _connectCompletion;
        private DisconnectReason _pendingReason;
        private bool _endRaised = true;
        private bool _netcodeRunning;
        private int _maxPlayers;

        public NgoSessionService(NetworkManager networkManager, INetworkTransportProvider transportProvider,
            IMatchmakingService matchmaking, IPlayerIdentity identity, AppSettings settings, SessionStateHolder holder,
            ServerPlayerRegistry registry, ConnectionApprovalHandler approval, NetworkSceneFlow sceneFlow,
            ServerSessionEvents serverEvents, NetPrefabs prefabs)
        {
            _networkManager = networkManager;
            _transportProvider = transportProvider;
            _matchmaking = matchmaking;
            _identity = identity;
            _settings = settings;
            _holder = holder;
            _registry = registry;
            _approval = approval;
            _sceneFlow = sceneFlow;
            _serverEvents = serverEvents;
            _sessionStatePrefab = prefabs.SessionState;
            _maxPlayers = settings.MaxPlayers;
        }

        public IReadOnlyReactiveProperty<ConnectionStatus> Status => _status;
        public IReadOnlyReactiveProperty<SessionPhase> Phase => _phase;
        public IReadOnlyReactiveProperty<bool> IsSceneLoading => _sceneFlow.IsLocalSceneLoading;
        public bool IsHost => _networkManager.IsHost;
        public ulong LocalClientId => _networkManager.LocalClientId;
        public int MaxPlayers => _maxPlayers;

        public event Action<DisconnectReason> Ended;

        public void Initialize()
        {
            _networkManager.NetworkConfig.NetworkTransport = _transportProvider.Transport;
            _networkManager.NetworkConfig.ConnectionApproval = true;
            _networkManager.ConnectionApprovalCallback = _approval.Handle;
            _networkManager.OnConnectionEvent += OnConnectionEvent;
            _networkManager.OnClientStopped += OnNetcodeStopped;
            _networkManager.OnTransportFailure += OnTransportFailure;

            _holder.Attached += OnStateAttached;
            _holder.Detached += OnStateDetached;
            _holder.HostClosing += OnHostClosing;

            UnityEngine.Application.quitting += OnApplicationQuitting;
        }

        public async UniTask<DisconnectReason> HostAsync(HostRequest request, CancellationToken cancellationToken)
        {
            if (_status.Value != ConnectionStatus.Offline)
            {
                return DisconnectReason.StartFailed;
            }

            CancellationToken token = BeginSession(ConnectionStatus.StartingHost, cancellationToken);
            _maxPlayers = Mathf.Clamp(request.MaxPlayers, 1, _settings.MaxPlayers);
            _approval.Configure(_maxPlayers);

            DisconnectReason prepared = await PrepareAsync(() => _matchmaking.PrepareHostAsync(request, token), token);
            if (prepared != DisconnectReason.None)
            {
                return EndSession(prepared);
            }

            _networkManager.NetworkConfig.ConnectionData = CreatePayload();
            if (!_networkManager.StartHost())
            {
                return EndSession(DisconnectReason.StartFailed);
            }

            _netcodeRunning = true;

            // The host's own connection may or may not go through approval; make sure it is registered.
            if (!_registry.TryGet(NetworkManager.ServerClientId, out _))
            {
                _registry.Add(NetworkManager.ServerClientId, LocalConnectionRequest());
            }

            SpawnSessionState();
            _sceneFlow.ServerLoadLobby();
            _status.Value = ConnectionStatus.Hosting;
            Debug.Log($"[Session] Hosting '{request.SessionName}' for up to {_maxPlayers} players.");
            return DisconnectReason.None;
        }

        public async UniTask<DisconnectReason> JoinAsync(string target, CancellationToken cancellationToken)
        {
            if (_status.Value != ConnectionStatus.Offline)
            {
                return DisconnectReason.StartFailed;
            }

            CancellationToken token = BeginSession(ConnectionStatus.Connecting, cancellationToken);

            DisconnectReason prepared = await PrepareAsync(() => _matchmaking.PrepareJoinAsync(target, token), token);
            if (prepared != DisconnectReason.None)
            {
                return EndSession(prepared);
            }

            _networkManager.NetworkConfig.ConnectionData = CreatePayload();
            _connectCompletion = new UniTaskCompletionSource<DisconnectReason>();
            if (!_networkManager.StartClient())
            {
                return EndSession(DisconnectReason.StartFailed);
            }

            _netcodeRunning = true;

            // The transport has its own timeout, but a Steam P2P connect may hang longer; cap it here.
            (bool isTimeout, DisconnectReason result) = await _connectCompletion.Task
                .TimeoutWithoutException(ConnectTimeout, DelayType.Realtime);
            if (isTimeout)
            {
                _pendingReason = DisconnectReason.ConnectTimeout;
                _networkManager.Shutdown();
                return DisconnectReason.ConnectTimeout;
            }

            return result;
        }

        public void Leave()
        {
            switch (_status.Value)
            {
                case ConnectionStatus.Offline:
                case ConnectionStatus.ShuttingDown:
                    return;
            }

            _pendingReason = DisconnectReason.LeftByUser;
            _sessionCts?.Cancel();

            if (!_netcodeRunning)
            {
                // Still in the matchmaking stage; PrepareAsync observes the cancellation and ends the session.
                return;
            }

            if (_networkManager.IsHost)
            {
                ShutdownHostGracefullyAsync().Forget();
            }
            else
            {
                _status.Value = ConnectionStatus.ShuttingDown;
                _networkManager.Shutdown();
            }
        }

        public void StartGame()
        {
            if (_networkManager.IsServer)
            {
                _sceneFlow.ServerStartGame();
            }
        }

        public void Dispose()
        {
            UnityEngine.Application.quitting -= OnApplicationQuitting;
            if (_networkManager != null)
            {
                _networkManager.OnConnectionEvent -= OnConnectionEvent;
                _networkManager.OnClientStopped -= OnNetcodeStopped;
                _networkManager.OnTransportFailure -= OnTransportFailure;
                _networkManager.ConnectionApprovalCallback = null;
            }

            _holder.Attached -= OnStateAttached;
            _holder.Detached -= OnStateDetached;
            _holder.HostClosing -= OnHostClosing;
            _sessionCts?.Cancel();
            _sessionCts?.Dispose();
        }

        private CancellationToken BeginSession(ConnectionStatus status, CancellationToken external)
        {
            _sessionCts?.Dispose();
            _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(external);
            _pendingReason = DisconnectReason.None;
            _endRaised = false;
            _netcodeRunning = false;
            _registry.Clear();
            _status.Value = status;
            return _sessionCts.Token;
        }

        private async UniTask<DisconnectReason> PrepareAsync(Func<UniTask<DisconnectReason>> prepare, CancellationToken token)
        {
            try
            {
                DisconnectReason reason = await prepare();
                token.ThrowIfCancellationRequested();
                return reason;
            }
            catch (OperationCanceledException)
            {
                return DisconnectReason.LeftByUser;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return DisconnectReason.PlatformError;
            }
        }

        private DisconnectReason EndSession(DisconnectReason reason)
        {
            if (_endRaised)
            {
                return reason;
            }

            _endRaised = true;
            _netcodeRunning = false;
            _registry.Clear();
            _connectCompletion?.TrySetResult(reason);
            _connectCompletion = null;
            _phase.Value = SessionPhase.None;
            _status.Value = ConnectionStatus.Offline;
            Debug.Log($"[Session] Ended: {reason}.");
            Ended?.Invoke(reason);
            return reason;
        }

        private async UniTaskVoid ShutdownHostGracefullyAsync()
        {
            _status.Value = ConnectionStatus.ShuttingDown;
            // Tell clients the shutdown is intentional so they show "host closed the session"
            // instead of "connection lost", then give the reliable message time to leave.
            _holder.State?.ServerNotifyHostClosing();
            await UniTask.Delay(HostShutdownGrace, DelayType.Realtime);
            _networkManager.Shutdown();
        }

        private void SpawnSessionState()
        {
            NetworkObject state = NetworkObject.InstantiateAndSpawn(_sessionStatePrefab.gameObject, _networkManager);
            if (state == null)
            {
                throw new InvalidOperationException("Failed to spawn the session state object.");
            }
        }

        private byte[] CreatePayload() => ConnectionPayloadCodec.Encode(LocalConnectionRequest());

        private ConnectionRequest LocalConnectionRequest()
        {
            return new ConnectionRequest(_settings.BuildVersion, _identity.PlayerId, _identity.DisplayName);
        }

        private void OnConnectionEvent(NetworkManager networkManager, ConnectionEventData data)
        {
            switch (data.EventType)
            {
                case ConnectionEvent.ClientConnected:
                    if (networkManager.IsServer)
                    {
                        ServerOnClientConnected(data.ClientId);
                    }
                    else if (data.ClientId == networkManager.LocalClientId)
                    {
                        _status.Value = ConnectionStatus.Connected;
                        _connectCompletion?.TrySetResult(DisconnectReason.None);
                        Debug.Log($"[Session] Connected as client {data.ClientId}.");
                    }

                    break;

                case ConnectionEvent.ClientDisconnected:
                    if (networkManager.IsServer && data.ClientId != NetworkManager.ServerClientId)
                    {
                        ServerOnClientDisconnected(data.ClientId);
                    }

                    break;
            }
        }

        private void ServerOnClientConnected(ulong clientId)
        {
            SessionNetworkState state = _holder.State;
            if (state == null || !_registry.TryGet(clientId, out ConnectionRequest request))
            {
                return;
            }

            // Late joiners are marked loaded once they finish scene synchronization.
            state.ServerAddPlayer(clientId, request, isLoaded: false);
            _sceneFlow.ServerOnClientRegistered(clientId);
            Debug.Log($"[Session] '{request.DisplayName}' joined (client {clientId}).");
        }

        private void ServerOnClientDisconnected(ulong clientId)
        {
            _registry.Remove(clientId);
            _holder.State?.ServerRemovePlayer(clientId);
            _sceneFlow.ServerOnClientLeft(clientId);
            _serverEvents.RaiseClientLeft(clientId);
            Debug.Log($"[Session] Client {clientId} left.");
        }

        private void OnStateAttached(SessionNetworkState state)
        {
            state.PhaseChanged += OnPhaseChanged;
            _phase.Value = state.Phase;

            if (_networkManager.IsServer)
            {
                // The host connected before the state object existed: add it (and anyone else) now.
                foreach (ulong clientId in _networkManager.ConnectedClientsIds)
                {
                    if (_registry.TryGet(clientId, out ConnectionRequest request))
                    {
                        state.ServerAddPlayer(clientId, request, isLoaded: true);
                    }
                }
            }
        }

        private void OnStateDetached()
        {
            _phase.Value = SessionPhase.None;
        }

        private void OnPhaseChanged(SessionPhase phase) => _phase.Value = phase;

        private void OnHostClosing() => _pendingReason = DisconnectReason.HostLeft;

        private void OnTransportFailure()
        {
            Debug.LogWarning("[Session] Transport failure.");
            if (_pendingReason == DisconnectReason.None)
            {
                _pendingReason = DisconnectReason.ConnectionLost;
            }
        }

        private void OnNetcodeStopped(bool wasHost)
        {
            if (_holder.State != null)
            {
                _holder.State.PhaseChanged -= OnPhaseChanged;
            }

            EndSession(ResolveEndReason(wasHost));
        }

        private DisconnectReason ResolveEndReason(bool wasHost)
        {
            // A reason sent by the host (rejection, kick) wins over everything we inferred locally.
            if (!wasHost && DisconnectReasonCodec.TryDecode(_networkManager.DisconnectReason, out DisconnectReason serverReason))
            {
                return serverReason;
            }

            if (_pendingReason != DisconnectReason.None)
            {
                return _pendingReason;
            }

            if (wasHost)
            {
                return DisconnectReason.LeftByUser;
            }

            // Never got connected: the host is unreachable or did not answer in time.
            return _status.Value == ConnectionStatus.Connecting ? DisconnectReason.ConnectTimeout : DisconnectReason.ConnectionLost;
        }

        private void OnApplicationQuitting()
        {
            if (_netcodeRunning && _networkManager.IsHost)
            {
                // Best effort on Alt+F4: queue the notice, Shutdown flushes the outgoing queue.
                _holder.State?.ServerNotifyHostClosing();
                _networkManager.Shutdown();
            }
        }
    }
}
