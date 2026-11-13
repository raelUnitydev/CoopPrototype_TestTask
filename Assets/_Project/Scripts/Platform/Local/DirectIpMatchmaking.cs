using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Application.Flow;
using Game.Application.Ports;
using Game.Domain.Reactive;
using Game.Domain.Session;
using Game.Net.Transport;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Game.Platform.Local
{
    /// <summary>Unity Transport bound to IP:port. Provides the transport component and resolves "ip:port" targets.</summary>
    public sealed class UtpTransportProvider : INetworkTransportProvider
    {
        public UtpTransportProvider(NetworkManager networkManager)
        {
            var transport = networkManager.gameObject.AddComponent<UnityTransport>();
            // Detect a dead host/client within a few seconds (defaults are tuned for 30 s).
            transport.ConnectTimeoutMS = 1000;
            transport.MaxConnectAttempts = 10;
            transport.DisconnectTimeoutMS = 8000;
            transport.HeartbeatTimeoutMS = 500;
            UnityTransport = transport;
        }

        public UnityTransport UnityTransport { get; }

        public NetworkTransport Transport => UnityTransport;
    }

    /// <summary>Direct connection by IP:port; no lobby service behind it.</summary>
    public sealed class DirectIpMatchmaking : IMatchmakingService
    {
        private const string ListenAddress = "0.0.0.0";

        private readonly UtpTransportProvider _transport;
        private readonly AppSettings _settings;
        private readonly ReactiveProperty<string> _sessionCode = new ReactiveProperty<string>(string.Empty);

        public DirectIpMatchmaking(UtpTransportProvider transport, AppSettings settings)
        {
            _transport = transport;
            _settings = settings;
        }

        public MatchmakingKind Kind => MatchmakingKind.DirectIp;
        public string DefaultJoinTarget => $"127.0.0.1:{_settings.Port}";
        public IReadOnlyReactiveProperty<string> SessionCode => _sessionCode;
        public bool SupportsInvites => false;

#pragma warning disable CS0067 // Direct IP has no platform-driven joins.
        public event Action<string> JoinRequested;
#pragma warning restore CS0067

        public UniTask<DisconnectReason> PrepareHostAsync(HostRequest request, CancellationToken cancellationToken)
        {
            _transport.UnityTransport.SetConnectionData(true, "127.0.0.1", _settings.Port, ListenAddress);
            _sessionCode.Value = $"{LocalAddressResolver.GetLanAddress()}:{_settings.Port}";
            return UniTask.FromResult(DisconnectReason.None);
        }

        public UniTask<DisconnectReason> PrepareJoinAsync(string target, CancellationToken cancellationToken)
        {
            if (!TryParseEndpoint(target, _settings.Port, out string address, out ushort port))
            {
                Debug.LogWarning($"[DirectIp] Invalid address '{target}'.");
                return UniTask.FromResult(DisconnectReason.PlatformError);
            }

            _transport.UnityTransport.SetConnectionData(true, address, port);
            _sessionCode.Value = $"{address}:{port}";
            return UniTask.FromResult(DisconnectReason.None);
        }

        public void PublishSessionState(PublicSessionState state)
        {
        }

        public void Leave() => _sessionCode.Value = string.Empty;

        public void OpenInviteDialog()
        {
        }

        public bool TryConsumeLaunchJoinTarget(out string target)
        {
            target = null;
            return false;
        }

        public static bool TryParseEndpoint(string text, ushort defaultPort, out string address, out ushort port)
        {
            address = null;
            port = defaultPort;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.Trim();
            int colon = trimmed.LastIndexOf(':');
            if (colon >= 0)
            {
                if (!ushort.TryParse(trimmed.Substring(colon + 1), out port) || port == 0)
                {
                    return false;
                }

                trimmed = trimmed.Substring(0, colon);
            }

            if (trimmed == "localhost")
            {
                trimmed = "127.0.0.1";
            }

            if (!System.Net.IPAddress.TryParse(trimmed, out System.Net.IPAddress ip)
                || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return false;
            }

            address = trimmed;
            return true;
        }
    }

    internal static class LocalAddressResolver
    {
        /// <summary>Best-effort LAN IPv4 of this machine, shown in the lobby so others know where to connect.</summary>
        public static string GetLanAddress()
        {
            try
            {
                using (var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork,
                           System.Net.Sockets.SocketType.Dgram, 0))
                {
                    // UDP connect does not send packets; it only selects the outgoing interface.
                    socket.Connect("8.8.8.8", 65530);
                    return ((System.Net.IPEndPoint)socket.LocalEndPoint).Address.ToString();
                }
            }
            catch (Exception)
            {
                return "127.0.0.1";
            }
        }
    }
}
