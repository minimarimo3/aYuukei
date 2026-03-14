using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UniGLTF;
using UniVRM10;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Yuukei.Runtime
{
    internal enum MascotMotionPresentationMode
    {
        Automatic,
        ManualOverride,
        DragOverride,
    }

    /// <summary>
    /// マスコットキャラクターの表示・アニメーション・表情・小道具を管理するランタイムコントローラー。
    /// VRMキャラクターの読み込み、モーション再生、デスクトップ上の移動処理を担当する。
    /// </summary>
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

        private static readonly Dictionary<string, ExpressionKey> BuiltinExpressionAliases = new Dictionary<string, ExpressionKey>(StringComparer.Ordinal)
        {
            [AliasRegistry.Normalize("default")] = ExpressionKey.Neutral,
            [AliasRegistry.Normalize("normal")] = ExpressionKey.Neutral,
            [AliasRegistry.Normalize("neutral")] = ExpressionKey.Neutral,
            [AliasRegistry.Normalize("happy")] = ExpressionKey.Happy,
            [AliasRegistry.Normalize("smile")] = ExpressionKey.Happy,
            [AliasRegistry.Normalize("angry")] = ExpressionKey.Angry,
            [AliasRegistry.Normalize("sad")] = ExpressionKey.Sad,
            [AliasRegistry.Normalize("relaxed")] = ExpressionKey.Relaxed,
            [AliasRegistry.Normalize("surprised")] = ExpressionKey.Surprised,
            [AliasRegistry.Normalize("aa")] = ExpressionKey.Aa,
            [AliasRegistry.Normalize("ih")] = ExpressionKey.Ih,
            [AliasRegistry.Normalize("ou")] = ExpressionKey.Ou,
            [AliasRegistry.Normalize("ee")] = ExpressionKey.Ee,
            [AliasRegistry.Normalize("oh")] = ExpressionKey.Oh,
            [AliasRegistry.Normalize("blink")] = ExpressionKey.Blink,
            [AliasRegistry.Normalize("blinkleft")] = ExpressionKey.BlinkLeft,
            [AliasRegistry.Normalize("blinkright")] = ExpressionKey.BlinkRight,
            [AliasRegistry.Normalize("lookup")] = ExpressionKey.LookUp,
            [AliasRegistry.Normalize("lookdown")] = ExpressionKey.LookDown,
            [AliasRegistry.Normalize("lookleft")] = ExpressionKey.LookLeft,
            [AliasRegistry.Normalize("lookright")] = ExpressionKey.LookRight,
        };

        private static readonly string[] HoverMotionCandidates = { "hover", "float", "idle" };

        private sealed class LoadedMotion
        {
            public LoadedMotion(string name, RuntimeGltfInstance gltfInstance, Vrm10AnimationInstance animationInstance, ITimeControl timeControl, float durationSeconds)
            {
                Name = name;
                GltfInstance = gltfInstance;
                AnimationInstance = animationInstance;
                TimeControl = timeControl;
                DurationSeconds = durationSeconds;
            }

            public string Name { get; }
            public RuntimeGltfInstance GltfInstance { get; }
            public Vrm10AnimationInstance AnimationInstance { get; }
            public ITimeControl TimeControl { get; }
            public float DurationSeconds { get; }
        }

        private readonly struct ControlRigPoseSnapshot
        {
            public ControlRigPoseSnapshot(Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
            {
                LocalPosition = localPosition;
                LocalRotation = localRotation;
                LocalScale = localScale;
            }

            public Vector3 LocalPosition { get; }
            public Quaternion LocalRotation { get; }
            public Vector3 LocalScale { get; }
        }

        private readonly struct TransformLocalPositionSnapshot
        {
            public TransformLocalPositionSnapshot(Transform transform, Vector3 localPosition)
            {
                Transform = transform;
                LocalPosition = localPosition;
            }

            public Transform Transform { get; }
            public Vector3 LocalPosition { get; }
        }

        private Camera _worldCamera;
        private Transform _root;
        private Transform _visualRoot;
        private Transform _anchor;
        private Transform _motionRoot;
        private GameObject _placeholderRoot;
        private Renderer _placeholderBody;
        private BoxCollider _hitbox;
        private readonly Dictionary<string, GameObject> _props = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, LoadedMotion> _loadedMotions = new Dictionary<string, LoadedMotion>(StringComparer.Ordinal);
        private readonly Dictionary<string, ExpressionKey> _expressionKeys = new Dictionary<string, ExpressionKey>(StringComparer.Ordinal);
        private readonly Dictionary<HumanBodyBones, ControlRigPoseSnapshot> _controlRigRestPose = new Dictionary<HumanBodyBones, ControlRigPoseSnapshot>();
        private readonly List<TransformLocalPositionSnapshot> _dragTranslationSnapshots = new List<TransformLocalPositionSnapshot>();

        private Vrm10Instance _vrmInstance;
        private RectInt _virtualDesktopBounds;
        private IReadOnlyList<DesktopDisplayInfo> _displays = Array.Empty<DesktopDisplayInfo>();
        private HashSet<int> _allowedDisplayIndices = new HashSet<int>();
        private Vector2 _desktopPosition;
        private bool _hasDesktopPosition;
        private bool _isVisible = true;
        private string _currentMotion = string.Empty;
        private string _manualMotionOverride;
        private float _motionPhase;
        private LoadedMotion _activeMotion;
        private float _activeMotionTime;
        private ExpressionKey? _activeExpression;
        private GlideLocomotionSettings _floatingSettings = new GlideLocomotionSettings();
        private float _floatTime;
        private float _phase1;
        private float _phase2;
        private float _phase3;
        private float _phaseTilt;
        private AnimationClip _dragMotionClip;
        private float _dragMotionFadeInSeconds = 0.12f;
        private float _dragMotionFadeOutSeconds = 0.16f;
        private bool _isUserDragMotionRequested;
        private float _dragMotionBlendWeight;
        private float _dragMotionTime;
        private PlayableGraph _dragMotionGraph;
        private AnimationMixerPlayable _dragMotionMixer;
        private AnimationClipPlayable _dragMotionPlayable;
        private bool _hasDragMotionPlayable;

        internal MascotMotionPresentationMode DebugPresentationMode => ResolveMotionPresentationMode();
        internal bool DebugIsUserDragMotionRequested => _isUserDragMotionRequested;
        internal float DebugDragMotionBlendWeight => _dragMotionBlendWeight;
        internal Vector2 DebugDesktopPosition => _desktopPosition;
        internal Vector3 DebugVisualLocalPosition => _visualRoot != null ? _visualRoot.localPosition : Vector3.zero;
        internal Quaternion DebugVisualLocalRotation => _visualRoot != null ? _visualRoot.localRotation : Quaternion.identity;

        /// <summary>マスコットの初期化。ルートオブジェクト・アンカー・プレースホルダーを構築する。</summary>
        public void Initialize(Camera worldCamera)
        {
            Debug.Log("[MascotRuntime] 初期化開始");
            _worldCamera = worldCamera;

            _root = new GameObject("MascotRoot").transform;
            _root.SetParent(transform, false);

            _visualRoot = new GameObject("VisualRoot").transform;
            _visualRoot.SetParent(_root, false);

            _hitbox = _visualRoot.gameObject.AddComponent<BoxCollider>();
            _hitbox.isTrigger = true;

            _anchor = new GameObject("SpeechAnchor").transform;
            _anchor.SetParent(_visualRoot, false);
            _anchor.localPosition = new Vector3(0f, 2.1f, 0f);

            _motionRoot = new GameObject("MotionCacheRoot").transform;
            _motionRoot.SetParent(transform, false);

            CreatePlaceholder();
            CreateProps();
            ScaleCharacterToScreenFraction();
            UpdateHitboxForPlaceholder();
            _floatTime = 0f;
            _phase1 = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            _phase2 = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            _phase3 = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            _phaseTilt = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            Debug.Log("[MascotRuntime] 初期化完了");
        }

        public Vector3 SpeechAnchorWorldPosition => _anchor != null ? _anchor.position : transform.position;

        public string CharacterId => "default_mascot";

        public void ApplyGlideSettings(GlideLocomotionSettings settings)
        {
            _floatingSettings = settings?.Clone() ?? new GlideLocomotionSettings();
        }

        public void ConfigureDragMotion(DragMotionSettings settings)
        {
            var resolvedSettings = settings ?? new DragMotionSettings();
            _dragMotionClip = resolvedSettings.Clip;
            _dragMotionFadeInSeconds = Mathf.Max(0.01f, resolvedSettings.FadeInSeconds);
            _dragMotionFadeOutSeconds = Mathf.Max(0.01f, resolvedSettings.FadeOutSeconds);
            Debug.Log(_dragMotionClip == null
                ? "[MascotRuntime] Drag motion clip is not assigned."
                : $"[MascotRuntime] Drag motion configured: clip='{_dragMotionClip.name}', length={_dragMotionClip.length:F3}s");
            RebuildDragMotionPlayableGraph();
        }

        public void SetUserDragMotionActive(bool active)
        {
            if (_isUserDragMotionRequested == active)
            {
                return;
            }

            _isUserDragMotionRequested = active;
            if (active)
            {
                _dragMotionTime = 0f;
                if (_hasDragMotionPlayable)
                {
                    _dragMotionPlayable.SetTime(0d);
                    _dragMotionGraph.Evaluate(0f);
                }
            }
        }

        public void CancelUserDragMotionImmediately()
        {
            _isUserDragMotionRequested = false;
            _dragMotionBlendWeight = 0f;
            _dragMotionTime = 0f;
            ApplyDragMotionWeight(0f);
            if (_hasDragMotionPlayable)
            {
                _dragMotionPlayable.SetTime(0d);
                _dragMotionGraph.Evaluate(0f);
            }
        }

        public void SetDesktopContext(RectInt virtualDesktopBounds, IReadOnlyList<DesktopDisplayInfo> displays, IReadOnlyCollection<int> allowedDisplayIndices)
        {
            _virtualDesktopBounds = virtualDesktopBounds;
            _displays = displays ?? Array.Empty<DesktopDisplayInfo>();
            _allowedDisplayIndices = allowedDisplayIndices != null
                ? new HashSet<int>(allowedDisplayIndices)
                : new HashSet<int>();

            if (!_hasDesktopPosition)
            {
                SetDesktopPositionInternal(ResolveSafeDesktopPosition());
            }
            else if (!IsPointOnAllowedDisplay(_desktopPosition))
            {
                SetDesktopPositionInternal(ResolveSafeDesktopPosition());
            }

            UpdateDisplayVisibility();
        }

        /// <summary>VRMキャラクターを非同期で読み込む。失敗時はプレースホルダーを表示する。</summary>
        public async UniTask LoadCharacterAsync(string characterPath, System.Threading.CancellationToken cancellationToken)
        {
            Debug.Log($"[MascotRuntime] キャラクター読み込み開始: {characterPath}");
            StopActiveMotion();
            ClearExpressionCatalog();
            ResetDragMotionPlaybackState();

            if (_vrmInstance != null)
            {
                Destroy(_vrmInstance.gameObject);
                _vrmInstance = null;
            }

            _placeholderRoot.SetActive(true);
            UpdateHitboxForPlaceholder();

            if (string.IsNullOrWhiteSpace(characterPath) || !File.Exists(characterPath))
            {
                Debug.Log("[MascotRuntime] キャラクターパスが未指定または存在しない。プレースホルダーを使用");
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
                _vrmInstance.transform.SetParent(_visualRoot, false);
                _vrmInstance.transform.localPosition = Vector3.zero;
                _vrmInstance.transform.localRotation = Quaternion.identity;
                _vrmInstance.transform.localScale = Vector3.one;
                _vrmInstance.UpdateType = Vrm10Instance.UpdateTypes.None;
                _placeholderRoot.SetActive(false);

                ScaleCharacterToScreenFraction();
                RebuildDragMotionPlayableGraph();
                RebuildExpressionCatalog();
                UpdateHitboxForLoadedCharacter();
                RebindCurrentMotion();
                Debug.Log($"[MascotRuntime] キャラクター読み込み成功: {characterPath}");
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
                UpdateHitboxForPlaceholder();
                RebuildDragMotionPlayableGraph();
            }
        }

        /// <summary>モーション定義を一括で非同期読み込みする。</summary>
        public async UniTask LoadMotionsAsync(IReadOnlyDictionary<string, string> motionPaths, System.Threading.CancellationToken cancellationToken)
        {
            Debug.Log($"[MascotRuntime] モーション読み込み開始 (件数: {motionPaths?.Count ?? 0})");
            ClearLoadedMotions();
            _manualMotionOverride = null;

            if (motionPaths != null)
            {
                foreach (var pair in motionPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value) || !File.Exists(pair.Value))
                    {
                        if (!string.IsNullOrWhiteSpace(pair.Value))
                        {
                            Debug.LogWarning($"[MascotRuntime] Motion '{pair.Key}' is missing: {pair.Value}");
                        }

                        Debug.Log($"[MascotRuntime] モーション '{pair.Key}' をスキップ");
                        continue;
                    }

                    RuntimeGltfInstance gltfInstance = null;
                    try
                    {
                        using var data = new GlbFileParser(pair.Value).Parse();
                        var motionData = new VrmAnimationData(data);
                        using var loader = new VrmAnimationImporter(motionData);
                        gltfInstance = await loader.LoadAsync(new ImmediateCaller());
                        gltfInstance.transform.SetParent(_motionRoot, false);
                        gltfInstance.transform.localPosition = Vector3.zero;
                        gltfInstance.transform.localRotation = Quaternion.identity;
                        gltfInstance.transform.localScale = Vector3.one;
                        foreach (var renderer in gltfInstance.Renderers)
                        {
                            renderer.enabled = false;
                        }

                        var animationInstance = gltfInstance.GetComponent<Vrm10AnimationInstance>();
                        animationInstance.ShowBoxMan(false);
                        var duration = gltfInstance.AnimationClips.Count > 0
                            ? Mathf.Max(gltfInstance.AnimationClips[0].length, 0.01f)
                            : 0.01f;
                        _loadedMotions[AliasRegistry.Normalize(pair.Key)] = new LoadedMotion(
                            pair.Key,
                            gltfInstance,
                            animationInstance,
                            (ITimeControl)animationInstance,
                            duration);
                        Debug.Log($"[MascotRuntime] モーション '{pair.Key}' を読み込み完了 (長さ: {duration:F2}秒)");
                    }
                    catch (OperationCanceledException)
                    {
                        if (gltfInstance != null)
                        {
                            gltfInstance.Dispose();
                        }

                        throw;
                    }
                    catch (Exception exception)
                    {
                        if (gltfInstance != null)
                        {
                            gltfInstance.Dispose();
                        }

                        Debug.LogWarning($"[MascotRuntime] Failed to load motion '{pair.Key}' from '{pair.Value}'. Skipping. {exception.Message}");
                    }
                }
            }

            RefreshDesiredMotion(true);
            Debug.Log($"[MascotRuntime] モーション読み込み完了 (合計: {_loadedMotions.Count}件)");
        }

        /// <summary>表情を設定する。VRMモデルまたはプレースホルダーの色を変更する。</summary>
        public void SetExpression(string name)
        {
            if (_vrmInstance != null)
            {
                if (!TryResolveExpressionKey(name, out var expressionKey))
                {
                    Debug.LogWarning($"[MascotRuntime] Unknown expression '{name}'.");
                    return;
                }

                _activeExpression = expressionKey;
                ApplyExpressionOverride();
                Debug.Log($"[MascotRuntime] 表情を設定: {name}");
                return;
            }

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
            Debug.Log($"[MascotRuntime] プレースホルダー表情を設定: {name}");
        }

        /// <summary>指定したモーションを再生する。</summary>
        public void PlayMotion(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                Debug.LogWarning("[MascotRuntime] Motion name was empty.");
                return;
            }

            _manualMotionOverride = name.Trim();
            _motionPhase = 0f;
            RefreshDesiredMotion(true);
            Debug.Log($"[MascotRuntime] モーション再生: {_manualMotionOverride}");
        }

        public void ClearMotionOverride()
        {
            if (string.IsNullOrWhiteSpace(_manualMotionOverride))
            {
                return;
            }

            _manualMotionOverride = null;
            RefreshDesiredMotion(true);
        }

        /// <summary>小道具の表示・非表示を切り替える。</summary>
        public void SetPropVisible(string name, bool visible)
        {
            if (!_props.TryGetValue(name ?? string.Empty, out var prop))
            {
                Debug.LogWarning($"[MascotRuntime] Unknown prop '{name}'.");
                return;
            }

            prop.SetActive(visible);
            Debug.Log($"[MascotRuntime] 小道具 '{name}' を{(visible ? "表示" : "非表示")}に設定");
        }

        /// <summary>マスコット全体の表示・非表示を切り替える。</summary>
        public void SetVisible(bool visible)
        {
            if (!visible)
            {
                CancelUserDragMotionImmediately();
            }

            _isVisible = visible;
            UpdateDisplayVisibility();
            Debug.Log($"[MascotRuntime] 表示状態を変更: {(visible ? "表示" : "非表示")}");
        }

        public void Tick(float deltaTime, float busyScore)
        {
            if (_root == null)
            {
                return;
            }

            if (!_hasDesktopPosition)
            {
                SetDesktopPositionInternal(ResolveSafeDesktopPosition());
            }
            else if (!IsPointOnAllowedDisplay(_desktopPosition))
            {
                SetDesktopPositionInternal(ResolveSafeDesktopPosition());
            }

            RefreshDesiredMotion();
            ApplyPlaceholderMotion(deltaTime);
            ProcessLoadedCharacter(deltaTime);
        }

        private void LateUpdate()
        {
            ApplyFloatingPose(Time.deltaTime);
        }

        public bool HitTestScreenPoint(Vector2 screenPoint)
        {
            if (_root == null || !_isVisible || _worldCamera == null)
            {
                return false;
            }

            var ray = _worldCamera.ScreenPointToRay(screenPoint);
            return _hitbox != null && _hitbox.Raycast(ray, out _, 100f);
        }

        public void MoveByScreenDelta(Vector2 delta)
        {
            if (!_hasDesktopPosition)
            {
                SetDesktopPositionInternal(ResolveSafeDesktopPosition());
            }

            SetDesktopPositionInternal(_desktopPosition + delta);
        }

        private void CreatePlaceholder()
        {
            _placeholderRoot = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _placeholderRoot.name = "PlaceholderMascot";
            _placeholderRoot.transform.SetParent(_visualRoot, false);
            _placeholderRoot.transform.localScale = new Vector3(1.1f, 1.5f, 1f);
            _placeholderRoot.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            _placeholderBody = _placeholderRoot.GetComponent<Renderer>();
            _placeholderBody.material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                color = ExpressionPalette["default"],
            };

            var placeholderCollider = _placeholderRoot.GetComponent<Collider>();
            if (placeholderCollider != null)
            {
                placeholderCollider.enabled = false;
            }
        }

        private void CreateProps()
        {
            var halo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            halo.name = "halo";
            halo.transform.SetParent(_visualRoot, false);
            halo.transform.localScale = new Vector3(0.8f, 0.03f, 0.8f);
            halo.transform.localPosition = new Vector3(0f, 3.1f, 0f);
            halo.GetComponent<Renderer>().material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                color = new Color(0.96f, 0.87f, 0.42f),
            };
            halo.SetActive(false);

            var propCollider = halo.GetComponent<Collider>();
            if (propCollider != null)
            {
                propCollider.enabled = false;
            }

            _props["halo"] = halo;
        }

        private Vector2 ResolveSafeDesktopPosition()
        {
            RectInt selected = default;
            var hasSelected = false;
            var bestIndex = int.MaxValue;
            foreach (var display in _displays)
            {
                if (_allowedDisplayIndices.Count > 0 && !_allowedDisplayIndices.Contains(display.Index))
                {
                    continue;
                }

                if (!hasSelected || display.Index < bestIndex)
                {
                    selected = display.Bounds;
                    bestIndex = display.Index;
                    hasSelected = true;
                }
            }

            if (!hasSelected)
            {
                selected = _virtualDesktopBounds.width <= 0 || _virtualDesktopBounds.height <= 0
                    ? new RectInt(0, 0, Screen.width, Screen.height)
                    : _virtualDesktopBounds;
            }

            var paddingX = Mathf.Min(160, Mathf.Max(40, selected.width / 6));
            var paddingY = Mathf.Min(140, Mathf.Max(40, selected.height / 6));
            float minX = selected.xMin + paddingX;
            float maxX = selected.xMax - paddingX;
            float minY = selected.yMin + paddingY;
            float maxY = selected.yMax - paddingY;

            var safeX = maxX >= minX ? maxX : selected.xMin + (selected.width * 0.5f);
            var safeY = maxY >= minY ? maxY : selected.yMin + (selected.height * 0.5f);
            return new Vector2(safeX, safeY);
        }

        private void SetDesktopPositionInternal(Vector2 position)
        {
            _desktopPosition = position;
            _hasDesktopPosition = true;
            ApplyDesktopPosition();
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
            UpdateDisplayVisibility();
        }

        private void ApplyFloatingPose(float deltaTime)
        {
            if (_visualRoot == null)
            {
                return;
            }

            if (!_hasDesktopPosition)
            {
                _visualRoot.localPosition = Vector3.zero;
                _visualRoot.localRotation = Quaternion.identity;
                return;
            }

            _floatTime += Mathf.Max(0f, deltaTime);

            var y = _floatingSettings.FloatAmplitudeY * (
                0.55f * Mathf.Sin(2f * Mathf.PI * _floatingSettings.FloatFrequency1 * _floatTime + _phase1) +
                0.30f * Mathf.Sin(2f * Mathf.PI * _floatingSettings.FloatFrequency2 * _floatTime + _phase2) +
                0.15f * Mathf.Sin(2f * Mathf.PI * _floatingSettings.FloatFrequency3 * _floatTime + _phase3));

            var x = _floatingSettings.FloatAmplitudeX *
                Mathf.Sin(2f * Mathf.PI * _floatingSettings.FloatFrequency3 * _floatTime + _phase1 + 1.2f);

            var tiltZ = _floatingSettings.TiltAmplitudeDeg *
                Mathf.Sin(2f * Mathf.PI * _floatingSettings.TiltFrequency * _floatTime + _phaseTilt);

            _visualRoot.localPosition = new Vector3(x, y, 0f);
            _visualRoot.localRotation = Quaternion.Euler(0f, 0f, tiltZ);
        }

        private void ApplyPlaceholderMotion(float deltaTime)
        {
            if (_placeholderRoot == null)
            {
                return;
            }

            _motionPhase += deltaTime;
            var localPosition = new Vector3(0f, 1.5f, 0f);
            var localRotation = Quaternion.identity;

            switch ((_manualMotionOverride ?? string.Empty).ToLowerInvariant())
            {
                case "wave":
                case "greeting":
                    localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(_motionPhase * 6f) * 10f);
                    break;
                case "bounce":
                case "jump":
                    localPosition.y += Mathf.Abs(Mathf.Sin(_motionPhase * 4f)) * 0.35f;
                    break;
            }

            _placeholderRoot.transform.localPosition = localPosition;
            _placeholderRoot.transform.localRotation = localRotation;
        }

        private void ProcessLoadedCharacter(float deltaTime)
        {
            if (_vrmInstance == null)
            {
                return;
            }

            AdvanceActiveMotion(deltaTime);
            UpdateDragMotionBlend(deltaTime);

            var runtime = _vrmInstance.Runtime;
            if (ShouldDriveDragMotion())
            {
                runtime.VrmAnimation = null;
                var controlRig = runtime.ControlRig;
                if (!ApplyBaseMotionPoseToControlRig())
                {
                    ApplyCachedControlRigPose();
                }

                // Keep the drag pose's rotations while preventing translation curves from shifting the pickup anchor.
                CaptureCurrentDragTranslationState(controlRig);
                EvaluateDragMotionPlayable(deltaTime);
                RestoreCapturedDragTranslationState();
                ApplyExpressionOverride(useActiveMotionSource: false);
                runtime.Process();
                return;
            }

            if (_activeMotion != null)
            {
                ApplyExpressionOverride();
                runtime.VrmAnimation = _activeMotion.AnimationInstance;
            }
            else
            {
                runtime.VrmAnimation = null;
                ApplyExpressionOverride(useActiveMotionSource: false);
            }

            runtime.Process();
        }

        private void RebuildExpressionCatalog()
        {
            _expressionKeys.Clear();
            if (_vrmInstance?.Vrm?.Expression == null)
            {
                return;
            }

            foreach (var (preset, clip) in _vrmInstance.Vrm.Expression.Clips)
            {
                var expressionKey = new ExpressionKey(preset, clip.name);
                RegisterExpressionKey(expressionKey.Name, expressionKey);
            }

            foreach (var pair in BuiltinExpressionAliases)
            {
                if (!_expressionKeys.ContainsKey(pair.Key))
                {
                    _expressionKeys[pair.Key] = pair.Value;
                }
            }
        }

        private void RegisterExpressionKey(string name, ExpressionKey expressionKey)
        {
            var normalized = AliasRegistry.Normalize(name);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            _expressionKeys[normalized] = expressionKey;
        }

        private bool TryResolveExpressionKey(string rawName, out ExpressionKey expressionKey)
        {
            expressionKey = default;
            var normalized = AliasRegistry.Normalize(rawName);
            return !string.IsNullOrWhiteSpace(normalized) && _expressionKeys.TryGetValue(normalized, out expressionKey);
        }

        private void ClearExpressionCatalog()
        {
            _activeExpression = null;
            _expressionKeys.Clear();
        }

        private void ApplyExpressionOverride(bool useActiveMotionSource = true)
        {
            if (useActiveMotionSource && _activeMotion != null)
            {
                if (!_activeExpression.HasValue)
                {
                    return;
                }

                foreach (var setter in _activeMotion.AnimationInstance.ExpressionSetterMap.Values)
                {
                    setter(0f);
                }

                if (_activeMotion.AnimationInstance.ExpressionSetterMap.TryGetValue(_activeExpression.Value, out var motionSetter))
                {
                    motionSetter(1f);
                }

                return;
            }

            if (_vrmInstance == null || !_activeExpression.HasValue)
            {
                return;
            }

            foreach (var expressionKey in _expressionKeys.Values)
            {
                _vrmInstance.Runtime.Expression.SetWeight(expressionKey, 0f);
            }

            _vrmInstance.Runtime.Expression.SetWeight(_activeExpression.Value, 1f);
        }

        private void AdvanceActiveMotion(float deltaTime)
        {
            if (_activeMotion == null)
            {
                return;
            }

            _activeMotionTime = Mathf.Repeat(_activeMotionTime + Mathf.Max(0f, deltaTime), _activeMotion.DurationSeconds);
            _activeMotion.TimeControl.SetTime(_activeMotionTime);
        }

        private void UpdateDragMotionBlend(float deltaTime)
        {
            if (!CanUseDragMotion())
            {
                _dragMotionBlendWeight = 0f;
                ApplyDragMotionWeight(0f);
                return;
            }

            var targetWeight = _isUserDragMotionRequested ? 1f : 0f;
            var fadeSeconds = targetWeight > _dragMotionBlendWeight ? _dragMotionFadeInSeconds : _dragMotionFadeOutSeconds;
            if (fadeSeconds <= 0f)
            {
                _dragMotionBlendWeight = targetWeight;
            }
            else
            {
                _dragMotionBlendWeight = Mathf.MoveTowards(
                    _dragMotionBlendWeight,
                    targetWeight,
                    Mathf.Max(0f, deltaTime) / fadeSeconds);
            }

            ApplyDragMotionWeight(_dragMotionBlendWeight);
        }

        private bool ShouldDriveDragMotion()
        {
            return _dragMotionBlendWeight > 0.001f && CanUseDragMotion();
        }

        private bool CanUseDragMotion()
        {
            return _hasDragMotionPlayable
                && _dragMotionClip != null
                && _vrmInstance != null
                && _vrmInstance.Runtime?.ControlRig?.ControlRigAnimator != null;
        }

        private void ApplyDragMotionWeight(float weight)
        {
            if (_hasDragMotionPlayable)
            {
                _dragMotionMixer.SetInputWeight(0, Mathf.Clamp01(weight));
            }
        }

        private bool ApplyBaseMotionPoseToControlRig()
        {
            var controlRig = _vrmInstance?.Runtime?.ControlRig;
            if (_activeMotion == null || controlRig == null)
            {
                return false;
            }

            Vrm10Retarget.Retarget(_activeMotion.AnimationInstance.ControlRig, (controlRig, controlRig));
            return true;
        }

        private void ApplyCachedControlRigPose()
        {
            var controlRig = _vrmInstance?.Runtime?.ControlRig;
            if (controlRig == null || _controlRigRestPose.Count == 0)
            {
                return;
            }

            foreach (var (boneType, controlBone) in controlRig.Bones)
            {
                if (!_controlRigRestPose.TryGetValue(boneType, out var snapshot))
                {
                    continue;
                }

                controlBone.ControlBone.localPosition = snapshot.LocalPosition;
                controlBone.ControlBone.localRotation = snapshot.LocalRotation;
                controlBone.ControlBone.localScale = snapshot.LocalScale;
            }
        }

        private void CaptureCurrentDragTranslationState(Vrm10RuntimeControlRig controlRig)
        {
            _dragTranslationSnapshots.Clear();
            if (controlRig == null)
            {
                return;
            }

            CaptureDragTranslationSnapshot(controlRig.ControlRigAnimator != null ? controlRig.ControlRigAnimator.transform : null);
            foreach (var (_, controlBone) in controlRig.Bones)
            {
                CaptureDragTranslationSnapshot(controlBone.ControlBone);
            }
        }

        private void CaptureDragTranslationSnapshot(Transform transform)
        {
            if (transform == null)
            {
                return;
            }

            for (var i = 0; i < _dragTranslationSnapshots.Count; i++)
            {
                if (_dragTranslationSnapshots[i].Transform == transform)
                {
                    return;
                }
            }

            _dragTranslationSnapshots.Add(new TransformLocalPositionSnapshot(transform, transform.localPosition));
        }

        private void RestoreCapturedDragTranslationState()
        {
            for (var i = 0; i < _dragTranslationSnapshots.Count; i++)
            {
                var snapshot = _dragTranslationSnapshots[i];
                if (snapshot.Transform == null)
                {
                    continue;
                }

                snapshot.Transform.localPosition = snapshot.LocalPosition;
            }

            _dragTranslationSnapshots.Clear();
        }

        private void EvaluateDragMotionPlayable(float deltaTime)
        {
            if (!CanUseDragMotion())
            {
                return;
            }

            var clipDuration = Mathf.Max(_dragMotionClip.length, 0.01f);
            _dragMotionTime = Mathf.Repeat(_dragMotionTime + Mathf.Max(0f, deltaTime), clipDuration);
            _dragMotionPlayable.SetTime(_dragMotionTime);
            _dragMotionGraph.Evaluate(0f);
        }

        private MascotMotionPresentationMode ResolveMotionPresentationMode()
        {
            if (_isUserDragMotionRequested && _dragMotionClip != null)
            {
                return MascotMotionPresentationMode.DragOverride;
            }

            return !string.IsNullOrWhiteSpace(_manualMotionOverride)
                ? MascotMotionPresentationMode.ManualOverride
                : MascotMotionPresentationMode.Automatic;
        }

        private void ResetDragMotionPlaybackState()
        {
            CancelUserDragMotionImmediately();
            DestroyDragMotionPlayableGraph();
            _controlRigRestPose.Clear();
            _dragTranslationSnapshots.Clear();
        }

        private void RebuildDragMotionPlayableGraph()
        {
            DestroyDragMotionPlayableGraph();
            _controlRigRestPose.Clear();
            _dragTranslationSnapshots.Clear();

            if (_vrmInstance == null)
            {
                Debug.Log("[MascotRuntime] Drag motion graph rebuild skipped: VRM instance is not loaded yet.");
                return;
            }

            var controlRig = _vrmInstance.Runtime?.ControlRig;
            if (controlRig == null)
            {
                Debug.LogWarning("[MascotRuntime] Drag motion graph rebuild skipped: ControlRig is unavailable.");
                return;
            }

            CaptureControlRigRestPose(controlRig);
            if (_dragMotionClip == null || _dragMotionClip.length <= 0f)
            {
                Debug.LogWarning("[MascotRuntime] Drag motion graph rebuild skipped: drag clip is missing or has zero length.");
                return;
            }

            var animator = controlRig.ControlRigAnimator;
            if (animator == null)
            {
                Debug.LogWarning("[MascotRuntime] Drag motion graph rebuild skipped: ControlRig animator is unavailable.");
                return;
            }

            _dragMotionGraph = PlayableGraph.Create("MascotDragMotion");
            _dragMotionGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _dragMotionMixer = AnimationMixerPlayable.Create(_dragMotionGraph, 1);
            _dragMotionPlayable = AnimationClipPlayable.Create(_dragMotionGraph, _dragMotionClip);
            _dragMotionPlayable.SetSpeed(0d);
            _dragMotionPlayable.SetApplyFootIK(false);
            _dragMotionPlayable.SetApplyPlayableIK(false);

            _dragMotionGraph.Connect(_dragMotionPlayable, 0, _dragMotionMixer, 0);
            _dragMotionMixer.SetInputWeight(0, 0f);
            var output = AnimationPlayableOutput.Create(_dragMotionGraph, "MascotDragMotion", animator);
            output.SetSourcePlayable(_dragMotionMixer);
            _dragMotionGraph.Play();
            _hasDragMotionPlayable = true;
            Debug.Log($"[MascotRuntime] Drag motion playable ready: clip='{_dragMotionClip.name}', animator='{animator.name}'.");
        }

        private void CaptureControlRigRestPose(Vrm10RuntimeControlRig controlRig)
        {
            foreach (var (boneType, controlBone) in controlRig.Bones)
            {
                _controlRigRestPose[boneType] = new ControlRigPoseSnapshot(
                    controlBone.ControlBone.localPosition,
                    controlBone.ControlBone.localRotation,
                    controlBone.ControlBone.localScale);
            }
        }

        private void DestroyDragMotionPlayableGraph()
        {
            if (_dragMotionGraph.IsValid())
            {
                _dragMotionGraph.Destroy();
            }

            _dragMotionGraph = default;
            _dragMotionMixer = default;
            _dragMotionPlayable = default;
            _hasDragMotionPlayable = false;
        }

        private void RebindCurrentMotion()
        {
            if (_vrmInstance == null)
            {
                return;
            }

            if (_loadedMotions.TryGetValue(AliasRegistry.Normalize(_currentMotion), out var motion))
            {
                if (_activeMotion != null && ReferenceEquals(_activeMotion, motion))
                {
                    _activeMotionTime = 0f;
                    return;
                }

                StopActiveMotion();
                _activeMotion = motion;
                _activeMotionTime = 0f;
                _activeMotion.TimeControl.OnControlTimeStart();
                _activeMotion.AnimationInstance.ShowBoxMan(false);
                return;
            }

            StopActiveMotion();
        }

        private void StopActiveMotion()
        {
            if (_activeMotion != null)
            {
                _activeMotion.TimeControl.OnControlTimeStop();
                _activeMotion = null;
                _activeMotionTime = 0f;
            }

            if (_vrmInstance != null)
            {
                _vrmInstance.Runtime.VrmAnimation = null;
            }
        }

        private void ClearLoadedMotions()
        {
            StopActiveMotion();
            foreach (var motion in _loadedMotions.Values)
            {
                motion.GltfInstance.Dispose();
            }

            _loadedMotions.Clear();
        }

        private void UpdateHitboxForPlaceholder()
        {
            if (_hitbox == null)
            {
                return;
            }

            _hitbox.center = new Vector3(0f, 1.6f, 0f);
            _hitbox.size = new Vector3(1.6f, 3.4f, 1.4f);
        }

        private void ScaleCharacterToScreenFraction()
        {
            if (_worldCamera == null || !_worldCamera.orthographic)
            {
                return;
            }

            var targetHeight = _worldCamera.orthographicSize * 2f / 3f;

            Renderer[] renderers = null;
            if (_vrmInstance != null)
            {
                renderers = _vrmInstance.GetComponentsInChildren<Renderer>();
            }

            if (renderers != null && renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                var currentHeight = bounds.size.y;
                if (currentHeight > 0.01f)
                {
                    var scale = targetHeight / currentHeight;
                    _vrmInstance.transform.localScale = Vector3.one * scale;

                    _anchor.localPosition = new Vector3(0f, targetHeight + 0.1f, 0f);
                    UpdateHaloPosition(targetHeight);
                }
            }
            else
            {
                // プレースホルダー用: カプセルの高さは2（デフォルト）* scaleY
                var placeholderDefaultHeight = 2f * 1.5f; // localScale.y = 1.5
                var scale = targetHeight / placeholderDefaultHeight;
                _placeholderRoot.transform.localScale = new Vector3(1.1f * scale, 1.5f * scale, 1f * scale);
                _placeholderRoot.transform.localPosition = new Vector3(0f, 1.5f * scale, 0f);

                _anchor.localPosition = new Vector3(0f, targetHeight + 0.1f, 0f);
                UpdateHaloPosition(targetHeight);
            }
        }

        private void UpdateHaloPosition(float characterHeight)
        {
            if (_props.TryGetValue("halo", out var halo) && halo != null)
            {
                halo.transform.localPosition = new Vector3(0f, characterHeight + 0.1f, 0f);
            }
        }

        private void UpdateHitboxForLoadedCharacter()
        {
            if (_hitbox == null || _vrmInstance == null || _visualRoot == null)
            {
                return;
            }

            var renderers = _vrmInstance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                UpdateHitboxForPlaceholder();
                return;
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            var localCenter = _visualRoot.InverseTransformPoint(bounds.center);
            _hitbox.center = localCenter;
            _hitbox.size = new Vector3(
                Mathf.Max(bounds.size.x, 0.6f),
                Mathf.Max(bounds.size.y, 1.4f),
                Mathf.Max(bounds.size.z, 0.4f));
        }

        private void RefreshDesiredMotion(bool forceRebind = false)
        {
            var desiredMotion = !string.IsNullOrWhiteSpace(_manualMotionOverride)
                ? _manualMotionOverride
                : ResolveAutomaticMotionName();

            desiredMotion ??= string.Empty;
            if (!forceRebind && string.Equals(_currentMotion, desiredMotion, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _currentMotion = desiredMotion;
            _motionPhase = 0f;
            RebindCurrentMotion();
        }

        private string ResolveAutomaticMotionName()
        {
            foreach (var candidate in HoverMotionCandidates)
            {
                if (_loadedMotions.ContainsKey(AliasRegistry.Normalize(candidate)))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private bool IsPointOnAllowedDisplay(Vector2 point)
        {
            if (_allowedDisplayIndices.Count == 0)
            {
                return true;
            }

            foreach (var display in _displays)
            {
                if (display.Bounds.Contains(Vector2Int.RoundToInt(point)))
                {
                    return _allowedDisplayIndices.Contains(display.Index);
                }
            }

            return true;
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

        private void OnDestroy()
        {
            ResetDragMotionPlaybackState();
            ClearLoadedMotions();
        }
    }
}
