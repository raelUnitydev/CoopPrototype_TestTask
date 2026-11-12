using System;
using Game.Domain.Session;

namespace Game.Net.Session
{
    /// <summary>
    /// Plain C# bridge between DI-registered services and the currently spawned <see cref="SessionNetworkState"/>
    /// (a network object that exists only while a session runs).
    /// </summary>
    public sealed class SessionStateHolder
    {
        public SessionNetworkState State { get; private set; }

        public SessionPhase Phase => State != null ? State.Phase : SessionPhase.None;

        public event Action<SessionNetworkState> Attached;
        public event Action Detached;

        /// <summary>Raised on clients when the host announced a graceful shutdown.</summary>
        public event Action HostClosing;

        internal void Attach(SessionNetworkState state)
        {
            State = state;
            Attached?.Invoke(state);
        }

        internal void Detach(SessionNetworkState state)
        {
            if (State != state)
            {
                return;
            }

            State = null;
            Detached?.Invoke();
        }

        internal void RaiseHostClosing() => HostClosing?.Invoke();
    }
}
