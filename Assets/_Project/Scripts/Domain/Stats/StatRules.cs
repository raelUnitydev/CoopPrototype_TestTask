using System.Collections.Generic;

namespace Game.Domain.Stats
{
    /// <summary>Gameplay events that feed per-player statistics. Sent over the wire as a byte.</summary>
    public enum StatEvent : byte
    {
        ItemSpawned = 0,
        ItemThrown = 1
    }

    public readonly struct AchievementDefinition
    {
        public string Id { get; }
        public string StatId { get; }
        public int Threshold { get; }

        public AchievementDefinition(string id, string statId, int threshold)
        {
            Id = id;
            StatId = statId;
            Threshold = threshold;
        }
    }

    /// <summary>Stat/achievement API names (fictional names, AppID 480 has no such entries).</summary>
    public static class StatRules
    {
        public const string ItemsSpawned = "stat_items_spawned";
        public const string ItemsThrown = "stat_items_thrown";

        public static readonly IReadOnlyList<AchievementDefinition> Achievements = new[]
        {
            new AchievementDefinition("ACH_FIRST_THROW", ItemsThrown, 1),
            new AchievementDefinition("ACH_THROW_25", ItemsThrown, 25),
            new AchievementDefinition("ACH_BUILDER_10", ItemsSpawned, 10)
        };

        public static string ToStatId(StatEvent statEvent)
        {
            return statEvent == StatEvent.ItemThrown ? ItemsThrown : ItemsSpawned;
        }
    }
}
