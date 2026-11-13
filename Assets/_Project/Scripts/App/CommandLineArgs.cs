using System;
using System.Globalization;

namespace Game.App
{
    /// <summary>
    /// Supported arguments:
    /// -transport utp|steam, -host, -join &lt;ip:port | lobbyId&gt;, -name &lt;name&gt;, -port &lt;port&gt;,
    /// -profile &lt;slot&gt;, -maxplayers &lt;n&gt;, -buildversion &lt;version&gt; (to test version mismatch),
    /// -netsim &lt;delayMs&gt; [lossPercent] (Multiplayer Tools Network Simulator, Unity Transport only).
    /// </summary>
    public sealed class CommandLineArgs
    {
        public PlatformMode? Platform { get; private set; }
        public bool Host { get; private set; }
        public string JoinTarget { get; private set; }
        public string Name { get; private set; }
        public ushort? Port { get; private set; }
        public int? Profile { get; private set; }
        public int? MaxPlayers { get; private set; }
        public string BuildVersion { get; private set; }
        public int? SimulatedDelayMs { get; private set; }
        public int SimulatedLossPercent { get; private set; }

        public static CommandLineArgs Parse(string[] args)
        {
            var result = new CommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i].ToLowerInvariant();
                string value = i + 1 < args.Length ? args[i + 1] : null;
                switch (key)
                {
                    case "-transport":
                        if (string.Equals(value, "steam", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Platform = PlatformMode.Steam;
                        }
                        else if (string.Equals(value, "utp", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(value, "ip", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Platform = PlatformMode.DirectIp;
                        }

                        break;
                    case "-host":
                        result.Host = true;
                        break;
                    case "-join":
                        result.JoinTarget = value;
                        break;
                    case "-name":
                        result.Name = value;
                        break;
                    case "-port":
                        if (ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort port))
                        {
                            result.Port = port;
                        }

                        break;
                    case "-profile":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int profile))
                        {
                            result.Profile = profile;
                        }

                        break;
                    case "-maxplayers":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxPlayers))
                        {
                            result.MaxPlayers = maxPlayers;
                        }

                        break;
                    case "-buildversion":
                        result.BuildVersion = value;
                        break;
                    case "-netsim":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int delay))
                        {
                            result.SimulatedDelayMs = delay;
                            if (i + 2 < args.Length
                                && int.TryParse(args[i + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int loss))
                            {
                                result.SimulatedLossPercent = loss;
                            }
                        }

                        break;
                }
            }

            return result;
        }
    }
}
