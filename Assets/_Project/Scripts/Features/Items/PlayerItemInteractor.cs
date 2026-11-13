using Game.Application.Flow;
using Game.Application.Ports;
using Game.Domain.Items;
using Game.Features.Player;
using Unity.Netcode;
using UnityEngine;
using VContainer;

namespace Game.Features.Items
{
    /// <summary>
    /// Lives on the character. The owner turns input into requests; the host executes them through
    /// <see cref="ItemAuthority"/>. RPCs are owner-only (InvokePermission.Owner), so a client can only
    /// ever act for its own character, and the sender id is taken from Netcode, not from the payload.
    /// </summary>
    [RequireComponent(typeof(PlayerCharacter))]
    public sealed class PlayerItemInteractor : NetworkBehaviour
    {
        [SerializeField] private float _reach = 3f;
        [SerializeField] private float _holdDistance = 1.4f;
        [SerializeField] private float _throwSpeed = 12f;

        private PlayerCharacter _character;
        private GameInput _input;
        private InputGate _inputGate;
        private ItemRegistry _registry;
        private ItemAuthority _authority;
        private ItemCatalog _catalog;
        private IHudFeedback _feedback;
        private int _selectedType;
        private float _grabRequestTime = -1f;

        [Inject]
        public void Construct(GameInput input, InputGate inputGate, ItemRegistry registry, ItemAuthority authority,
            ItemCatalog catalog, IHudFeedback feedback)
        {
            _input = input;
            _inputGate = inputGate;
            _registry = registry;
            _authority = authority;
            _catalog = catalog;
            _feedback = feedback;
        }

        private void Awake()
        {
            _character = GetComponent<PlayerCharacter>();
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || !_inputGate.GameplayEnabled)
            {
                return;
            }

            NetworkItem held = _registry.FindHeldBy(OwnerClientId);
            if (held != null && held.IsLocallyHeld)
            {
                HandleHeld(held);
            }
            else if (held == null && (_input.Grab.WasPressedThisFrame() || _input.Throw.WasPressedThisFrame()))
            {
                TryGrab();
            }

            if (_input.SpawnItem.WasPressedThisFrame())
            {
                SpawnRpc((byte)_selectedType);
            }

            if (_input.NextItemType.WasPressedThisFrame() && _catalog.Items.Count > 0)
            {
                _selectedType = (_selectedType + 1) % _catalog.Items.Count;
                _feedback.Show($"Spawn type: {_catalog.Items[_selectedType].name}");
            }
        }

        private void LateUpdate()
        {
            if (!IsSpawned || !IsOwner)
            {
                return;
            }

            NetworkItem held = _registry.FindHeldBy(OwnerClientId);
            if (held != null)
            {
                Vector3 holdPoint = _character.ViewOrigin + _character.ViewForward * _holdDistance;
                held.MoveHeld(holdPoint, transform.rotation, Time.deltaTime);
            }
        }

        private void HandleHeld(NetworkItem held)
        {
            if (_input.Throw.WasPressedThisFrame())
            {
                Vector3 velocity = _character.ViewForward * _throwSpeed + _character.Velocity;
                held.MarkReleasePending();
                ReleaseRpc(held.NetworkObject, held.transform.position, held.transform.rotation, velocity, true);
            }
            else if (_input.Drop.WasPressedThisFrame() || _input.Grab.WasPressedThisFrame())
            {
                held.MarkReleasePending();
                ReleaseRpc(held.NetworkObject, held.transform.position, held.transform.rotation, _character.Velocity, false);
            }
            else if (_input.Delete.WasPressedThisFrame())
            {
                held.MarkReleasePending();
                DeleteRpc(held.NetworkObject);
            }
        }

        private void TryGrab()
        {
            // Debounce: the answer takes one round trip; do not spam while waiting.
            if (Time.unscaledTime - _grabRequestTime < 0.3f)
            {
                return;
            }

            if (!Physics.Raycast(_character.ViewOrigin, _character.ViewForward, out RaycastHit hit, _reach, ~0, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            var item = hit.collider.GetComponentInParent<NetworkItem>();
            if (item == null || !item.IsSpawned)
            {
                return;
            }

            if (!item.IsFree)
            {
                _feedback.Show("Someone is already holding it.");
                return;
            }

            _grabRequestTime = Time.unscaledTime;
            GrabRpc(item.NetworkObject);
        }

        #region RPCs (owner -> host)

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void GrabRpc(NetworkObjectReference itemReference)
        {
            if (TryResolve(itemReference, out NetworkItem item))
            {
                Respond(_authority.Grab(OwnerClientId, item));
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ReleaseRpc(NetworkObjectReference itemReference, Vector3 position, Quaternion rotation, Vector3 velocity, bool isThrow)
        {
            if (TryResolve(itemReference, out NetworkItem item))
            {
                Respond(_authority.Release(OwnerClientId, item, position, rotation, velocity, isThrow));
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void DeleteRpc(NetworkObjectReference itemReference)
        {
            if (TryResolve(itemReference, out NetworkItem item))
            {
                Respond(_authority.Delete(OwnerClientId, item));
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SpawnRpc(byte typeIndex)
        {
            Respond(_authority.Spawn(OwnerClientId, typeIndex));
        }

        [Rpc(SendTo.Owner)]
        private void RequestRejectedRpc(ItemRequestResult result)
        {
            // A rejected release/delete leaves the item in our hands.
            _registry.FindHeldBy(OwnerClientId)?.CancelReleasePending();
            _feedback.Show(Describe(result));
        }

        #endregion

        private void Respond(ItemRequestResult result)
        {
            if (result != ItemRequestResult.Ok)
            {
                RequestRejectedRpc(result);
            }
        }

        private static bool TryResolve(NetworkObjectReference reference, out NetworkItem item)
        {
            item = null;
            return reference.TryGet(out NetworkObject networkObject) && networkObject.TryGetComponent(out item);
        }

        private static string Describe(ItemRequestResult result)
        {
            switch (result)
            {
                case ItemRequestResult.AlreadyHeld: return "Someone else grabbed it first.";
                case ItemRequestResult.TooFar: return "Too far away.";
                case ItemRequestResult.NotHolder: return "You are not holding this item.";
                case ItemRequestResult.RateLimited: return "Spawning too fast.";
                case ItemRequestResult.LimitReached: return "Item limit reached.";
                case ItemRequestResult.PlayerAlreadyHolding: return "Your hands are full.";
                default: return "Request rejected.";
            }
        }
    }
}
