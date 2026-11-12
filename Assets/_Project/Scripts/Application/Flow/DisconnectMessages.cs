using Game.Domain.Session;

namespace Game.Application.Flow
{
    public static class DisconnectMessages
    {
        /// <summary>Reasons that are the user's own action and need no dialog.</summary>
        public static bool IsSilent(DisconnectReason reason)
        {
            return reason == DisconnectReason.None || reason == DisconnectReason.LeftByUser;
        }

        public static string Describe(DisconnectReason reason)
        {
            switch (reason)
            {
                case DisconnectReason.ServerFull: return "The session is full.";
                case DisconnectReason.VersionMismatch: return "The host runs a different game version.";
                case DisconnectReason.DuplicatePlayer: return "This player is already in the session.";
                case DisconnectReason.ConnectTimeout: return "Connection timed out.";
                case DisconnectReason.Kicked: return "You were kicked by the host.";
                case DisconnectReason.HostLeft: return "The host has closed the session.";
                case DisconnectReason.ConnectionLost: return "Connection to the host was lost.";
                case DisconnectReason.InvalidPayload: return "The host rejected the connection data.";
                case DisconnectReason.SessionBusy: return "The session is loading a level. Try again in a moment.";
                case DisconnectReason.StartFailed: return "Failed to start the network session.";
                case DisconnectReason.PlatformError: return "Platform (lobby) error.";
                default: return "Disconnected.";
            }
        }
    }
}
