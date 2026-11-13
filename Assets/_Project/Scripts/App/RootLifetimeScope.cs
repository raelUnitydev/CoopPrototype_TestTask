using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Application.Flow;
using Game.Application.Ports;
using Game.Features.Items;
using Game.Features.Player;
using Game.Net;
using Game.Net.Scenes;
using Game.Net.Session;
using Game.UI;
using Unity.Multiplayer.Tools.NetworkSimulator.Runtime;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

namespace Game.App
{
    /// <summary>
    /// Composition root (lives in the Bootstrap scene, DontDestroyOnLoad). The only place that knows every
    /// module: picks the platform/transport, builds the NetworkManager and wires all services.
    /// </summary>
    public sealed class RootLifetimeScope : LifetimeScope
    {
        [SerializeField] private AppConfig _config;

        private string _startupNotice;
        private CommandLineArgs _args;

        protected override void Awake()
        {
            DontDestroyOnLoad(gameObject);
            base.Awake();
        }

        protected override void Configure(IContainerBuilder builder)
        {
            _args = CommandLineArgs.Parse(Environment.GetCommandLineArgs());
            var settings = new AppSettings(
                string.IsNullOrEmpty(_args.BuildVersion) ? UnityEngine.Application.version : _args.BuildVersion,
                Mathf.Clamp(_args.MaxPlayers ?? _config.MaxPlayers, 1, 4),
                _args.Port ?? _config.Port);
            builder.RegisterInstance(settings);
            builder.RegisterInstance(_config.ItemCatalog);
            builder.RegisterInstance(new NetPrefabs(_config.SessionStatePrefab));
            builder.RegisterInstance(new PlayerFeatureSettings(_config.PlayerPrefab));
            builder.RegisterComponent(CreateNetworkManager());

            // Platform + transport: the single switch point (config asset or -transport argument).
            IPlatformInstaller platform = PlatformSelector.Select(_config, _args, out _startupNotice);
            platform.Install(builder);
            Debug.Log($"[App] Platform: {platform.Name}. Build version {settings.BuildVersion}.");

            // Application
            builder.Register<InputGate>(Lifetime.Singleton);
            builder.Register<AchievementTracker>(Lifetime.Singleton).AsSelf().As<IStatEventSink>();
            builder.Register<AppFlow>(Lifetime.Singleton);

            // Network session
            builder.Register<SessionStateHolder>(Lifetime.Singleton);
            builder.Register<ServerPlayerRegistry>(Lifetime.Singleton);
            builder.Register<ServerSessionEvents>(Lifetime.Singleton);
            builder.Register<ConnectionApprovalHandler>(Lifetime.Singleton).WithParameter(settings.BuildVersion);
            builder.Register<NetworkSceneFlow>(Lifetime.Singleton);
            builder.Register<NgoSessionService>(Lifetime.Singleton).AsSelf().As<ISessionService>();
            builder.Register<NetworkRoster>(Lifetime.Singleton).As<IPlayerRoster>().As<ISessionCommands>();
            builder.Register<LocalSceneNavigator>(Lifetime.Singleton).As<ISceneNavigator>();
            builder.Register<NetworkPrefabInjector>(Lifetime.Singleton);

            // Gameplay features
            builder.Register<GameInput>(Lifetime.Singleton);
            builder.Register<CharacterSpawner>(Lifetime.Singleton);
            builder.Register<ItemRegistry>(Lifetime.Singleton);
            builder.Register<ItemAuthority>(Lifetime.Singleton);

            // UI
            builder.Register<UiNotifications>(Lifetime.Singleton).AsSelf().As<IUserNotifier>().As<IHudFeedback>();
            builder.Register<UiRoot>(Lifetime.Singleton);

            builder.RegisterBuildCallback(Boot);
        }

        private void Boot(IObjectResolver container)
        {
            container.Resolve<NetworkPrefabInjector>().Register(CollectNetworkPrefabs());
            container.Resolve<NgoSessionService>().Initialize();
            container.Resolve<CharacterSpawner>();
            container.Resolve<ItemAuthority>();

            var ui = container.Resolve<UiRoot>();
            ui.Initialize();

            var flow = container.Resolve<AppFlow>();
            flow.Initialize();

            if (!string.IsNullOrEmpty(_startupNotice))
            {
                container.Resolve<IUserNotifier>().ShowMessage("Steam", _startupNotice);
            }

            SceneManager.LoadScene(SceneNames.Menu, LoadSceneMode.Single);
            RunStartupCommandsAsync(flow, container.Resolve<IPlayerIdentity>(), container.Resolve<AppSettings>()).Forget();
        }

        private async UniTaskVoid RunStartupCommandsAsync(AppFlow flow, IPlayerIdentity identity, AppSettings settings)
        {
            // Boot runs inside Awake of the first scene, but Netcode registers its message types in
            // RuntimeInitializeOnLoad(AfterSceneLoad); starting a session before that breaks the client.
            await UniTask.NextFrame();
            if (_args.Host)
            {
                flow.Host(new HostRequest($"{identity.DisplayName}'s session", settings.MaxPlayers, Domain.Session.LobbyVisibility.FriendsOnly));
            }
            else if (!string.IsNullOrEmpty(_args.JoinTarget))
            {
                flow.Join(_args.JoinTarget);
            }
        }

        private IEnumerable<NetworkObject> CollectNetworkPrefabs()
        {
            yield return _config.SessionStatePrefab;
            yield return _config.PlayerPrefab;
            foreach (NetworkObject item in _config.ItemCatalog.Items)
            {
                yield return item;
            }
        }

        private NetworkManager CreateNetworkManager()
        {
            var managerObject = new GameObject("NetworkManager");
            DontDestroyOnLoad(managerObject);
            var networkManager = managerObject.AddComponent<NetworkManager>();
            // NetworkConfig is only created by the editor Reset() of a scene component; create it for a runtime one.
            networkManager.NetworkConfig ??= new NetworkConfig();
            NetworkConfig config = networkManager.NetworkConfig;
            config.TickRate = _config.TickRate;
            config.EnableSceneManagement = true;
            config.ConnectionApproval = true;
            config.ClientConnectionBufferTimeout = 10;
            config.LoadSceneTimeOut = 30;
            config.PlayerPrefab = null;
            networkManager.LogLevel = LogLevel.Normal;

            if (_args.SimulatedDelayMs.HasValue)
            {
                // Bad-network testing (e.g. -netsim 150 5): delay + loss applied to this peer's traffic.
                var simulator = managerObject.AddComponent<NetworkSimulator>();
                simulator.ConnectionPreset = NetworkSimulatorPreset.Create("CommandLine",
                    packetDelayMs: _args.SimulatedDelayMs.Value, packetJitterMs: _args.SimulatedDelayMs.Value / 10,
                    packetLossPercent: _args.SimulatedLossPercent);
                Debug.Log($"[App] Network simulator: {_args.SimulatedDelayMs} ms delay, {_args.SimulatedLossPercent}% loss.");
            }

            return networkManager;
        }
    }
}
