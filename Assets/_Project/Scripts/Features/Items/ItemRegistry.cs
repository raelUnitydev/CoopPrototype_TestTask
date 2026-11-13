using System.Collections.Generic;
using Game.Domain.Items;

namespace Game.Features.Items
{
    /// <summary>Items currently spawned on this peer (maintained by the items themselves).</summary>
    public sealed class ItemRegistry
    {
        private readonly List<NetworkItem> _items = new List<NetworkItem>();

        public IReadOnlyList<NetworkItem> Items => _items;

        public void Add(NetworkItem item)
        {
            if (!_items.Contains(item))
            {
                _items.Add(item);
            }
        }

        public void Remove(NetworkItem item) => _items.Remove(item);

        public NetworkItem FindHeldBy(ulong clientId)
        {
            if (clientId == ItemRules.NoHolder)
            {
                return null;
            }

            foreach (NetworkItem item in _items)
            {
                if (item.Holder == clientId)
                {
                    return item;
                }
            }

            return null;
        }
    }
}
