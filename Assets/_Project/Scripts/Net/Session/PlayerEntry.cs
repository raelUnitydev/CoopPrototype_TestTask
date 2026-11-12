using System;
using Game.Domain.Session;
using Unity.Collections;
using Unity.Netcode;

namespace Game.Net.Session
{
    /// <summary>Replicated roster row. Kept small: it is re-sent as a delta whenever a field changes.</summary>
    public struct PlayerEntry : INetworkSerializable, IEquatable<PlayerEntry>
    {
        public ulong ClientId;
        public FixedString64Bytes PlayerId;
        public FixedString64Bytes Name;
        public byte ColorIndex;
        public ushort RttMs;
        public bool IsLoaded;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref ColorIndex);
            serializer.SerializeValue(ref RttMs);
            serializer.SerializeValue(ref IsLoaded);
        }

        public bool Equals(PlayerEntry other)
        {
            return ClientId == other.ClientId
                   && PlayerId.Equals(other.PlayerId)
                   && Name.Equals(other.Name)
                   && ColorIndex == other.ColorIndex
                   && RttMs == other.RttMs
                   && IsLoaded == other.IsLoaded;
        }

        public override bool Equals(object obj) => obj is PlayerEntry other && Equals(other);

        public override int GetHashCode() => ClientId.GetHashCode();

        public PlayerSnapshot ToSnapshot(ulong serverClientId)
        {
            return new PlayerSnapshot(ClientId, new PlayerId(PlayerId.ToString()), Name.ToString(), ColorIndex,
                RttMs, ClientId == serverClientId, IsLoaded);
        }

        /// <summary>Truncates by UTF-8 bytes so FixedString never throws on long or non-latin names.</summary>
        public static FixedString64Bytes ToFixed(string value)
        {
            var result = new FixedString64Bytes();
            if (string.IsNullOrEmpty(value))
            {
                return result;
            }

            foreach (char c in value)
            {
                if (result.Append(c) != FormatError.None)
                {
                    break;
                }
            }

            return result;
        }
    }
}
