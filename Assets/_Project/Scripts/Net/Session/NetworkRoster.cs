using System;
using System.Collections.Generic;
using Game.Application.Ports;
using Game.Domain.Session;
using Unity.Netcode;

namespace Game.Net.Session
{
    /// <summary>Exposes the replicated roster to UI/gameplay through Netcode-free interfaces.</summary>
    public sealed class NetworkRoster : IPlayerRoster, ISessionCommands, IDisposable
    {
        private readonly NetworkManager _networkManager;
        private readonly SessionStateHolder _holder;
        private readonly List<PlayerSnapshot> _players = new List<PlayerSnapshot>();
        private SessionNetworkState _state;

        public NetworkRoster(NetworkManager networkManager, SessionStateHolder holder)
        {
            _networkManager = networkManager;
            _holder = holder;
            _holder.Attached += OnAttached;
            _holder.Detached += OnDetached;
        }

        public IReadOnlyList<PlayerSnapshot> Players => _players;

        public event Action Changed;

        public bool TryGet(ulong clientId, out PlayerSnapshot player)
        {
            foreach (PlayerSnapshot snapshot in _players)
            {
                if (snapshot.ClientId == clientId)
                {
                    player = snapshot;
                    return true;
                }
            }

            player = default;
            return false;
        }

        public void RequestColorChange() => _state?.RequestColorChange();

        public void Kick(ulong clientId)
        {
            if (!_networkManager.IsServer || clientId == NetworkManager.ServerClientId)
            {
                return;
            }

            _networkManager.DisconnectClient(clientId, DisconnectReasonCodec.Encode(DisconnectReason.Kicked));
        }

        public void Dispose()
        {
            _holder.Attached -= OnAttached;
            _holder.Detached -= OnDetached;
            OnDetached();
        }

        private void OnAttached(SessionNetworkState state)
        {
            _state = state;
            _state.RosterChanged += Rebuild;
            Rebuild();
        }

        private void OnDetached()
        {
            if (_state != null)
            {
                _state.RosterChanged -= Rebuild;
                _state = null;
            }

            Rebuild();
        }

        private void Rebuild()
        {
            _players.Clear();
            if (_state != null)
            {
                foreach (PlayerEntry entry in _state.Players)
                {
                    _players.Add(entry.ToSnapshot(NetworkManager.ServerClientId));
                }
            }

            Changed?.Invoke();
        }
    }
}
