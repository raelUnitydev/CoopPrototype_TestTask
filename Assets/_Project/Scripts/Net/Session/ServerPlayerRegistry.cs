using System.Collections.Generic;
using Game.Domain.Session;

namespace Game.Net.Session
{
    /// <summary>
    /// Host-only table of approved connections. Filled synchronously in the approval callback, so two
    /// simultaneous connections with the same PlayerId or for the last free slot cannot both pass.
    /// </summary>
    public sealed class ServerPlayerRegistry
    {
        private readonly Dictionary<ulong, ConnectionRequest> _byClient = new Dictionary<ulong, ConnectionRequest>();

        public int Count => _byClient.Count;

        public void Add(ulong clientId, in ConnectionRequest request) => _byClient[clientId] = request;

        public bool Remove(ulong clientId) => _byClient.Remove(clientId);

        public bool TryGet(ulong clientId, out ConnectionRequest request) => _byClient.TryGetValue(clientId, out request);

        public bool ContainsPlayer(PlayerId playerId)
        {
            foreach (KeyValuePair<ulong, ConnectionRequest> pair in _byClient)
            {
                if (pair.Value.PlayerId == playerId)
                {
                    return true;
                }
            }

            return false;
        }

        public void Clear() => _byClient.Clear();
    }
}
