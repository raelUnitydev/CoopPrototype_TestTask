using Game.Domain.Items;
using Unity.Netcode;
using UnityEngine;
using VContainer;

namespace Game.Features.Items
{
    /// <summary>
    /// Shared physics item.
    /// Free:  owned by the host; only the host simulates physics, everyone else interpolates the
    ///        NetworkTransform (owner authority mode, so authority follows ownership).
    /// Held:  ownership is transferred to the holder, who moves it kinematically in front of the camera.
    /// Thrown: ownership returns to the host, which applies the throw from the position the holder
    ///        reported, so the flight is a single host simulation that every peer interpolates.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class NetworkItem : NetworkBehaviour
    {
        private const float FollowSharpness = 30f;

        private readonly NetworkVariable<ulong> _holder = new NetworkVariable<ulong>(ItemRules.NoHolder);

        private Rigidbody _body;
        private Collider[] _colliders;
        private ItemRegistry _registry;
        private bool _releasePending;

        public ulong Holder => _holder.Value;
        public bool IsFree => _holder.Value == ItemRules.NoHolder;

        /// <summary>True on the holder's machine while it drives the item.</summary>
        public bool IsLocallyHeld => IsSpawned && IsOwner && _holder.Value == NetworkManager.LocalClientId && !_releasePending;

        [Inject]
        public void Construct(ItemRegistry registry)
        {
            _registry = registry;
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _colliders = GetComponentsInChildren<Collider>();
        }

        public override void OnNetworkSpawn()
        {
            _registry.Add(this);
            _holder.OnValueChanged += OnHolderChanged;
            ApplyPhysicsMode();
        }

        public override void OnNetworkDespawn()
        {
            _registry.Remove(this);
            _holder.OnValueChanged -= OnHolderChanged;
        }

        protected override void OnOwnershipChanged(ulong previous, ulong current)
        {
            base.OnOwnershipChanged(previous, current);
            _releasePending = false;
            ApplyPhysicsMode();
        }

        #region Holder (owner) side

        /// <summary>Holder only: moves the item towards the hold point. Replicated by the owner-mode NetworkTransform.</summary>
        public void MoveHeld(Vector3 position, Quaternion rotation, float deltaTime)
        {
            if (!IsLocallyHeld)
            {
                return;
            }

            float t = 1f - Mathf.Exp(-FollowSharpness * deltaTime);
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, position, t),
                Quaternion.Slerp(transform.rotation, rotation, t));
        }

        /// <summary>
        /// Holder only: stop driving the item right after sending a release/throw request. It stays where it
        /// was released until the host takes ownership back and its simulation reaches us - no snapping back.
        /// </summary>
        public void MarkReleasePending() => _releasePending = true;

        /// <summary>Holder only: the host rejected the release, keep holding.</summary>
        public void CancelReleasePending() => _releasePending = false;

        #endregion

        #region Server side

        public void ServerGrab(ulong clientId)
        {
            _holder.Value = clientId;
            if (OwnerClientId != clientId)
            {
                NetworkObject.ChangeOwnership(clientId);
            }

            ApplyPhysicsMode();
        }

        public void ServerRelease(Vector3 position, Quaternion rotation, Vector3 velocity)
        {
            // Take authority back first, otherwise our transform write would be overwritten by interpolation.
            NetworkObject.RemoveOwnership();
            _holder.Value = ItemRules.NoHolder;
            ApplyPhysicsMode();

            transform.SetPositionAndRotation(position, rotation);
            _body.position = position;
            _body.rotation = rotation;
            _body.linearVelocity = velocity;
            _body.angularVelocity = Vector3.zero;
            _body.WakeUp();
        }

        /// <summary>Holder disconnected: the item drops where it is.</summary>
        public void ServerForceRelease()
        {
            if (OwnerClientId != NetworkManager.ServerClientId)
            {
                NetworkObject.RemoveOwnership();
            }

            _holder.Value = ItemRules.NoHolder;
            ApplyPhysicsMode();
            _body.linearVelocity = Vector3.zero;
            _body.WakeUp();
        }

        #endregion

        private void OnHolderChanged(ulong previous, ulong current)
        {
            // The host holds items without an ownership change (it already owns them), so the pending
            // release must also be cleared here, not only in OnOwnershipChanged.
            _releasePending = false;
            ApplyPhysicsMode();
        }

        private void ApplyPhysicsMode()
        {
            bool held = !IsFree;

            // Only the host simulates a free item. Everyone else (and the holder) is kinematic,
            // so local physics never fights the replicated transform - this is what prevents
            // corrections/teleports on landing.
            bool simulate = IsServer && !held;
            _body.isKinematic = !simulate;
            _body.interpolation = simulate ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;

            // A held item must not push its holder or other items around.
            foreach (Collider itemCollider in _colliders)
            {
                itemCollider.enabled = !held;
            }
        }
    }
}
