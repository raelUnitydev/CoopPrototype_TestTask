using System;
using System.Collections.Generic;
using Game.Domain.Items;
using Game.Domain.Stats;
using Game.Net.Session;
using Unity.Netcode;
using UnityEngine;

namespace Game.Features.Items
{
    /// <summary>
    /// Host-side authority for every item request. Each request is validated against the host's own
    /// state (holder, distance, cooldown) with <see cref="ItemRules"/>. Requests are processed one at a
    /// time, so when two players grab the same item the first valid request wins and the second gets a
    /// rejection - clients never change item state optimistically, hence no desync.
    /// </summary>
    public sealed class ItemAuthority : IDisposable
    {
        private const float HandHeight = 1.2f;

        private readonly NetworkManager _networkManager;
        private readonly ItemRegistry _registry;
        private readonly ItemCatalog _catalog;
        private readonly ServerSessionEvents _events;
        private readonly ItemRulesConfig _rules = ItemRulesConfig.Default;
        private readonly Dictionary<ulong, double> _lastSpawnTime = new Dictionary<ulong, double>();

        public ItemAuthority(NetworkManager networkManager, ItemRegistry registry, ItemCatalog catalog, ServerSessionEvents events)
        {
            _networkManager = networkManager;
            _registry = registry;
            _catalog = catalog;
            _events = events;
            _events.ClientLeft += OnClientLeft;
        }

        public void Dispose()
        {
            _events.ClientLeft -= OnClientLeft;
        }

        public ItemRequestResult Spawn(ulong clientId, int typeIndex)
        {
            if (!TryGetHand(clientId, out Vector3 hand, out Vector3 forward) || !_catalog.TryGet(typeIndex, out NetworkObject prefab))
            {
                return ItemRequestResult.InvalidRelease;
            }

            double now = _networkManager.ServerTime.Time;
            if (!_lastSpawnTime.TryGetValue(clientId, out double last))
            {
                last = double.NegativeInfinity;
            }

            ItemRequestResult result = ItemRules.CanSpawn(_registry.Items.Count, now, last, _rules);
            if (result != ItemRequestResult.Ok)
            {
                return result;
            }

            _lastSpawnTime[clientId] = now;
            Vector3 position = hand + forward * 1.5f + Vector3.up * 0.3f;

            // Spawned and owned by the host: the item does not belong to its creator and survives his exit.
            NetworkObject.InstantiateAndSpawn(prefab.gameObject, _networkManager, NetworkManager.ServerClientId,
                destroyWithScene: true, position: position, rotation: Quaternion.identity);
            _events.ReportStat(clientId, StatEvent.ItemSpawned);
            return ItemRequestResult.Ok;
        }

        public ItemRequestResult Grab(ulong clientId, NetworkItem item)
        {
            if (!TryGetHand(clientId, out Vector3 hand, out _))
            {
                return ItemRequestResult.TooFar;
            }

            bool alreadyHolding = _registry.FindHeldBy(clientId) != null;
            float distance = Vector3.Distance(hand, item.transform.position);
            ItemRequestResult result = ItemRules.CanGrab(item.Holder, alreadyHolding, distance, _rules);
            if (result == ItemRequestResult.Ok)
            {
                item.ServerGrab(clientId);
            }

            return result;
        }

        public ItemRequestResult Release(ulong clientId, NetworkItem item, Vector3 position, Quaternion rotation, Vector3 velocity, bool isThrow)
        {
            if (!TryGetHand(clientId, out Vector3 hand, out _))
            {
                return ItemRequestResult.NotHolder;
            }

            float offset = Vector3.Distance(hand, position);
            ItemRequestResult result = ItemRules.CanRelease(clientId, item.Holder, offset, _rules);
            if (result != ItemRequestResult.Ok)
            {
                return result;
            }

            Vector3 clamped = velocity * ItemRules.ThrowSpeedScale(velocity.magnitude, _rules);
            item.ServerRelease(position, rotation, clamped);

            if (isThrow)
            {
                _events.ReportStat(clientId, StatEvent.ItemThrown);
            }

            return ItemRequestResult.Ok;
        }

        public ItemRequestResult Delete(ulong clientId, NetworkItem item)
        {
            ItemRequestResult result = ItemRules.CanDelete(clientId, item.Holder);
            if (result == ItemRequestResult.Ok)
            {
                item.NetworkObject.Despawn();
            }

            return result;
        }

        private void OnClientLeft(ulong clientId)
        {
            _lastSpawnTime.Remove(clientId);

            // The leaver's held item is released and falls (Netcode keeps it alive: DontDestroyWithOwner).
            NetworkItem held;
            while ((held = _registry.FindHeldBy(clientId)) != null)
            {
                held.ServerForceRelease();
            }
        }

        /// <summary>Host's view of the player's hand. It lags behind the owner by ~RTT/2, hence the latency tolerance in the rules.</summary>
        private bool TryGetHand(ulong clientId, out Vector3 hand, out Vector3 forward)
        {
            hand = default;
            forward = Vector3.forward;
            if (!_networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null)
            {
                return false;
            }

            Transform character = client.PlayerObject.transform;
            hand = character.position + Vector3.up * HandHeight;
            forward = character.forward;
            return true;
        }
    }
}
