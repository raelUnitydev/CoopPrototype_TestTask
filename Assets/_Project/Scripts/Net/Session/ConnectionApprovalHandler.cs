using Game.Domain.Session;
using Unity.Netcode;
using UnityEngine;

namespace Game.Net.Session
{
    /// <summary>Adapter between Netcode's approval callback and the pure <see cref="ApprovalPolicy"/>.</summary>
    public sealed class ConnectionApprovalHandler
    {
        private readonly ServerPlayerRegistry _registry;
        private readonly SessionStateHolder _state;
        private readonly string _buildVersion;
        private ApprovalPolicy _policy;

        public ConnectionApprovalHandler(ServerPlayerRegistry registry, SessionStateHolder state, string buildVersion)
        {
            _registry = registry;
            _state = state;
            _buildVersion = buildVersion;
        }

        public void Configure(int maxPlayers)
        {
            _policy = new ApprovalPolicy(maxPlayers, _buildVersion);
        }

        public void Handle(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;
            response.Pending = false;

            if (!ConnectionPayloadCodec.TryDecode(request.Payload, out ConnectionRequest connection))
            {
                Reject(response, request.ClientNetworkId, DisconnectReason.InvalidPayload);
                return;
            }

            var context = new ApprovalContext(_registry.Count, _registry.ContainsPlayer(connection.PlayerId), _state.Phase);
            ApprovalResult result = _policy.Evaluate(connection, context);
            if (!result.Approved)
            {
                Reject(response, request.ClientNetworkId, result.Reason);
                return;
            }

            var sanitized = new ConnectionRequest(connection.BuildVersion, connection.PlayerId,
                ApprovalPolicy.SanitizeName(connection.DisplayName));
            _registry.Add(request.ClientNetworkId, sanitized);
            response.Approved = true;
            Debug.Log($"[Approval] Client {request.ClientNetworkId} '{sanitized.DisplayName}' ({sanitized.PlayerId}) approved.");
        }

        private static void Reject(NetworkManager.ConnectionApprovalResponse response, ulong clientId, DisconnectReason reason)
        {
            response.Approved = false;
            response.Reason = DisconnectReasonCodec.Encode(reason);
            Debug.Log($"[Approval] Client {clientId} rejected: {reason}.");
        }
    }
}
