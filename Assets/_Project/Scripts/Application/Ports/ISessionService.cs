using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Domain.Reactive;
using Game.Domain.Session;

namespace Game.Application.Ports
{
    public readonly struct HostRequest
    {
        public string SessionName { get; }
        public int MaxPlayers { get; }
        public LobbyVisibility Visibility { get; }

        public HostRequest(string sessionName, int maxPlayers, LobbyVisibility visibility)
        {
            SessionName = sessionName;
            MaxPlayers = maxPlayers;
            Visibility = visibility;
        }
    }

    /// <summary>
    /// Network session facade. Gameplay/UI never talk to the network library or transport directly.
    /// Async methods return <see cref="DisconnectReason.None"/> on success.
    /// </summary>
    public interface ISessionService
    {
        IReadOnlyReactiveProperty<ConnectionStatus> Status { get; }
        IReadOnlyReactiveProperty<SessionPhase> Phase { get; }

        /// <summary>True while this peer loads or synchronizes a network scene.</summary>
        IReadOnlyReactiveProperty<bool> IsSceneLoading { get; }
        bool IsHost { get; }
        ulong LocalClientId { get; }
        int MaxPlayers { get; }

        /// <summary>Raised once when an established or pending session ends for the local player.</summary>
        event Action<DisconnectReason> Ended;

        UniTask<DisconnectReason> HostAsync(HostRequest request, CancellationToken cancellationToken);
        UniTask<DisconnectReason> JoinAsync(string target, CancellationToken cancellationToken);
        void Leave();

        /// <summary>Host only: moves everybody from the lobby to the level.</summary>
        void StartGame();
    }

    /// <summary>Replicated list of players in the session.</summary>
    public interface IPlayerRoster
    {
        IReadOnlyList<PlayerSnapshot> Players { get; }
        event Action Changed;
        bool TryGet(ulong clientId, out PlayerSnapshot player);
    }

    /// <summary>Session-level commands of the local player.</summary>
    public interface ISessionCommands
    {
        void RequestColorChange();

        /// <summary>Host only.</summary>
        void Kick(ulong clientId);
    }
}
