namespace Game.Domain.Session
{
    /// <summary>
    /// Why a session ended (or was refused) for the local player.
    /// Values are sent over the wire, so never reorder existing entries.
    /// </summary>
    public enum DisconnectReason : byte
    {
        None = 0,
        Unknown = 1,
        ServerFull = 2,
        VersionMismatch = 3,
        DuplicatePlayer = 4,
        ConnectTimeout = 5,
        Kicked = 6,
        HostLeft = 7,
        ConnectionLost = 8,
        InvalidPayload = 9,
        SessionBusy = 10,
        StartFailed = 11,
        PlatformError = 12,
        LeftByUser = 13
    }
}
