namespace Game.Domain.Session
{
    /// <summary>Local connection lifecycle (who we are on the network right now).</summary>
    public enum ConnectionStatus
    {
        Offline,
        StartingHost,
        Hosting,
        Connecting,
        Connected,
        ShuttingDown
    }

    /// <summary>Replicated session phase, owned by the host. Values are sent over the wire.</summary>
    public enum SessionPhase : byte
    {
        None = 0,
        Lobby = 1,
        Loading = 2,
        InGame = 3
    }

    public enum LobbyVisibility
    {
        FriendsOnly,
        Private
    }
}
