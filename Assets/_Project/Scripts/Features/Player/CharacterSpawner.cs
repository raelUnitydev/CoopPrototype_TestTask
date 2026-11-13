using System;
using Game.Net.Session;
using Unity.Netcode;
using UnityEngine;

namespace Game.Features.Player
{
    public sealed class PlayerFeatureSettings
    {
        public NetworkObject PlayerPrefab { get; }

        public PlayerFeatureSettings(NetworkObject playerPrefab)
        {
            PlayerPrefab = playerPrefab;
        }
    }

    /// <summary>
    /// Host-side: spawns a character, owned by the client, as soon as that client has the level loaded.
    /// The character is a player object, so Netcode despawns it automatically when its owner leaves.
    /// </summary>
    public sealed class CharacterSpawner : IDisposable
    {
        private const float SpawnRadius = 3f;

        private readonly NetworkManager _networkManager;
        private readonly ServerSessionEvents _events;
        private readonly PlayerFeatureSettings _settings;
        private int _spawnIndex;

        public CharacterSpawner(NetworkManager networkManager, ServerSessionEvents events, PlayerFeatureSettings settings)
        {
            _networkManager = networkManager;
            _events = events;
            _settings = settings;
            _events.ClientReadyInGame += OnClientReady;
        }

        public void Dispose()
        {
            _events.ClientReadyInGame -= OnClientReady;
        }

        private void OnClientReady(ulong clientId)
        {
            if (!_networkManager.IsServer
                || !_networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
                || client.PlayerObject != null)
            {
                return;
            }

            float angle = _spawnIndex++ * 90f * Mathf.Deg2Rad;
            var position = new Vector3(Mathf.Cos(angle) * SpawnRadius, 1.1f, Mathf.Sin(angle) * SpawnRadius);
            Quaternion rotation = Quaternion.LookRotation(-new Vector3(position.x, 0f, position.z));

            // destroyWithScene: characters belong to the level and go away if the host switches scenes.
            NetworkObject.InstantiateAndSpawn(_settings.PlayerPrefab.gameObject, _networkManager, clientId,
                destroyWithScene: true, isPlayerObject: true, position: position, rotation: rotation);
        }
    }
}
