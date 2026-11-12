using Game.Application.Ports;
using Game.Domain.Stats;

namespace Game.Application.Flow
{
    /// <summary>
    /// Runs on the client that owns the stats. The host validates gameplay events and forwards them
    /// to the acting player; this class turns them into stat increments and achievement unlocks.
    /// StoreStats is batched: immediately on unlock (so the toast appears) and on session end otherwise.
    /// </summary>
    public sealed class AchievementTracker : IStatEventSink
    {
        private readonly IStatsService _stats;
        private bool _dirty;

        public AchievementTracker(IStatsService stats)
        {
            _stats = stats;
        }

        public void OnStatEvent(StatEvent statEvent)
        {
            string statId = StatRules.ToStatId(statEvent);
            int value = _stats.Increment(statId, 1);
            _dirty = true;

            bool unlockedAny = false;
            foreach (AchievementDefinition achievement in StatRules.Achievements)
            {
                if (achievement.StatId == statId && value >= achievement.Threshold && !_stats.IsUnlocked(achievement.Id))
                {
                    _stats.Unlock(achievement.Id);
                    unlockedAny = true;
                }
            }

            if (unlockedAny)
            {
                Flush();
            }
        }

        public void Flush()
        {
            if (!_dirty)
            {
                return;
            }

            _stats.Store();
            _dirty = false;
        }
    }
}
