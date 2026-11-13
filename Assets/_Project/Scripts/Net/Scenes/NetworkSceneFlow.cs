using System;
using System.Collections.Generic;
using Game.Domain.Reactive;
using Game.Domain.Session;
using Game.Net.Session;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Net.Scenes
{
    public static class SceneNames
    {
        public const string Bootstrap = "Bootstrap";
        public const string Menu = "Menu";
        public const string Lobby = "Lobby";
        public const string Game = "Game";
    }

    /// <summary>
    /// Networked scene switching. The host loads scenes through Netcode, tracks which clients finished
    /// loading and flips the replicated phase to InGame only when the last one is done - the phase change
    /// reaches every client in the same tick, so all loading screens close together. Clients that drop
    /// during loading are removed from the wait set, so nobody gets stuck.
    /// </summary>
    public sealed class NetworkSceneFlow : IDisposable
    {
        private readonly NetworkManager _networkManager;
        private readonly SessionStateHolder _holder;
        private readonly ServerSessionEvents _serverEvents;
        private readonly HashSet<ulong> _waitingForClients = new HashSet<ulong>();
        private readonly HashSet<ulong> _synchronizedClients = new HashSet<ulong>();
        private readonly ReactiveProperty<bool> _isLocalSceneLoading = new ReactiveProperty<bool>();
        private NetworkSceneManager _hookedSceneManager;

        public NetworkSceneFlow(NetworkManager networkManager, SessionStateHolder holder, ServerSessionEvents serverEvents)
        {
            _networkManager = networkManager;
            _holder = holder;
            _serverEvents = serverEvents;
            _networkManager.OnServerStarted += HookSceneEvents;
            _networkManager.OnClientStarted += HookSceneEvents;
            _networkManager.OnClientStopped += OnStopped;
            _networkManager.OnServerStopped += OnStopped;
        }

        /// <summary>True while this peer is loading/synchronizing a network scene.</summary>
        public IReadOnlyReactiveProperty<bool> IsLocalSceneLoading => _isLocalSceneLoading;

        public void ServerLoadLobby()
        {
            SessionNetworkState state = _holder.State;
            state.ServerSetPhase(SessionPhase.Lobby);
            state.ServerSetAllLoaded(true);
            _networkManager.SceneManager.LoadScene(SceneNames.Lobby, LoadSceneMode.Single);
        }

        public void ServerStartGame()
        {
            SessionNetworkState state = _holder.State;
            if (!_networkManager.IsServer || state == null || state.Phase != SessionPhase.Lobby)
            {
                return;
            }

            _waitingForClients.Clear();
            foreach (ulong clientId in _networkManager.ConnectedClientsIds)
            {
                _waitingForClients.Add(clientId);
            }

            state.ServerSetPhase(SessionPhase.Loading);
            state.ServerSetAllLoaded(false);

            SceneEventProgressStatus status = _networkManager.SceneManager.LoadScene(SceneNames.Game, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"[Scenes] Failed to start loading the level: {status}");
                state.ServerSetPhase(SessionPhase.Lobby);
                state.ServerSetAllLoaded(true);
            }
        }

        /// <summary>Called by the session service when a client disconnects.</summary>
        public void ServerOnClientLeft(ulong clientId)
        {
            _synchronizedClients.Remove(clientId);
            if (_waitingForClients.Remove(clientId))
            {
                TryCompleteLoading();
            }
        }

        public void Dispose()
        {
            _networkManager.OnServerStarted -= HookSceneEvents;
            _networkManager.OnClientStarted -= HookSceneEvents;
            _networkManager.OnClientStopped -= OnStopped;
            _networkManager.OnServerStopped -= OnStopped;
            UnhookSceneEvents();
        }

        private void HookSceneEvents()
        {
            // On a host both OnServerStarted and OnClientStarted fire.
            if (_hookedSceneManager == _networkManager.SceneManager || _networkManager.SceneManager == null)
            {
                return;
            }

            UnhookSceneEvents();
            _hookedSceneManager = _networkManager.SceneManager;
            _hookedSceneManager.OnSceneEvent += OnSceneEvent;
        }

        private void UnhookSceneEvents()
        {
            if (_hookedSceneManager != null)
            {
                _hookedSceneManager.OnSceneEvent -= OnSceneEvent;
                _hookedSceneManager = null;
            }
        }

        /// <summary>
        /// Called by the session service after the client was added to the roster. Netcode may report the
        /// end of the initial scene synchronization before or after the connection callback, so the
        /// "ready" transition happens when both are known.
        /// </summary>
        public void ServerOnClientRegistered(ulong clientId)
        {
            if (_synchronizedClients.Contains(clientId))
            {
                MarkSynchronizedClientReady(clientId);
            }
        }

        private void OnStopped(bool _)
        {
            UnhookSceneEvents();
            _waitingForClients.Clear();
            _synchronizedClients.Clear();
            _isLocalSceneLoading.Value = false;
        }

        private void OnSceneEvent(SceneEvent sceneEvent)
        {
            TrackLocalLoading(sceneEvent);

            if (!_networkManager.IsServer || _holder.State == null)
            {
                return;
            }

            switch (sceneEvent.SceneEventType)
            {
                case SceneEventType.LoadComplete when sceneEvent.SceneName == SceneNames.Game:
                    if (_holder.State.Phase == SessionPhase.Loading)
                    {
                        _holder.State.ServerSetLoaded(sceneEvent.ClientId, true);
                        _waitingForClients.Remove(sceneEvent.ClientId);
                        TryCompleteLoading();
                    }

                    break;

                case SceneEventType.LoadEventCompleted when sceneEvent.SceneName == SceneNames.Game:
                    // Safety net: clients that did not finish within Netcode's load timeout are dropped.
                    if (sceneEvent.ClientsThatTimedOut != null)
                    {
                        foreach (ulong clientId in sceneEvent.ClientsThatTimedOut)
                        {
                            _networkManager.DisconnectClient(clientId, DisconnectReasonCodec.Encode(DisconnectReason.ConnectTimeout));
                            _waitingForClients.Remove(clientId);
                        }
                    }

                    TryCompleteLoading();
                    break;

                case SceneEventType.SynchronizeComplete:
                    // (Late) join: the client synchronized the current scene on its own; nobody waited for it.
                    _synchronizedClients.Add(sceneEvent.ClientId);
                    MarkSynchronizedClientReady(sceneEvent.ClientId);
                    break;
            }
        }

        private void MarkSynchronizedClientReady(ulong clientId)
        {
            SessionNetworkState state = _holder.State;
            if (state == null || !state.TryGetPlayer(clientId, out _))
            {
                return; // Not in the roster yet; ServerOnClientRegistered will retry.
            }

            state.ServerSetLoaded(clientId, true);
            if (state.Phase == SessionPhase.InGame)
            {
                _serverEvents.RaiseClientReadyInGame(clientId);
            }
        }

        private void TrackLocalLoading(SceneEvent sceneEvent)
        {
            if (sceneEvent.ClientId != _networkManager.LocalClientId)
            {
                return;
            }

            switch (sceneEvent.SceneEventType)
            {
                case SceneEventType.Load:
                case SceneEventType.Synchronize:
                    _isLocalSceneLoading.Value = true;
                    break;
                case SceneEventType.LoadComplete:
                case SceneEventType.SynchronizeComplete:
                    _isLocalSceneLoading.Value = false;
                    break;
            }
        }

        private void TryCompleteLoading()
        {
            SessionNetworkState state = _holder.State;
            if (state == null || state.Phase != SessionPhase.Loading || _waitingForClients.Count > 0)
            {
                return;
            }

            state.ServerSetPhase(SessionPhase.InGame);
            Debug.Log("[Scenes] All clients loaded the level.");

            foreach (ulong clientId in _networkManager.ConnectedClientsIds)
            {
                _serverEvents.RaiseClientReadyInGame(clientId);
            }
        }
    }
}
