using Game.Application.Ports;
using Game.Net.Transport;
using Game.Platform.Local;
using Game.Platform.Steam;
using UnityEngine;
using VContainer;

namespace Game.App
{
    /// <summary>
    /// Everything platform/transport specific is bound here and only here. Adding a new transport =
    /// a new Platform.X assembly with its IMatchmakingService / INetworkTransportProvider plus one installer.
    /// </summary>
    public interface IPlatformInstaller
    {
        string Name { get; }
        void Install(IContainerBuilder builder);
    }

    public sealed class LocalPlatformInstaller : IPlatformInstaller
    {
        private readonly CommandLineArgs _args;

        public LocalPlatformInstaller(CommandLineArgs args)
        {
            _args = args;
        }

        public string Name => "Direct IP (Unity Transport)";

        public void Install(IContainerBuilder builder)
        {
            builder.Register<UtpTransportProvider>(Lifetime.Singleton).AsSelf().As<INetworkTransportProvider>();
            builder.Register<DirectIpMatchmaking>(Lifetime.Singleton).As<IMatchmakingService>();
            builder.RegisterInstance(new LocalIdentityOptions(_args.Profile, _args.Name));
            builder.Register<LocalPlayerIdentity>(Lifetime.Singleton).As<IPlayerIdentity>();
            builder.Register<LogStatsService>(Lifetime.Singleton).As<IStatsService>();
            builder.Register<NullRichPresence>(Lifetime.Singleton).As<IRichPresence>();
        }
    }

    public sealed class SteamPlatformInstaller : IPlatformInstaller
    {
        public string Name => "Steam (P2P via Steam Datagram Relay)";

        public void Install(IContainerBuilder builder)
        {
            builder.Register<SteamTransportProvider>(Lifetime.Singleton).AsSelf().As<INetworkTransportProvider>();
            builder.Register<SteamLobbyMatchmaking>(Lifetime.Singleton).AsSelf().As<IMatchmakingService>();
            builder.Register<SteamPlayerIdentity>(Lifetime.Singleton).As<IPlayerIdentity>();
            builder.Register<SteamStatsService>(Lifetime.Singleton).As<IStatsService>();
            builder.Register<SteamRichPresence>(Lifetime.Singleton).As<IRichPresence>();
        }
    }

    public static class PlatformSelector
    {
        /// <summary>Picks the platform; falls back to direct IP with a notice if Steam cannot start.</summary>
        public static IPlatformInstaller Select(AppConfig config, CommandLineArgs args, out string notice)
        {
            notice = null;
            PlatformMode mode = args.Platform ?? (UnityEngine.Application.isEditor ? config.EditorPlatform : config.BuildPlatform);
            if (mode == PlatformMode.DirectIp)
            {
                return new LocalPlatformInstaller(args);
            }

            if (SteamClient.TryInitialize(out string error))
            {
                return new SteamPlatformInstaller();
            }

            Debug.LogWarning($"[Platform] Steam unavailable ({error}) - falling back to direct IP.");
            notice = $"Steam is unavailable ({error}).\nUsing direct IP connection (Unity Transport) instead.";
            return new LocalPlatformInstaller(args);
        }
    }
}
