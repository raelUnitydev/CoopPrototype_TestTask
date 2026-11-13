using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Game.Features.Items
{
    /// <summary>
    /// Spawnable item types. Extension point: add a prefab with <see cref="NetworkItem"/> + NetworkObject
    /// and list it here - it is registered as a network prefab automatically.
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Item Catalog", fileName = "ItemCatalog")]
    public sealed class ItemCatalog : ScriptableObject
    {
        [SerializeField] private List<NetworkObject> _items = new List<NetworkObject>();

        public IReadOnlyList<NetworkObject> Items => _items;

        public bool TryGet(int index, out NetworkObject prefab)
        {
            bool valid = index >= 0 && index < _items.Count && _items[index] != null;
            prefab = valid ? _items[index] : null;
            return valid;
        }
    }
}
