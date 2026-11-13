using System.Collections.Generic;
using Game.Application.Ports;
using UnityEngine;

namespace Game.Platform.Local
{
    /// <summary>Stats stub: keeps values in memory and logs what a real backend would receive.</summary>
    public sealed class LogStatsService : IStatsService
    {
        private readonly Dictionary<string, int> _stats = new Dictionary<string, int>();
        private readonly HashSet<string> _achievements = new HashSet<string>();

        public int Increment(string statId, int amount)
        {
            _stats.TryGetValue(statId, out int value);
            value += amount;
            _stats[statId] = value;
            Debug.Log($"[Stats] {statId} = {value}");
            return value;
        }

        public bool IsUnlocked(string achievementId) => _achievements.Contains(achievementId);

        public void Unlock(string achievementId)
        {
            if (_achievements.Add(achievementId))
            {
                Debug.Log($"[Stats] Achievement unlocked: {achievementId}");
            }
        }

        public void Store() => Debug.Log("[Stats] StoreStats()");
    }

    public sealed class NullRichPresence : IRichPresence
    {
        public void Set(PresenceState state)
        {
        }

        public void Clear()
        {
        }
    }
}
