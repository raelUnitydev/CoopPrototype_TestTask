namespace Game.Domain.Session
{
    /// <summary>Read model of a player in the session, used by UI and gameplay.</summary>
    public readonly struct PlayerSnapshot
    {
        public ulong ClientId { get; }
        public PlayerId PlayerId { get; }
        public string Name { get; }
        public byte ColorIndex { get; }
        public int RttMs { get; }
        public bool IsHost { get; }
        public bool IsLoaded { get; }

        public PlayerSnapshot(ulong clientId, PlayerId playerId, string name, byte colorIndex, int rttMs, bool isHost, bool isLoaded)
        {
            ClientId = clientId;
            PlayerId = playerId;
            Name = name;
            ColorIndex = colorIndex;
            RttMs = rttMs;
            IsHost = isHost;
            IsLoaded = isLoaded;
        }
    }

    public static class PlayerColors
    {
        /// <summary>Number of colors in the palette; the actual colors live in the presentation layer.</summary>
        public const byte Count = 8;

        public static byte Next(byte current) => (byte)((current + 1) % Count);
    }
}
