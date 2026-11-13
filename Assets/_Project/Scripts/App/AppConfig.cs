using Game.Features.Items;
using Unity.Netcode;
using UnityEngine;

namespace Game.App
{
    public enum PlatformMode
    {
        /// <summary>Steam if it initializes, otherwise direct IP (Unity Transport).</summary>
        Auto,
        DirectIp,
        Steam
    }

    /// <summary>Single place where the transport/platform and network prefabs are configured.</summary>
    [CreateAssetMenu(menuName = "Game/App Config", fileName = "AppConfig")]
    public sealed class AppConfig : ScriptableObject
    {
        [Header("Platform / transport")]
        [Tooltip("Used by player builds. Overridden by -transport utp|steam.")]
        public PlatformMode BuildPlatform = PlatformMode.Auto;
        [Tooltip("Used in the Editor (Multiplayer Play Mode runs several instances on one PC, so direct IP is the default).")]
        public PlatformMode EditorPlatform = PlatformMode.DirectIp;

        [Header("Session")]
        [Range(1, 4)] public int MaxPlayers = 4;
        public ushort Port = 7777;
        [Tooltip("Network tick rate. 30 Hz is plenty for a co-op prototype and halves traffic compared to 60.")]
        public uint TickRate = 30;

        [Header("Network prefabs")]
        public NetworkObject SessionStatePrefab;
        public NetworkObject PlayerPrefab;
        public ItemCatalog ItemCatalog;
    }
}
