using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Game.Net
{
    /// <summary>
    /// Registers a prefab handler for every network prefab so that instances spawned by Netcode
    /// (on host and clients alike) get their dependencies injected by the container.
    /// This keeps container lookups out of prefabs (no Service Locator / FindObjectOfType).
    /// </summary>
    public sealed class NetworkPrefabInjector
    {
        private readonly NetworkManager _networkManager;
        private readonly IObjectResolver _resolver;

        public NetworkPrefabInjector(NetworkManager networkManager, IObjectResolver resolver)
        {
            _networkManager = networkManager;
            _resolver = resolver;
        }

        public void Register(IEnumerable<NetworkObject> prefabs)
        {
            foreach (NetworkObject prefab in prefabs)
            {
                if (!_networkManager.NetworkConfig.Prefabs.Contains(prefab.gameObject))
                {
                    _networkManager.AddNetworkPrefab(prefab.gameObject);
                }

                _networkManager.PrefabHandler.AddHandler(prefab, new InjectingHandler(prefab, _resolver));
            }
        }

        private sealed class InjectingHandler : INetworkPrefabInstanceHandler
        {
            private readonly NetworkObject _prefab;
            private readonly IObjectResolver _resolver;

            public InjectingHandler(NetworkObject prefab, IObjectResolver resolver)
            {
                _prefab = prefab;
                _resolver = resolver;
            }

            public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
            {
                return _resolver.Instantiate(_prefab, position, rotation);
            }

            public void Destroy(NetworkObject networkObject)
            {
                Object.Destroy(networkObject.gameObject);
            }
        }
    }
}
