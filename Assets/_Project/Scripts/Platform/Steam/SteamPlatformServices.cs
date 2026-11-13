using System.Collections.Generic;
using Game.Application.Ports;
using Game.Domain.Session;
using Steamworks;
using UnityEngine;

namespace Game.Platform.Steam
{
    /// <summary>PlayerId = SteamID64, name = Steam persona name.</summary>
    public sealed class SteamPlayerIdentity : IPlayerIdentity
    {
        public PlayerId PlayerId => new PlayerId(SteamUser.GetSteamID().m_SteamID.ToString());
        public string DisplayName => SteamFriends.GetPersonaName();
        public bool CanRename => false;

        public void Rename(string displayName)
        {
        }
    }

    /// <summary>
    /// Rich presence shown in the friends list. "status" is displayed as-is; "connect" enables
    /// "Join Game" from the friends list; steam_player_group groups party members.
    /// (Localized steam_display tokens need a backend config, which AppID 480 does not allow.)
    /// </summary>
    public sealed class SteamRichPresence : IRichPresence
    {
        private readonly SteamLobbyMatchmaking _lobby;

        public SteamRichPresence(SteamLobbyMatchmaking lobby)
        {
            _lobby = lobby;
        }

        public void Set(PresenceState state)
        {
            if (!SteamClient.IsInitialized)
            {
                return;
            }

            CSteamID lobby = _lobby.CurrentLobby;
            switch (state.Kind)
            {
                case PresenceKind.Lobby:
                    SteamFriends.SetRichPresence("status", $"In lobby ({state.Players}/{state.MaxPlayers})");
                    break;
                case PresenceKind.InGame:
                    SteamFriends.SetRichPresence("status", $"In game {state.Players}/{state.MaxPlayers}");
                    break;
                default:
                    SteamFriends.ClearRichPresence();
                    SteamFriends.SetRichPresence("status", "In menu");
                    return;
            }

            if (lobby.IsValid())
            {
                string lobbyId = lobby.m_SteamID.ToString();
                SteamFriends.SetRichPresence("connect", $"+connect_lobby {lobbyId}");
                SteamFriends.SetRichPresence("steam_player_group", lobbyId);
                SteamFriends.SetRichPresence("steam_player_group_size", state.Players.ToString());
            }
        }

        public void Clear()
        {
            if (SteamClient.IsInitialized)
            {
                SteamFriends.ClearRichPresence();
            }
        }
    }

    /// <summary>
    /// Steam user stats with fictional API names (AppID 480 does not define them, so Steam calls
    /// return false; values are cached locally and every call is logged so the flow can be verified).
    /// Called on the client that owns the stats, after the host validated the event.
    /// </summary>
    public sealed class SteamStatsService : IStatsService
    {
        private readonly Dictionary<string, int> _cache = new Dictionary<string, int>();
        private readonly HashSet<string> _unlocked = new HashSet<string>();

        public int Increment(string statId, int amount)
        {
            if (!_cache.TryGetValue(statId, out int value) && SteamUserStats.GetStat(statId, out int stored))
            {
                value = stored;
            }

            value += amount;
            _cache[statId] = value;
            bool ok = SteamUserStats.SetStat(statId, value);
            Debug.Log($"[SteamStats] SetStat({statId}, {value}) -> {ok}");
            return value;
        }

        public bool IsUnlocked(string achievementId)
        {
            return _unlocked.Contains(achievementId)
                   || (SteamUserStats.GetAchievement(achievementId, out bool achieved) && achieved);
        }

        public void Unlock(string achievementId)
        {
            _unlocked.Add(achievementId);
            bool ok = SteamUserStats.SetAchievement(achievementId);
            Debug.Log($"[SteamStats] SetAchievement({achievementId}) -> {ok}");
        }

        public void Store()
        {
            if (!SteamClient.IsInitialized)
            {
                return;
            }

            bool ok = SteamUserStats.StoreStats();
            Debug.Log($"[SteamStats] StoreStats() -> {ok}");
        }
    }
}
