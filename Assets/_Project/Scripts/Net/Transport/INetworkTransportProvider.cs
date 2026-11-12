using Unity.Netcode;

namespace Game.Net.Transport
{
    /// <summary>
    /// Supplies the concrete transport (Unity Transport, Steam sockets, ...). Implemented by platform
    /// modules; the session service only assigns it to the NetworkManager.
    /// </summary>
    public interface INetworkTransportProvider
    {
        NetworkTransport Transport { get; }
    }
}
