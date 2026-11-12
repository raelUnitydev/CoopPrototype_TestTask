namespace Game.Domain.Session
{
    /// <summary>Data a client sends to the host with its connection attempt.</summary>
    public readonly struct ConnectionRequest
    {
        public string BuildVersion { get; }
        public PlayerId PlayerId { get; }
        public string DisplayName { get; }

        public ConnectionRequest(string buildVersion, PlayerId playerId, string displayName)
        {
            BuildVersion = buildVersion ?? string.Empty;
            PlayerId = playerId;
            DisplayName = displayName ?? string.Empty;
        }
    }

    public readonly struct ApprovalResult
    {
        public bool Approved { get; }
        public DisconnectReason Reason { get; }

        private ApprovalResult(bool approved, DisconnectReason reason)
        {
            Approved = approved;
            Reason = reason;
        }

        public static ApprovalResult Accept() => new ApprovalResult(true, DisconnectReason.None);

        public static ApprovalResult Reject(DisconnectReason reason) => new ApprovalResult(false, reason);
    }

    /// <summary>Snapshot of host state the approval policy needs to make a decision.</summary>
    public readonly struct ApprovalContext
    {
        public int OccupiedSlots { get; }
        public bool IsPlayerAlreadyInSession { get; }
        public SessionPhase Phase { get; }

        public ApprovalContext(int occupiedSlots, bool isPlayerAlreadyInSession, SessionPhase phase)
        {
            OccupiedSlots = occupiedSlots;
            IsPlayerAlreadyInSession = isPlayerAlreadyInSession;
            Phase = phase;
        }
    }
}
