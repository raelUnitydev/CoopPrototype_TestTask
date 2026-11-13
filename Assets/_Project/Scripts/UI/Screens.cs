using System;
using System.Collections.Generic;
using Game.Application.Ports;
using Game.Domain.Session;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>Base view: a panel that can be shown/hidden. Views expose events and setters, no logic.</summary>
    public abstract class ViewBase
    {
        protected ViewBase(RectTransform root)
        {
            Root = root;
        }

        public RectTransform Root { get; }

        public bool IsVisible => Root.gameObject.activeSelf;

        public void SetVisible(bool visible)
        {
            if (Root.gameObject.activeSelf != visible)
            {
                Root.gameObject.SetActive(visible);
            }
        }
    }

    public sealed class MainMenuView : ViewBase
    {
        private readonly Text _status;
        private readonly Text _maxPlayersLabel;
        private readonly Text _visibilityLabel;
        private readonly Button _cancel;
        private readonly List<Selectable> _interactive = new List<Selectable>();

        public MainMenuView(Transform parent, MatchmakingKind kind, string playerName, bool canRename, string defaultTarget)
            : base(UiFactory.CreatePanel("MainMenu", parent, new Vector2(520, 560)))
        {
            UiFactory.CreateText(Root, "Coop Physics Prototype", 30, TextAnchor.MiddleCenter, 44);

            if (canRename)
            {
                UiFactory.CreateText(Root, "Name", 18);
                NameInput = UiFactory.CreateInput(Root, playerName, "Player name");
                _interactive.Add(NameInput);
            }
            else
            {
                UiFactory.CreateText(Root, $"Signed in as {playerName}", 20);
            }

            if (kind == MatchmakingKind.SteamLobby)
            {
                UiFactory.CreateText(Root, "Lobby name", 18);
                SessionNameInput = UiFactory.CreateInput(Root, $"{playerName}'s lobby", "Lobby name");
                _interactive.Add(SessionNameInput);

                RectTransform settings = UiFactory.CreateRow(Root);
                _interactive.Add(UiFactory.CreateButton(settings, "Max players", () => MaxPlayersClicked?.Invoke()));
                _maxPlayersLabel = UiFactory.CreateText(settings, string.Empty, 20, TextAnchor.MiddleCenter);
                _interactive.Add(UiFactory.CreateButton(settings, "Visibility", () => VisibilityClicked?.Invoke()));
                _visibilityLabel = UiFactory.CreateText(settings, string.Empty, 20, TextAnchor.MiddleCenter);
            }

            _interactive.Add(UiFactory.CreateButton(Root, kind == MatchmakingKind.SteamLobby ? "Create Steam lobby" : "Host", () => HostClicked?.Invoke()));

            UiFactory.CreateText(Root, kind == MatchmakingKind.SteamLobby ? "Lobby ID" : "Host address (ip:port)", 18);
            TargetInput = UiFactory.CreateInput(Root, defaultTarget, kind == MatchmakingKind.SteamLobby ? "Lobby ID" : "127.0.0.1:7777");
            _interactive.Add(TargetInput);
            _interactive.Add(UiFactory.CreateButton(Root, "Join", () => JoinClicked?.Invoke()));

            _status = UiFactory.CreateText(Root, string.Empty, 18, TextAnchor.MiddleCenter);
            _cancel = UiFactory.CreateButton(Root, "Cancel", () => CancelClicked?.Invoke(), UiFactory.DangerColor);
            _interactive.Add(UiFactory.CreateButton(Root, "Quit", () => QuitClicked?.Invoke(), UiFactory.DangerColor));
        }

        public InputField NameInput { get; }
        public InputField SessionNameInput { get; }
        public InputField TargetInput { get; }

        public event Action HostClicked;
        public event Action JoinClicked;
        public event Action CancelClicked;
        public event Action QuitClicked;
        public event Action MaxPlayersClicked;
        public event Action VisibilityClicked;

        public void SetBusy(bool busy, string status)
        {
            foreach (Selectable selectable in _interactive)
            {
                selectable.interactable = !busy;
            }

            _cancel.gameObject.SetActive(busy);
            _status.text = status;
        }

        public void SetLobbySettings(int maxPlayers, LobbyVisibility visibility)
        {
            if (_maxPlayersLabel != null)
            {
                _maxPlayersLabel.text = maxPlayers.ToString();
                _visibilityLabel.text = visibility == LobbyVisibility.FriendsOnly ? "Friends" : "Private";
            }
        }
    }

    public sealed class LobbyView : ViewBase
    {
        private readonly Text _code;
        private readonly Text _players;
        private readonly Button _start;
        private readonly Button _invite;

        public LobbyView(Transform parent) : base(UiFactory.CreatePanel("Lobby", parent, new Vector2(520, 480)))
        {
            UiFactory.CreateText(Root, "Lobby", 30, TextAnchor.MiddleCenter, 44);
            _code = UiFactory.CreateText(Root, string.Empty, 18);
            _players = UiFactory.CreateText(Root, string.Empty, 20, TextAnchor.UpperLeft, 180);
            _start = UiFactory.CreateButton(Root, "Start", () => StartClicked?.Invoke());
            _invite = UiFactory.CreateButton(Root, "Invite friends (Steam overlay)", () => InviteClicked?.Invoke());
            UiFactory.CreateButton(Root, "Leave", () => LeaveClicked?.Invoke(), UiFactory.DangerColor);
        }

        public event Action StartClicked;
        public event Action InviteClicked;
        public event Action LeaveClicked;

        public void Render(string sessionCode, IReadOnlyList<PlayerSnapshot> players, int maxPlayers, bool isHost, bool canInvite)
        {
            _code.text = string.IsNullOrEmpty(sessionCode) ? string.Empty : $"Join code: {sessionCode}";
            var builder = new System.Text.StringBuilder();
            builder.AppendLine($"Players {players.Count}/{maxPlayers}:");
            foreach (PlayerSnapshot player in players)
            {
                builder.AppendLine($"  {player.Name}{(player.IsHost ? "  [host]" : string.Empty)}");
            }

            _players.text = builder.ToString();
            _start.gameObject.SetActive(isHost);
            _invite.gameObject.SetActive(canInvite);
            if (!isHost)
            {
                _code.text += "\nWaiting for the host to start...";
            }
        }
    }

    public sealed class LoadingView : ViewBase
    {
        private readonly Text _details;

        public LoadingView(Transform parent) : base(UiFactory.CreateRect("Loading", parent).Stretch())
        {
            Root.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.03f, 0.05f, 1f);
            Text title = UiFactory.CreateText(Root, "Loading...", 36, TextAnchor.MiddleCenter);
            title.rectTransform.Stretch();
            title.rectTransform.offsetMin = new Vector2(0, 80);
            _details = UiFactory.CreateText(Root, string.Empty, 20, TextAnchor.UpperCenter);
            _details.rectTransform.Stretch();
            _details.rectTransform.offsetMax = new Vector2(0, -360);
        }

        public void Render(IReadOnlyList<PlayerSnapshot> players)
        {
            var builder = new System.Text.StringBuilder();
            foreach (PlayerSnapshot player in players)
            {
                builder.AppendLine($"{player.Name}: {(player.IsLoaded ? "ready" : "loading")}");
            }

            _details.text = builder.ToString();
        }
    }

    public sealed class PlayerListView : ViewBase
    {
        private readonly RectTransform _rows;

        public PlayerListView(Transform parent) : base(UiFactory.CreatePanel("PlayerList", parent, new Vector2(560, 360)))
        {
            UiFactory.CreateText(Root, "Players (Tab to close)", 26, TextAnchor.MiddleCenter, 40);
            _rows = UiFactory.CreateRect("Rows", Root);
            var layout = _rows.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
        }

        public event Action<ulong> KickClicked;

        public void Render(IReadOnlyList<PlayerSnapshot> players, ulong localClientId, bool isHost)
        {
            UiFactory.ClearChildren(_rows);
            foreach (PlayerSnapshot player in players)
            {
                RectTransform row = UiFactory.CreateRow(_rows);
                string tags = (player.IsHost ? " [host]" : string.Empty) + (player.ClientId == localClientId ? " (you)" : string.Empty);
                UiFactory.CreateText(row, player.Name + tags, 20);
                UiFactory.CreateText(row, player.IsHost ? "host" : $"{player.RttMs} ms", 20, TextAnchor.MiddleRight);
                if (isHost && !player.IsHost)
                {
                    ulong clientId = player.ClientId;
                    UiFactory.CreateButton(row, "Kick", () => KickClicked?.Invoke(clientId), UiFactory.DangerColor, 32);
                }
            }
        }
    }

    public sealed class PauseView : ViewBase
    {
        public PauseView(Transform parent) : base(UiFactory.CreatePanel("Pause", parent, new Vector2(400, 260)))
        {
            UiFactory.CreateText(Root, "Paused", 30, TextAnchor.MiddleCenter, 44);
            UiFactory.CreateButton(Root, "Resume", () => ResumeClicked?.Invoke());
            UiFactory.CreateButton(Root, "Players", () => PlayersClicked?.Invoke());
            UiFactory.CreateButton(Root, "Leave session", () => LeaveClicked?.Invoke(), UiFactory.DangerColor);
        }

        public event Action ResumeClicked;
        public event Action PlayersClicked;
        public event Action LeaveClicked;
    }

    public sealed class HudView : ViewBase
    {
        private readonly Text _feedback;

        public HudView(Transform parent) : base(UiFactory.CreateRect("Hud", parent).Stretch())
        {
            Text crosshair = UiFactory.CreateText(Root, "+", 28, TextAnchor.MiddleCenter);
            crosshair.rectTransform.Stretch();
            crosshair.raycastTarget = false;

            Text help = UiFactory.CreateText(Root,
                "WASD move | Space jump | E grab/drop | LMB throw | Q drop | X delete | F spawn | R item type | C color | Tab players | Esc menu",
                16, TextAnchor.LowerCenter);
            help.rectTransform.Stretch();
            help.rectTransform.offsetMin = new Vector2(20, 12);
            help.raycastTarget = false;

            _feedback = UiFactory.CreateText(Root, string.Empty, 22, TextAnchor.MiddleCenter);
            _feedback.rectTransform.Stretch();
            _feedback.rectTransform.offsetMax = new Vector2(0, -160);
            _feedback.raycastTarget = false;
        }

        public void SetFeedback(string message) => _feedback.text = message;
    }

    public sealed class MessageView : ViewBase
    {
        private readonly Text _title;
        private readonly Text _message;

        public MessageView(Transform parent) : base(UiFactory.CreatePanel("Message", parent, new Vector2(620, 320)))
        {
            _title = UiFactory.CreateText(Root, string.Empty, 26, TextAnchor.MiddleCenter, 40);
            _message = UiFactory.CreateText(Root, string.Empty, 20, TextAnchor.MiddleCenter, 150);
            UiFactory.CreateButton(Root, "OK", () => SetVisible(false));
        }

        public void Show(string title, string message)
        {
            _title.text = title;
            _message.text = message;
            SetVisible(true);
            Root.SetAsLastSibling();
        }
    }
}
