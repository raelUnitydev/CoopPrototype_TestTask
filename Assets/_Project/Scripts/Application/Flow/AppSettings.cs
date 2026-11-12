namespace Game.Application.Flow
{
    /// <summary>Resolved runtime settings (config asset + command line).</summary>
    public sealed class AppSettings
    {
        public string BuildVersion { get; }
        public int MaxPlayers { get; }
        public ushort Port { get; }

        public AppSettings(string buildVersion, int maxPlayers, ushort port)
        {
            BuildVersion = buildVersion;
            MaxPlayers = maxPlayers;
            Port = port;
        }
    }

    /// <summary>Whether gameplay may consume input (UI overlays take the cursor).</summary>
    public sealed class InputGate
    {
        private int _blockers;

        public bool GameplayEnabled => _blockers == 0;

        public void Block() => _blockers++;

        public void Unblock()
        {
            if (_blockers > 0)
            {
                _blockers--;
            }
        }
    }
}
