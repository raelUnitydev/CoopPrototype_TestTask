using System;
using Game.Application.Flow;
using Game.Application.Ports;
using Game.Domain.Reactive;
using Game.Domain.Session;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Builds the persistent UI and routes between screens based on session state
    /// (Offline -> menu, Lobby phase -> lobby, Loading / scene sync -> loading screen, InGame -> HUD).
    /// Messages from other layers arrive through <see cref="UiNotifications"/>.
    /// </summary>
    public sealed class UiRoot : IDisposable
    {
        private const float FeedbackDuration = 2.5f;

        private readonly ISessionService _session;
        private readonly IPlayerRoster _roster;
        private readonly ISessionCommands _commands;
        private readonly IMatchmakingService _matchmaking;
        private readonly InputGate _inputGate;
        private readonly UiNotifications _notifications;
        private readonly CompositeDisposable _subscriptions = new CompositeDisposable();
        private readonly GameObject _canvasObject;

        private readonly MainMenuView _menuView;
        private readonly LobbyView _lobbyView;
        private readonly LoadingView _loadingView;
        private readonly PlayerListView _playerListView;
        private readonly PauseView _pauseView;
        private readonly HudView _hudView;
        private readonly MessageView _messageView;

        private readonly MainMenuPresenter _menuPresenter;
        private readonly LobbyPresenter _lobbyPresenter;
        private readonly PlayerListPresenter _playerListPresenter;

        private float _feedbackHideTime;
        private bool _gameplayBlocked;
        private float _nextListRefresh;

        public UiRoot(ISessionService session, IPlayerRoster roster, ISessionCommands commands, IMatchmakingService matchmaking,
            IPlayerIdentity identity, AppFlow flow, AppSettings settings, InputGate inputGate, UiNotifications notifications)
        {
            _notifications = notifications;
            _session = session;
            _roster = roster;
            _commands = commands;
            _matchmaking = matchmaking;
            _inputGate = inputGate;

            _canvasObject = CreateCanvas();
            Transform root = _canvasObject.transform;

            _hudView = new HudView(root);
            _loadingView = new LoadingView(root);
            _menuView = new MainMenuView(root, matchmaking.Kind, identity.DisplayName, identity.CanRename, matchmaking.DefaultJoinTarget);
            _lobbyView = new LobbyView(root);
            _playerListView = new PlayerListView(root);
            _pauseView = new PauseView(root);
            _messageView = new MessageView(root);
            _messageView.SetVisible(false);
            _playerListView.SetVisible(false);
            _pauseView.SetVisible(false);

            _menuPresenter = new MainMenuPresenter(_menuView, flow, identity, settings);
            _lobbyPresenter = new LobbyPresenter(_lobbyView, session, roster, matchmaking, flow);
            _playerListPresenter = new PlayerListPresenter(_playerListView, session, roster, commands);

            _pauseView.ResumeClicked += ClosePause;
            _pauseView.PlayersClicked += () => _playerListView.SetVisible(true);
            _pauseView.LeaveClicked += () =>
            {
                ClosePause();
                flow.Leave();
            };

            _canvasObject.AddComponent<UiTicker>().Tick = Tick;
        }

        public void Initialize()
        {
            _session.Status.Subscribe(_ => Route()).AddTo(_subscriptions);
            _session.Phase.Subscribe(_ => Route()).AddTo(_subscriptions);
            _session.IsSceneLoading.Subscribe(_ => Route()).AddTo(_subscriptions);
            _matchmaking.SessionCode.Subscribe(_ => _lobbyPresenter.Render()).AddTo(_subscriptions);
            _roster.Changed += OnRosterChanged;
            _notifications.MessageRequested += ShowMessage;
            _notifications.FeedbackRequested += ShowFeedback;
        }

        private void ShowMessage(string title, string message) => _messageView.Show(title, message);

        private void ShowFeedback(string message)
        {
            _hudView.SetFeedback(message);
            _feedbackHideTime = Time.unscaledTime + FeedbackDuration;
        }

        public void Dispose()
        {
            _roster.Changed -= OnRosterChanged;
            _notifications.MessageRequested -= ShowMessage;
            _notifications.FeedbackRequested -= ShowFeedback;
            _subscriptions.Dispose();
            _menuPresenter.Dispose();
            _lobbyPresenter.Dispose();
            _playerListPresenter.Dispose();
            if (_canvasObject != null)
            {
                UnityEngine.Object.Destroy(_canvasObject);
            }
        }

        private bool IsInGame =>
            _session.Status.Value is ConnectionStatus.Hosting or ConnectionStatus.Connected
            && _session.Phase.Value == SessionPhase.InGame
            && !_session.IsSceneLoading.Value;

        private void Route()
        {
            ConnectionStatus status = _session.Status.Value;
            SessionPhase phase = _session.Phase.Value;
            bool online = status is ConnectionStatus.Hosting or ConnectionStatus.Connected or ConnectionStatus.ShuttingDown;
            bool loading = online && (phase == SessionPhase.Loading || phase == SessionPhase.None
                                      || _session.IsSceneLoading.Value || status == ConnectionStatus.ShuttingDown);

            _menuView.SetVisible(!online);
            _menuPresenter.Render(status);
            _lobbyView.SetVisible(online && !loading && phase == SessionPhase.Lobby);
            _loadingView.SetVisible(loading);
            _hudView.SetVisible(IsInGame);

            if (!IsInGame)
            {
                _pauseView.SetVisible(false);
                _playerListView.SetVisible(false);
            }

            OnRosterChanged();
            UpdateInputGate();
        }

        private void OnRosterChanged()
        {
            if (_lobbyView.IsVisible)
            {
                _lobbyPresenter.Render();
            }

            if (_loadingView.IsVisible)
            {
                _loadingView.Render(_roster.Players);
            }

            if (_playerListView.IsVisible)
            {
                _playerListPresenter.Render();
            }
        }

        private void Tick()
        {
            if (_feedbackHideTime > 0f && Time.unscaledTime > _feedbackHideTime)
            {
                _feedbackHideTime = 0f;
                _hudView.SetFeedback(string.Empty);
            }

            Keyboard keyboard = Keyboard.current;
            if (IsInGame && keyboard != null)
            {
                if (keyboard.tabKey.wasPressedThisFrame)
                {
                    _playerListView.SetVisible(!_playerListView.IsVisible);
                    _playerListPresenter.Render();
                }

                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    if (_playerListView.IsVisible)
                    {
                        _playerListView.SetVisible(false);
                    }
                    else
                    {
                        _pauseView.SetVisible(!_pauseView.IsVisible);
                    }
                }
            }

            // Ping values change every second; the list is cheap to rebuild.
            if (_playerListView.IsVisible && Time.unscaledTime > _nextListRefresh)
            {
                _nextListRefresh = Time.unscaledTime + 1f;
                _playerListPresenter.Render();
            }

            UpdateInputGate();
        }

        private void ClosePause() => _pauseView.SetVisible(false);

        private void UpdateInputGate()
        {
            bool block = !IsInGame || _pauseView.IsVisible || _playerListView.IsVisible || _messageView.IsVisible;
            if (block == _gameplayBlocked)
            {
                return;
            }

            _gameplayBlocked = block;
            if (block)
            {
                _inputGate.Block();
            }
            else
            {
                _inputGate.Unblock();
            }
        }

        private static GameObject CreateCanvas()
        {
            var canvasObject = new GameObject("UI", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(canvasObject);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.transform.SetParent(canvasObject.transform, false);
            return canvasObject;
        }

        private sealed class UiTicker : MonoBehaviour
        {
            public Action Tick;

            private void Update() => Tick?.Invoke();
        }
    }
}
