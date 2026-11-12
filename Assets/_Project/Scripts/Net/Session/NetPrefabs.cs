using Unity.Netcode;

namespace Game.Net.Session
{
    /// <summary>Network prefabs owned by the session layer (supplied by the composition root).</summary>
    public sealed class NetPrefabs
    {
        public NetworkObject SessionState { get; }

        public NetPrefabs(NetworkObject sessionState)
        {
            SessionState = sessionState;
        }
    }
}
