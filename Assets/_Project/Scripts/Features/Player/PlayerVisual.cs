using Game.Application.Ports;
using Game.Domain.Session;
using Unity.Netcode;
using UnityEngine;
using VContainer;

namespace Game.Features.Player
{
    /// <summary>
    /// Applies the roster color and name to the character. The color lives in the replicated roster
    /// (not on the character), so a late joiner gets it together with the rest of the session state.
    /// </summary>
    public sealed class PlayerVisual : NetworkBehaviour
    {
        private static readonly Color[] Palette =
        {
            new Color(0.95f, 0.30f, 0.25f), new Color(0.25f, 0.55f, 0.95f), new Color(0.30f, 0.80f, 0.35f),
            new Color(0.95f, 0.80f, 0.20f), new Color(0.70f, 0.35f, 0.90f), new Color(0.20f, 0.85f, 0.85f),
            new Color(0.95f, 0.55f, 0.15f), new Color(0.90f, 0.90f, 0.90f)
        };

        [SerializeField] private Renderer[] _renderers;
        [SerializeField] private TextMesh _nameLabel;

        private IPlayerRoster _roster;
        private MaterialPropertyBlock _block;
        private Camera _cachedCamera;

        [Inject]
        public void Construct(IPlayerRoster roster)
        {
            _roster = roster;
        }

        public override void OnNetworkSpawn()
        {
            _block = new MaterialPropertyBlock();
            _roster.Changed += Refresh;
            Refresh();

            // Do not render our own body/name in first person.
            if (IsOwner)
            {
                foreach (MeshRenderer meshRenderer in GetComponentsInChildren<MeshRenderer>())
                {
                    if (_nameLabel == null || meshRenderer.gameObject != _nameLabel.gameObject)
                    {
                        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                    }
                }

                if (_nameLabel != null)
                {
                    _nameLabel.gameObject.SetActive(false);
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            _roster.Changed -= Refresh;
        }

        private void LateUpdate()
        {
            if (_nameLabel == null || !_nameLabel.gameObject.activeSelf)
            {
                return;
            }

            if (_cachedCamera == null || !_cachedCamera.isActiveAndEnabled)
            {
                _cachedCamera = Camera.main;
            }

            if (_cachedCamera != null)
            {
                _nameLabel.transform.rotation = Quaternion.LookRotation(_nameLabel.transform.position - _cachedCamera.transform.position);
            }
        }

        public static Color ColorOf(byte index) => Palette[index % Palette.Length];

        private void Refresh()
        {
            if (!_roster.TryGet(OwnerClientId, out PlayerSnapshot player))
            {
                return;
            }

            _block.SetColor("_BaseColor", ColorOf(player.ColorIndex));
            _block.SetColor("_Color", ColorOf(player.ColorIndex));
            foreach (Renderer bodyRenderer in _renderers)
            {
                bodyRenderer.SetPropertyBlock(_block);
            }

            if (_nameLabel != null)
            {
                _nameLabel.text = player.Name;
            }
        }
    }
}
