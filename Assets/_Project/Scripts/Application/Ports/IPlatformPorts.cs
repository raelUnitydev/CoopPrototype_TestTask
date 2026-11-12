using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Domain.Reactive;
using Game.Domain.Session;

namespace Game.Application.Ports
{
    public enum MatchmakingKind
    {
        DirectIp,
        SteamLobby
    }

    public readonly struct PublicSessionState
    {
        public int PlayerCount { get; }
        public int MaxPlayers { get; }
        public SessionPhase Phase { get; }

        public PublicSessionState(int playerCount, int maxPlayers, SessionPhase phase)
        {
            PlayerCount = playerCount;
            MaxPlayers = maxPlayers;
            Phase = phase;
        }

        /// <summary>New players may join while there is a free slot and no scene switch is running.</summary>
        public bool IsJoinable => PlayerCount < MaxPlayers && Phase != SessionPhase.Loading;
    }

    /// <summary>
    /// How peers find each other: direct IP:port or a Steam lobby. Implementations also configure
    /// their transport, so the session code never knows which transport is used.
    /// </summary>
    public interface IMatchmakingService
    {
        MatchmakingKind Kind { get; }
        string DefaultJoinTarget { get; }

        /// <summary>Human-readable code other players use to join (IP:port or lobby id).</summary>
        IReadOnlyReactiveProperty<string> SessionCode { get; }

        bool SupportsInvites { get; }

        /// <summary>Raised when the platform asks us to join a session (overlay invite, "Join Game").</summary>
        event Action<string> JoinRequested;

        UniTask<DisconnectReason> PrepareHostAsync(HostRequest request, CancellationToken cancellationToken);
        UniTask<DisconnectReason> PrepareJoinAsync(string target, CancellationToken cancellationToken);

        /// <summary>Host only: mirrors session state into the platform (lobby joinable flag, member count).</summary>
        void PublishSessionState(PublicSessionState state);

        void Leave();
        void OpenInviteDialog();

        /// <summary>Join target passed by the platform at launch (e.g. "+connect_lobby").</summary>
        bool TryConsumeLaunchJoinTarget(out string target);
    }

    public interface IPlayerIdentity
    {
        PlayerId PlayerId { get; }
        string DisplayName { get; }
        bool CanRename { get; }
        void Rename(string displayName);
    }

    public enum PresenceKind
    {
        Menu,
        Lobby,
        InGame
    }

    public readonly struct PresenceState
    {
        public PresenceKind Kind { get; }
        public int Players { get; }
        public int MaxPlayers { get; }

        public PresenceState(PresenceKind kind, int players, int maxPlayers)
        {
            Kind = kind;
            Players = players;
            MaxPlayers = maxPlayers;
        }
    }

    public interface IRichPresence
    {
        void Set(PresenceState state);
        void Clear();
    }

    /// <summary>Per-user stats/achievements of the local player (Steam stats belong to the local user).</summary>
    public interface IStatsService
    {
        /// <returns>New value of the stat.</returns>
        int Increment(string statId, int amount);
        bool IsUnlocked(string achievementId);
        void Unlock(string achievementId);

        /// <summary>Persists pending changes. Expensive / rate limited on Steam, so call it in batches.</summary>
        void Store();
    }
}
