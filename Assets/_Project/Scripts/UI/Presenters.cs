using System;
using Game.Application.Flow;
using Game.Application.Ports;
using Game.Domain.Session;
using UnityEngine;

namespace Game.UI
{
    public sealed class MainMenuPresenter : IDisposable
    {
        private static readonly int[] MaxPlayerOptions = { 2, 3, 4 };

        private readonly MainMenuView _view;
        private readonly AppFlow _flow;
        private readonly IPlayerIdentity _identity;
        private readonly AppSettings _settings;
        private int _maxPlayersIndex = MaxPlayerOptions.Length - 1;
        private LobbyVisibility _visibility = LobbyVisibility.FriendsOnly;

        public MainMenuPresenter(MainMenuView view, AppFlow flow, IPlayerIdentity identity, AppSettings settings)
        {
            _view = view;
            _flow = flow;
            _identity = identity;
            _settings = settings;

            _view.HostClicked += OnHost;
            _view.JoinClicked += OnJoin;
            _view.CancelClicked += _flow.Leave;
            _view.QuitClicked += UnityEngine.Application.Quit;
            _view.MaxPlayersClicked += OnMaxPlayers;
            _view.VisibilityClicked += OnVisibility;
            _view.SetLobbySettings(MaxPlayers, _visibility);
        }

        private int MaxPlayers => Mathf.Min(MaxPlayerOptions[_maxPlayersIndex], _settings.MaxPlayers);

        public void Render(ConnectionStatus status)
        {
            switch (status)
            {
                case ConnectionStatus.StartingHost:
                    _view.SetBusy(true, "Starting host...");
                    break;
                case ConnectionStatus.Connecting:
                    _view.SetBusy(true, "Connecting...");
                    break;
                default:
                    _view.SetBusy(false, string.Empty);
                    break;
            }
        }

        public void Dispose()
        {
            _view.HostClicked -= OnHost;
            _view.JoinClicked -= OnJoin;
            _view.CancelClicked -= _flow.Leave;
            _view.MaxPlayersClicked -= OnMaxPlayers;
            _view.VisibilityClicked -= OnVisibility;
        }

        private void ApplyName()
        {
            if (_view.NameInput != null && _identity.CanRename)
            {
                _identity.Rename(_view.NameInput.text);
            }
        }

        private void OnHost()
        {
            ApplyName();
            string sessionName = _view.SessionNameInput != null ? _view.SessionNameInput.text : _identity.DisplayName;
            _flow.Host(new HostRequest(sessionName, MaxPlayers, _visibility));
        }

        private void OnJoin()
        {
            ApplyName();
            _flow.Join(_view.TargetInput.text);
        }

        private void OnMaxPlayers()
        {
            _maxPlayersIndex = (_maxPlayersIndex + 1) % MaxPlayerOptions.Length;
            _view.SetLobbySettings(MaxPlayers, _visibility);
        }

        private void OnVisibility()
        {
            _visibility = _visibility == LobbyVisibility.FriendsOnly ? LobbyVisibility.Private : LobbyVisibility.FriendsOnly;
            _view.SetLobbySettings(MaxPlayers, _visibility);
        }
    }

    public sealed class LobbyPresenter : IDisposable
    {
        private readonly LobbyView _view;
        private readonly ISessionService _session;
        private readonly IPlayerRoster _roster;
        private readonly IMatchmakingService _matchmaking;
        private readonly AppFlow _flow;

        public LobbyPresenter(LobbyView view, ISessionService session, IPlayerRoster roster, IMatchmakingService matchmaking, AppFlow flow)
        {
            _view = view;
            _session = session;
            _roster = roster;
            _matchmaking = matchmaking;
            _flow = flow;
            _view.StartClicked += _session.StartGame;
            _view.InviteClicked += _matchmaking.OpenInviteDialog;
            _view.LeaveClicked += _flow.Leave;
        }

        public void Render()
        {
            _view.Render(_matchmaking.SessionCode.Value, _roster.Players, _session.MaxPlayers, _session.IsHost,
                _matchmaking.SupportsInvites);
        }

        public void Dispose()
        {
            _view.StartClicked -= _session.StartGame;
            _view.InviteClicked -= _matchmaking.OpenInviteDialog;
            _view.LeaveClicked -= _flow.Leave;
        }
    }

    public sealed class PlayerListPresenter : IDisposable
    {
        private readonly PlayerListView _view;
        private readonly ISessionService _session;
        private readonly IPlayerRoster _roster;
        private readonly ISessionCommands _commands;

        public PlayerListPresenter(PlayerListView view, ISessionService session, IPlayerRoster roster, ISessionCommands commands)
        {
            _view = view;
            _session = session;
            _roster = roster;
            _commands = commands;
            _view.KickClicked += _commands.Kick;
        }

        public void Render() => _view.Render(_roster.Players, _session.LocalClientId, _session.IsHost);

        public void Dispose() => _view.KickClicked -= _commands.Kick;
    }
}
