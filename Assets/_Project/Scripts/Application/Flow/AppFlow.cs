using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Application.Ports;
using Game.Domain.Reactive;
using Game.Domain.Session;

namespace Game.Application.Flow
{
    /// <summary>
    /// Application-level coordinator: starts/ends sessions on user request, returns to the menu with a
    /// readable reason when a session ends, and mirrors session state into platform services
    /// (lobby joinability, rich presence). Knows nothing about the network library or the transport.
    /// </summary>
    public sealed class AppFlow : IDisposable
    {
        private readonly ISessionService _session;
        private readonly IPlayerRoster _roster;
        private readonly IMatchmakingService _matchmaking;
        private readonly IRichPresence _presence;
        private readonly ISceneNavigator _navigator;
        private readonly IUserNotifier _notifier;
        private readonly AchievementTracker _achievements;
        private readonly CompositeDisposable _subscriptions = new CompositeDisposable();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        public AppFlow(ISessionService session, IPlayerRoster roster, IMatchmakingService matchmaking,
            IRichPresence presence, ISceneNavigator navigator, IUserNotifier notifier, AchievementTracker achievements)
        {
            _session = session;
            _roster = roster;
            _matchmaking = matchmaking;
            _presence = presence;
            _navigator = navigator;
            _notifier = notifier;
            _achievements = achievements;
        }

        public bool IsBusy => _session.Status.Value != ConnectionStatus.Offline;

        public void Initialize()
        {
            _session.Ended += OnSessionEnded;
            _roster.Changed += RefreshPublicState;
            _matchmaking.JoinRequested += OnPlatformJoinRequested;
            _session.Status.Subscribe(_ => RefreshPublicState()).AddTo(_subscriptions);
            _session.Phase.Subscribe(_ => RefreshPublicState()).AddTo(_subscriptions);

            if (_matchmaking.TryConsumeLaunchJoinTarget(out string launchTarget))
            {
                JoinNextFrameAsync(launchTarget).Forget();
            }
        }

        /// <summary>Initialize runs during boot; the network library is ready only from the next frame.</summary>
        private async UniTaskVoid JoinNextFrameAsync(string target)
        {
            await UniTask.NextFrame(_lifetime.Token);
            Join(target);
        }

        public void Host(HostRequest request)
        {
            if (IsBusy)
            {
                return;
            }

            _session.HostAsync(request, _lifetime.Token).Forget();
        }

        public void Join(string target)
        {
            if (IsBusy)
            {
                return;
            }

            _session.JoinAsync(target, _lifetime.Token).Forget();
        }

        /// <summary>Leaves the session or cancels a pending connection.</summary>
        public void Leave() => _session.Leave();

        public void Dispose()
        {
            _session.Ended -= OnSessionEnded;
            _roster.Changed -= RefreshPublicState;
            _matchmaking.JoinRequested -= OnPlatformJoinRequested;
            _subscriptions.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        private void OnSessionEnded(DisconnectReason reason)
        {
            // Stats are stored in batches: on achievement unlock and when a session ends.
            _achievements.Flush();
            _matchmaking.Leave();
            _presence.Set(new PresenceState(PresenceKind.Menu, 0, 0));
            _navigator.LoadMenu();

            if (!DisconnectMessages.IsSilent(reason))
            {
                _notifier.ShowMessage("Disconnected", DisconnectMessages.Describe(reason));
            }
        }

        private void OnPlatformJoinRequested(string target)
        {
            if (IsBusy)
            {
                _notifier.ShowMessage("Join", "Leave the current session before joining another one.");
                return;
            }

            Join(target);
        }

        private void RefreshPublicState()
        {
            ConnectionStatus status = _session.Status.Value;
            if (status != ConnectionStatus.Hosting && status != ConnectionStatus.Connected)
            {
                return;
            }

            SessionPhase phase = _session.Phase.Value;
            int players = _roster.Players.Count;
            PresenceKind kind = phase == SessionPhase.InGame || phase == SessionPhase.Loading
                ? PresenceKind.InGame
                : PresenceKind.Lobby;

            _presence.Set(new PresenceState(kind, players, _session.MaxPlayers));

            if (_session.IsHost)
            {
                _matchmaking.PublishSessionState(new PublicSessionState(players, _session.MaxPlayers, phase));
            }
        }
    }
}
