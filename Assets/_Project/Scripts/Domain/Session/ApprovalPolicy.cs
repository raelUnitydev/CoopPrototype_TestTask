using System;

namespace Game.Domain.Session
{
    /// <summary>
    /// Pure host-side rules for accepting a connection. Kept free of Netcode so it can be unit tested
    /// and reused with any transport.
    /// </summary>
    public sealed class ApprovalPolicy
    {
        private readonly int _maxPlayers;
        private readonly string _buildVersion;

        public ApprovalPolicy(int maxPlayers, string buildVersion)
        {
            if (maxPlayers < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPlayers));
            }

            _maxPlayers = maxPlayers;
            _buildVersion = buildVersion ?? string.Empty;
        }

        public int MaxPlayers => _maxPlayers;

        public ApprovalResult Evaluate(in ConnectionRequest request, in ApprovalContext context)
        {
            if (!request.PlayerId.IsValid)
            {
                return ApprovalResult.Reject(DisconnectReason.InvalidPayload);
            }

            // Version first: a different build cannot even talk the same protocol, other checks are meaningless.
            if (!string.Equals(request.BuildVersion, _buildVersion, StringComparison.Ordinal))
            {
                return ApprovalResult.Reject(DisconnectReason.VersionMismatch);
            }

            if (context.IsPlayerAlreadyInSession)
            {
                return ApprovalResult.Reject(DisconnectReason.DuplicatePlayer);
            }

            if (context.OccupiedSlots >= _maxPlayers)
            {
                return ApprovalResult.Reject(DisconnectReason.ServerFull);
            }

            // Scene switch in progress: a joiner would synchronize to a scene that is about to be replaced.
            if (context.Phase == SessionPhase.Loading)
            {
                return ApprovalResult.Reject(DisconnectReason.SessionBusy);
            }

            return ApprovalResult.Accept();
        }

        public static string SanitizeName(string rawName, int maxLength = 24)
        {
            string name = (rawName ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                return "Player";
            }

            return name.Length > maxLength ? name.Substring(0, maxLength) : name;
        }
    }
}
