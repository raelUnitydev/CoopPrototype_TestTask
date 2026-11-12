using System;
using Game.Domain.Stats;

namespace Game.Net.Session
{
    /// <summary>
    /// Host-side session events for gameplay features (character spawning, releasing held items, ...).
    /// Features subscribe here instead of hooking into Netcode connection callbacks themselves.
    /// </summary>
    public sealed class ServerSessionEvents
    {
        private readonly SessionStateHolder _state;

        public ServerSessionEvents(SessionStateHolder state)
        {
            _state = state;
        }

        /// <summary>The client has the level loaded and can get its character.</summary>
        public event Action<ulong> ClientReadyInGame;

        /// <summary>The client left the session (graceful, kicked, timed out or crashed).</summary>
        public event Action<ulong> ClientLeft;

        /// <summary>Forwards a validated gameplay event to the player who owns the stats.</summary>
        public void ReportStat(ulong clientId, StatEvent statEvent)
        {
            _state.State?.ServerSendStat(clientId, statEvent);
        }

        internal void RaiseClientReadyInGame(ulong clientId) => ClientReadyInGame?.Invoke(clientId);

        internal void RaiseClientLeft(ulong clientId) => ClientLeft?.Invoke(clientId);
    }
}
