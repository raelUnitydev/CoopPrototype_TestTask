namespace Game.Domain.Items
{
    public enum ItemRequestResult : byte
    {
        Ok = 0,
        AlreadyHeld = 1,
        TooFar = 2,
        NotHolder = 3,
        RateLimited = 4,
        LimitReached = 5,
        PlayerAlreadyHolding = 6,
        InvalidRelease = 7
    }

    public readonly struct ItemRulesConfig
    {
        public float MaxGrabDistance { get; }
        /// <summary>Extra slack: the host sees an owner-authoritative player ~RTT/2 + interpolation in the past.</summary>
        public float LatencyTolerance { get; }
        public float MaxThrowSpeed { get; }
        /// <summary>Max distance between holder and the release position he reports.</summary>
        public float MaxReleaseOffset { get; }
        public double SpawnCooldownSeconds { get; }
        public int MaxItems { get; }

        public ItemRulesConfig(float maxGrabDistance, float latencyTolerance, float maxThrowSpeed,
            float maxReleaseOffset, double spawnCooldownSeconds, int maxItems)
        {
            MaxGrabDistance = maxGrabDistance;
            LatencyTolerance = latencyTolerance;
            MaxThrowSpeed = maxThrowSpeed;
            MaxReleaseOffset = maxReleaseOffset;
            SpawnCooldownSeconds = spawnCooldownSeconds;
            MaxItems = maxItems;
        }

        // Tolerances cover a sprinting player (7.5 m/s) seen by the host ~300 ms in the past.
        public static ItemRulesConfig Default => new ItemRulesConfig(3f, 2.5f, 18f, 5f, 0.4, 64);
    }

    /// <summary>
    /// Host-side validation of item requests. Pure functions: the caller supplies the authoritative state.
    /// Because the host processes requests one by one, "first valid request wins" resolves grab races.
    /// </summary>
    public static class ItemRules
    {
        public const ulong NoHolder = ulong.MaxValue;

        public static ItemRequestResult CanGrab(ulong currentHolder, bool requesterHoldsSomething, float distance, in ItemRulesConfig config)
        {
            if (currentHolder != NoHolder)
            {
                return ItemRequestResult.AlreadyHeld;
            }

            if (requesterHoldsSomething)
            {
                return ItemRequestResult.PlayerAlreadyHolding;
            }

            return distance <= config.MaxGrabDistance + config.LatencyTolerance
                ? ItemRequestResult.Ok
                : ItemRequestResult.TooFar;
        }

        public static ItemRequestResult CanRelease(ulong requester, ulong currentHolder, float releaseOffset, in ItemRulesConfig config)
        {
            if (currentHolder != requester)
            {
                return ItemRequestResult.NotHolder;
            }

            return releaseOffset <= config.MaxReleaseOffset ? ItemRequestResult.Ok : ItemRequestResult.InvalidRelease;
        }

        public static ItemRequestResult CanDelete(ulong requester, ulong currentHolder)
        {
            return currentHolder == requester ? ItemRequestResult.Ok : ItemRequestResult.NotHolder;
        }

        public static ItemRequestResult CanSpawn(int existingItems, double now, double lastSpawnTime, in ItemRulesConfig config)
        {
            if (existingItems >= config.MaxItems)
            {
                return ItemRequestResult.LimitReached;
            }

            return now - lastSpawnTime >= config.SpawnCooldownSeconds ? ItemRequestResult.Ok : ItemRequestResult.RateLimited;
        }

        /// <summary>Scale factor (0..1] that clamps a reported throw velocity to the allowed maximum.</summary>
        public static float ThrowSpeedScale(float requestedSpeed, in ItemRulesConfig config)
        {
            if (requestedSpeed <= config.MaxThrowSpeed || requestedSpeed <= 0f)
            {
                return 1f;
            }

            return config.MaxThrowSpeed / requestedSpeed;
        }
    }
}
