using Game.Application.Flow;
using Game.Application.Ports;
using Unity.Netcode;
using UnityEngine;
using VContainer;

namespace Game.Features.Player
{
    /// <summary>
    /// First-person character. Owner-authoritative: the owner simulates its own movement and the
    /// (owner-mode) NetworkTransform replicates the result; remote peers only interpolate.
    /// The camera is created locally for the owner and is not part of the networked prefab.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerCharacter : NetworkBehaviour
    {
        [SerializeField] private Transform _head;
        [SerializeField] private float _walkSpeed = 4.5f;
        [SerializeField] private float _sprintSpeed = 7.5f;
        [SerializeField] private float _jumpHeight = 1.1f;
        [SerializeField] private float _gravity = -20f;
        [SerializeField] private float _mouseSensitivity = 0.12f;

        private CharacterController _controller;
        private GameInput _input;
        private InputGate _inputGate;
        private ISessionCommands _sessionCommands;
        private Camera _camera;
        private Camera _disabledSceneCamera;
        private float _pitch;
        private float _verticalVelocity;

        /// <summary>World-space velocity of the last owner movement step (used to inherit throw momentum).</summary>
        public Vector3 Velocity { get; private set; }

        public Transform Head => _head;

        /// <summary>Owner only: camera eye position and look direction.</summary>
        public Vector3 ViewOrigin => _camera != null ? _camera.transform.position : _head.position;
        public Vector3 ViewForward => _camera != null ? _camera.transform.forward : transform.forward;
        public Quaternion ViewRotation => _camera != null ? _camera.transform.rotation : transform.rotation;

        [Inject]
        public void Construct(GameInput input, InputGate inputGate, ISessionCommands sessionCommands)
        {
            _input = input;
            _inputGate = inputGate;
            _sessionCommands = sessionCommands;
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        public override void OnNetworkSpawn()
        {
            // The CharacterController stays enabled on every peer so remote players remain solid colliders
            // for the host's item physics, but only the owner ever calls Move().
            if (IsOwner)
            {
                CreateOwnerCamera();
            }
        }

        public override void OnNetworkDespawn()
        {
            DestroyOwnerCamera();
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner)
            {
                return;
            }

            bool inputEnabled = _inputGate.GameplayEnabled;
            Cursor.lockState = inputEnabled ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !inputEnabled;

            if (inputEnabled)
            {
                Vector2 look = _input.Look.ReadValue<Vector2>() * _mouseSensitivity;
                transform.Rotate(0f, look.x, 0f);
                _pitch = Mathf.Clamp(_pitch - look.y, -85f, 85f);

                if (_input.ChangeColor.WasPressedThisFrame())
                {
                    _sessionCommands.RequestColorChange();
                }
            }

            Move(inputEnabled);
        }

        private void LateUpdate()
        {
            if (_camera != null)
            {
                _camera.transform.SetPositionAndRotation(_head.position, transform.rotation * Quaternion.Euler(_pitch, 0f, 0f));
            }
        }

        private void Move(bool inputEnabled)
        {
            Vector2 moveInput = inputEnabled ? _input.Move.ReadValue<Vector2>() : Vector2.zero;
            bool sprint = inputEnabled && _input.Sprint.IsPressed();
            Vector3 horizontal = (transform.right * moveInput.x + transform.forward * moveInput.y)
                                 * (sprint ? _sprintSpeed : _walkSpeed);

            if (_controller.isGrounded)
            {
                _verticalVelocity = -1f;
                if (inputEnabled && _input.Jump.WasPressedThisFrame())
                {
                    _verticalVelocity = Mathf.Sqrt(_jumpHeight * -2f * _gravity);
                }
            }

            _verticalVelocity += _gravity * Time.deltaTime;
            Vector3 velocity = horizontal + Vector3.up * _verticalVelocity;
            _controller.Move(velocity * Time.deltaTime);
            Velocity = velocity;

            // Safety net if the player falls off the level.
            if (transform.position.y < -30f)
            {
                _controller.enabled = false;
                transform.position = new Vector3(0f, 2f, 0f);
                _controller.enabled = true;
            }
        }

        private void CreateOwnerCamera()
        {
            _disabledSceneCamera = Camera.main;
            if (_disabledSceneCamera != null)
            {
                _disabledSceneCamera.enabled = false;
            }

            var cameraObject = new GameObject("LocalPlayerCamera");
            _camera = cameraObject.AddComponent<Camera>();
            _camera.nearClipPlane = 0.05f;
            _camera.fieldOfView = 75f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.tag = "MainCamera";
        }

        private void DestroyOwnerCamera()
        {
            if (_camera != null)
            {
                Destroy(_camera.gameObject);
                _camera = null;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (_disabledSceneCamera != null)
            {
                _disabledSceneCamera.enabled = true;
                _disabledSceneCamera = null;
            }
        }
    }
}
