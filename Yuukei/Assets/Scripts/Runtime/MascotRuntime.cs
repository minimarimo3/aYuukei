using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UniVRM10;
using UnityEngine;

namespace Yuukei.Runtime
{
    public sealed class MascotRuntime : MonoBehaviour
    {
        private static readonly IReadOnlyDictionary<string, Color> ExpressionPalette = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            ["default"] = new Color(0.95f, 0.92f, 0.86f),
            ["normal"] = new Color(0.95f, 0.92f, 0.86f),
            ["happy"] = new Color(1f, 0.83f, 0.52f),
            ["smile"] = new Color(1f, 0.83f, 0.52f),
            ["sad"] = new Color(0.56f, 0.69f, 0.92f),
            ["angry"] = new Color(0.94f, 0.43f, 0.38f),
            ["surprised"] = new Color(0.88f, 0.76f, 0.98f),
        };

        private Camera _worldCamera;
        private Transform _root;
        private Transform _anchor;
        private GameObject _placeholderRoot;
        private Renderer _placeholderBody;
        private readonly Dictionary<string, GameObject> _props = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private Vrm10Instance _vrmInstance;
        private RectInt _virtualDesktopBounds;
        private IReadOnlyList<DesktopDisplayInfo> _displays = Array.Empty<DesktopDisplayInfo>();
        private HashSet<int> _allowedDisplayIndices = new HashSet<int>();
        private Vector2 _desktopPosition;
        private Vector2 _desktopTarget;
        private bool _isVisible = true;
        private string _currentMotion = "idle";
        private float _motionPhase;

        public void Initialize(Camera worldCamera)
        {
            _worldCamera = worldCamera;

            _root = new GameObject("MascotRoot").transform;
            _root.SetParent(transform, false);

            _anchor = new GameObject("SpeechAnchor").transform;
            _anchor.SetParent(_root, false);
            _anchor.localPosition = new Vector3(0f, 2.1f, 0f);

            CreatePlaceholder();
            CreateProps();
        }

        public Vector3 SpeechAnchorWorldPosition => _anchor != null ? _anchor.position : transform.position;

        public string CharacterId => "default_mascot";

        public void SetDesktopContext(RectInt virtualDesktopBounds, IReadOnlyList<DesktopDisplayInfo> displays, IReadOnlyCollection<int> allowedDisplayIndices)
        {
            _virtualDesktopBounds = virtualDesktopBounds;
            _displays = displays ?? Array.Empty<DesktopDisplayInfo>();
            _allowedDisplayIndices = allowedDisplayIndices != null
                ? new HashSet<int>(allowedDisplayIndices)
                : new HashSet<int>();

            if (_desktopTarget == Vector2.zero)
            {
                ChooseNextTarget();
            }

            UpdateDisplayVisibility();
        }

        public async UniTask LoadCharacterAsync(string characterPath, CancellationToken cancellationToken)
        {
            if (_vrmInstance != null)
            {
                Destroy(_vrmInstance.gameObject);
                _vrmInstance = null;
            }

            _placeholderRoot.SetActive(true);

            if (string.IsNullOrWhiteSpace(characterPath) || !File.Exists(characterPath))
            {
                return;
            }

            try
            {
                var instance = await Vrm10.LoadPathAsync(characterPath, true, ct: cancellationToken);
                if (instance == null)
                {
                    return;
                }

                _vrmInstance = instance;
                _vrmInstance.transform.SetParent(_root, false);
                _vrmInstance.transform.localPosition = Vector3.zero;
                _vrmInstance.transform.localRotation = Quaternion.identity;
                _vrmInstance.transform.localScale = Vector3.one;
                _placeholderRoot.SetActive(false);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                Debug.LogWarning($"[MascotRuntime] Failed to load character '{characterPath}'. Using placeholder mascot instead. {exception.Message}");
                if (_vrmInstance != null)
                {
                    Destroy(_vrmInstance.gameObject);
                    _vrmInstance = null;
                }

                _placeholderRoot.SetActive(true);
            }
        }

        public void SetExpression(string name)
        {
            if (_placeholderBody == null)
            {
                return;
            }

            if (!ExpressionPalette.TryGetValue(name ?? string.Empty, out var color))
            {
                Debug.LogWarning($"[MascotRuntime] Unknown expression '{name}'.");
                return;
            }

            _placeholderBody.material.color = color;
        }

        public void PlayMotion(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                Debug.LogWarning("[MascotRuntime] Motion name was empty.");
                return;
            }

            _currentMotion = name;
            _motionPhase = 0f;
        }

        public void SetPropVisible(string name, bool visible)
        {
            if (!_props.TryGetValue(name ?? string.Empty, out var prop))
            {
                Debug.LogWarning($"[MascotRuntime] Unknown prop '{name}'.");
                return;
            }

            prop.SetActive(visible);
        }

        public void SetVisible(bool visible)
        {
            _isVisible = visible;
            UpdateDisplayVisibility();
        }

        public void Tick(float deltaTime, float busyScore)
        {
            if (_root == null)
            {
                return;
            }

            var dampedBusy = Mathf.Clamp01(busyScore);
            var moveSpeed = Mathf.Lerp(190f, 45f, dampedBusy);
            var arriveDistance = Mathf.Lerp(56f, 22f, dampedBusy);
            var direction = _desktopTarget - _desktopPosition;
            if (direction.magnitude <= arriveDistance)
            {
                ChooseNextTarget();
                direction = _desktopTarget - _desktopPosition;
            }

            var next = _desktopPosition + direction.normalized * moveSpeed * deltaTime;
            if (direction.sqrMagnitude < (next - _desktopPosition).sqrMagnitude)
            {
                next = _desktopTarget;
            }

            _desktopPosition = next;
            ApplyDesktopPosition();
            AnimateMotion(deltaTime);
        }

        public bool HitTestScreenPoint(Vector2 screenPoint)
        {
            if (_root == null || !_isVisible || _worldCamera == null)
            {
                return false;
            }

            var ray = _worldCamera.ScreenPointToRay(screenPoint);
            return Physics.Raycast(ray, out _, 100f);
        }

        public void MoveByScreenDelta(Vector2 delta)
        {
            _desktopPosition += delta;
            _desktopTarget = _desktopPosition;
            ApplyDesktopPosition();
        }

        private void CreatePlaceholder()
        {
            _placeholderRoot = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _placeholderRoot.name = "PlaceholderMascot";
            _placeholderRoot.transform.SetParent(_root, false);
            _placeholderRoot.transform.localScale = new Vector3(1.1f, 1.5f, 1f);
            _placeholderRoot.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            _placeholderBody = _placeholderRoot.GetComponent<Renderer>();
            _placeholderBody.material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                color = ExpressionPalette["default"],
            };
        }

        private void CreateProps()
        {
            var halo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            halo.name = "halo";
            halo.transform.SetParent(_root, false);
            halo.transform.localScale = new Vector3(0.8f, 0.03f, 0.8f);
            halo.transform.localPosition = new Vector3(0f, 3.1f, 0f);
            halo.GetComponent<Renderer>().material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                color = new Color(0.96f, 0.87f, 0.42f),
            };
            halo.SetActive(false);
            _props["halo"] = halo;
        }

        private void ChooseNextTarget()
        {
            var candidates = new List<RectInt>();
            foreach (var display in _displays)
            {
                if (_allowedDisplayIndices.Count == 0 || _allowedDisplayIndices.Contains(display.Index))
                {
                    candidates.Add(display.Bounds);
                }
            }

            if (candidates.Count == 0)
            {
                candidates.Add(_virtualDesktopBounds.width <= 0 || _virtualDesktopBounds.height <= 0
                    ? new RectInt(0, 0, Screen.width, Screen.height)
                    : _virtualDesktopBounds);
            }

            var selected = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            var paddingX = Mathf.Min(160, Mathf.Max(40, selected.width / 6));
            var paddingY = Mathf.Min(140, Mathf.Max(40, selected.height / 6));
            _desktopTarget = new Vector2(
                UnityEngine.Random.Range(selected.xMin + paddingX, selected.xMax - paddingX),
                UnityEngine.Random.Range(selected.yMin + paddingY, selected.yMax - paddingY));

            if (_desktopPosition == Vector2.zero)
            {
                _desktopPosition = _desktopTarget;
                ApplyDesktopPosition();
            }
        }

        private void ApplyDesktopPosition()
        {
            if (_worldCamera == null)
            {
                return;
            }

            var relativeX = _desktopPosition.x - _virtualDesktopBounds.xMin;
            var relativeY = _desktopPosition.y - _virtualDesktopBounds.yMin;
            var screenPoint = new Vector3(relativeX, Screen.height - relativeY, Mathf.Abs(_worldCamera.transform.position.z));
            var world = _worldCamera.ScreenToWorldPoint(screenPoint);
            world.z = 0f;
            _root.position = world;
        }

        private void AnimateMotion(float deltaTime)
        {
            _motionPhase += deltaTime;
            var localPosition = new Vector3(0f, 1.5f, 0f);
            var localRotation = Quaternion.identity;

            switch ((_currentMotion ?? string.Empty).ToLowerInvariant())
            {
                case "wave":
                case "greeting":
                    localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(_motionPhase * 6f) * 10f);
                    break;
                case "bounce":
                case "jump":
                    localPosition.y += Mathf.Abs(Mathf.Sin(_motionPhase * 4f)) * 0.35f;
                    break;
                case "float":
                case "idle":
                    localPosition.y += Mathf.Sin(_motionPhase * 2.3f) * 0.12f;
                    break;
                default:
                    Debug.LogWarning($"[MascotRuntime] Unknown motion '{_currentMotion}'.");
                    _currentMotion = "idle";
                    break;
            }

            if (_placeholderRoot != null)
            {
                _placeholderRoot.transform.localPosition = localPosition;
                _placeholderRoot.transform.localRotation = localRotation;
            }
        }

        private void UpdateDisplayVisibility()
        {
            if (_root == null)
            {
                return;
            }

            var displayIndex = -1;
            foreach (var display in _displays)
            {
                if (display.Bounds.Contains(Vector2Int.RoundToInt(_desktopPosition)))
                {
                    displayIndex = display.Index;
                    break;
                }
            }

            var shouldRender = _isVisible && (displayIndex < 0 || _allowedDisplayIndices.Count == 0 || _allowedDisplayIndices.Contains(displayIndex));
            _root.gameObject.SetActive(shouldRender);
        }
    }
}
