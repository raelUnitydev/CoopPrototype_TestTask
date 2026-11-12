using System;
using System.Collections.Generic;
using Game.Application.Ports;
using Game.Domain.Session;
using Game.Domain.Stats;
using Unity.Netcode;
using UnityEngine;
using VContainer;

namespace Game.Net.Session
{
    /// <summary>
    /// Host-owned network object that lives for the whole session (DontDestroyOnLoad, survives level loads).
    /// Holds the replicated roster and phase - late joiners receive both automatically on spawn.
    /// </summary>
    public sealed class SessionNetworkState : NetworkBehaviour
    {
        private const float PingInterval = 1f;
        private const float RttSmoothing = 0.3f;
        private const int RttPublishThresholdMs = 3;

        private readonly NetworkList<PlayerEntry> _players = new NetworkList<PlayerEntry>();
        private readonly NetworkVariable<SessionPhase> _phase = new NetworkVariable<SessionPhase>(SessionPhase.None);
        private readonly Dictionary<ulong, float> _smoothedRtt = new Dictionary<ulong, float>();
        private readonly List<ulong> _pingTargets = new List<ulong>();

        private SessionStateHolder _holder;
        private IStatEventSink _statSink;
        private float _nextPingTime;

        public SessionPhase Phase => _phase.Value;
        public NetworkList<PlayerEntry> Players => _players;

        public event Action RosterChanged;
        public event Action<SessionPhase> PhaseChanged;

        [Inject]
        public void Construct(SessionStateHolder holder, IStatEventSink statSink)
        {
            _holder = holder;
            _statSink = statSink;
        }

        private void Awake()
        {
            // Session state must survive Netcode scene switches (lobby -> level).
            DontDestroyOnLoad(gameObject);
        }

        public override void OnNetworkSpawn()
        {
            _players.OnListChanged += OnPlayersChanged;
            _phase.OnValueChanged += OnPhaseChanged;
            _holder.Attach(this);
        }

        public override void OnNetworkDespawn()
        {
            _players.OnListChanged -= OnPlayersChanged;
            _phase.OnValueChanged -= OnPhaseChanged;
            _holder.Detach(this);
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || Time.unscaledTime < _nextPingTime)
            {
                return;
            }

            _nextPingTime = Time.unscaledTime + PingInterval;
            SendPings();
        }

        #region Server API

        public void ServerSetPhase(SessionPhase phase)
        {
            AssertServer();
            _phase.Value = phase;
        }

        public void ServerAddPlayer(ulong clientId, in ConnectionRequest request, bool isLoaded)
        {
            AssertServer();
            if (IndexOf(clientId) >= 0)
            {
                return;
            }

            _players.Add(new PlayerEntry
            {
                ClientId = clientId,
                PlayerId = PlayerEntry.ToFixed(request.PlayerId.Value),
                Name = PlayerEntry.ToFixed(request.DisplayName),
                ColorIndex = (byte)(_players.Count % PlayerColors.Count),
                IsLoaded = isLoaded
            });
        }

        public void ServerRemovePlayer(ulong clientId)
        {
            AssertServer();
            int index = IndexOf(clientId);
            if (index >= 0)
            {
                _players.RemoveAt(index);
            }

            _smoothedRtt.Remove(clientId);
        }

        public void ServerSetLoaded(ulong clientId, bool isLoaded)
        {
            AssertServer();
            Modify(clientId, entry =>
            {
                entry.IsLoaded = isLoaded;
                return entry;
            });
        }

        public void ServerSetAllLoaded(bool isLoaded)
        {
            AssertServer();
            for (int i = 0; i < _players.Count; i++)
            {
                PlayerEntry entry = _players[i];
                if (entry.IsLoaded != isLoaded)
                {
                    entry.IsLoaded = isLoaded;
                    _players[i] = entry;
                }
            }
        }

        public void ServerNotifyHostClosing()
        {
            AssertServer();
            HostClosingRpc();
        }

        public void ServerSendStat(ulong clientId, StatEvent statEvent)
        {
            AssertServer();
            StatEventRpc(statEvent, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        #endregion

        public void RequestColorChange() => ChangeColorRpc();

        public bool TryGetPlayer(ulong clientId, out PlayerEntry entry)
        {
            int index = IndexOf(clientId);
            entry = index >= 0 ? _players[index] : default;
            return index >= 0;
        }

        #region RPCs

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ChangeColorRpc(RpcParams rpcParams = default)
        {
            // Color is stored in the roster (single source of truth), so late joiners see it too.
            Modify(rpcParams.Receive.SenderClientId, entry =>
            {
                entry.ColorIndex = PlayerColors.Next(entry.ColorIndex);
                return entry;
            });
        }

        // Application-level ping: works the same on every transport (the Steam transport reports no RTT).
        [Rpc(SendTo.SpecifiedInParams, Delivery = RpcDelivery.Unreliable)]
        private void PingRpc(double serverTime, RpcParams rpcParams)
        {
            PongRpc(serverTime);
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Everyone)]
        private void PongRpc(double serverTime, RpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;
            float sample = (float)((Time.realtimeSinceStartupAsDouble - serverTime) * 1000.0);
            if (sample < 0f || sample > 10000f)
            {
                return;
            }

            float smoothed = _smoothedRtt.TryGetValue(clientId, out float previous)
                ? Mathf.Lerp(previous, sample, RttSmoothing)
                : sample;
            _smoothedRtt[clientId] = smoothed;

            var rtt = (ushort)Mathf.Clamp(Mathf.RoundToInt(smoothed), 0, ushort.MaxValue);
            Modify(clientId, entry =>
            {
                // Skip tiny changes to avoid re-sending the roster row every second.
                if (Mathf.Abs(entry.RttMs - rtt) >= RttPublishThresholdMs)
                {
                    entry.RttMs = rtt;
                }

                return entry;
            });
        }

        [Rpc(SendTo.NotServer)]
        private void HostClosingRpc()
        {
            _holder.RaiseHostClosing();
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void StatEventRpc(StatEvent statEvent, RpcParams rpcParams)
        {
            _statSink.OnStatEvent(statEvent);
        }

        #endregion

        private void SendPings()
        {
            _pingTargets.Clear();
            foreach (PlayerEntry entry in _players)
            {
                if (entry.ClientId != NetworkManager.ServerClientId)
                {
                    _pingTargets.Add(entry.ClientId);
                }
            }

            double now = Time.realtimeSinceStartupAsDouble;
            foreach (ulong clientId in _pingTargets)
            {
                PingRpc(now, RpcTarget.Single(clientId, RpcTargetUse.Temp));
            }
        }

        private void Modify(ulong clientId, Func<PlayerEntry, PlayerEntry> change)
        {
            int index = IndexOf(clientId);
            if (index < 0)
            {
                return;
            }

            PlayerEntry current = _players[index];
            PlayerEntry updated = change(current);
            if (!updated.Equals(current))
            {
                _players[index] = updated;
            }
        }

        private int IndexOf(ulong clientId)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].ClientId == clientId)
                {
                    return i;
                }
            }

            return -1;
        }

        private void OnPlayersChanged(NetworkListEvent<PlayerEntry> change) => RosterChanged?.Invoke();

        private void OnPhaseChanged(SessionPhase previous, SessionPhase current) => PhaseChanged?.Invoke(current);

        private void AssertServer()
        {
            if (!IsServer)
            {
                throw new InvalidOperationException("Server-only API called on a client.");
            }
        }
    }
}
