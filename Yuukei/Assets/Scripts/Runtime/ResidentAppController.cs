using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kirurobo;
using UnityEngine;
using UnityEngine.UI;

namespace Yuukei.Runtime
{
    public sealed class ResidentAppController : MonoBehaviour
    {
        private Canvas _runtimeCanvas;
        private Camera _mainCamera;
        private UniWindowController _windowController;
        private IDesktopPlatformAdapter _desktopAdapter;
        private PersistenceStore _persistenceStore;
        private PackageManager _packageManager;
        private TutorialBootstrap _tutorialBootstrap;
        private PluginLoader _pluginLoader;
        private AliasRegistry _aliasRegistry;
        private YuukeiVariableStore _variableStore;
        private SpeechBubbleController _speechBubbleController;
        private ChoiceOverlayController _choiceOverlayController;
        private SettingsWindow _settingsWindow;
        private MascotRuntime _mascotRuntime;
        private InputContextMonitor _inputContextMonitor;
        private DaihonBridge _daihonBridge;
        private CancellationTokenSource _lifetime;
        private bool _isSettingsVisible;
        private bool _isInitialized;
        private bool _apiKeyConfigured;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            _lifetime = new CancellationTokenSource();
            EnsureSceneReferences();
            BuildRuntimeObjects();
        }

        private void Start()
        {
            InitializeAsync(_lifetime.Token).Forget();
        }

        private void Update()
        {
            if (!_isInitialized)
            {
                return;
            }

            _desktopAdapter.Tick();
            _inputContextMonitor.Tick();
            _mascotRuntime.SetDesktopContext(_desktopAdapter.GetVirtualDesktopBounds(), _desktopAdapter.GetDisplays(), _inputContextMonitor.RecalculateAllowedDisplays());
            _mascotRuntime.Tick(Time.deltaTime, _inputContextMonitor.BusyScore);
        }

        private async UniTask InitializeAsync(CancellationToken cancellationToken)
        {
            _desktopAdapter = new WindowsDesktopAdapter();
            _desktopAdapter.Initialize();
            _desktopAdapter.ShortcutTriggered += HandleShortcutTriggered;
            _desktopAdapter.TrayCommandRequested += HandleTrayCommandRequested;

            _persistenceStore = new PersistenceStore();
            var isFirstLaunch = !File.Exists(_persistenceStore.SaveFilePath);
            await _persistenceStore.LoadAsync(cancellationToken);

            _packageManager = new PackageManager(_persistenceStore);
            _tutorialBootstrap = new TutorialBootstrap(_persistenceStore, _packageManager);
            _pluginLoader = new PluginLoader();
            _pluginLoader.StateChanged += RefreshSettingsWindow;
            _aliasRegistry = new AliasRegistry();
            _variableStore = new YuukeiVariableStore(_persistenceStore);

            _daihonBridge = new DaihonBridge(_aliasRegistry, _variableStore, _speechBubbleController, _choiceOverlayController, _mascotRuntime);
            _inputContextMonitor.Initialize(_desktopAdapter, _windowController, _mascotRuntime);
            _inputContextMonitor.EventRaised += OnRuntimeEventRaised;
            _desktopAdapter.ApplyShortcuts(_persistenceStore.Data.AppState.ShortcutConfig);
            _desktopAdapter.UpdateShellState(BuildShellState());

            _settingsWindow.Initialize(
                CloseSettingsAsync,
                SwitchPackageAsync,
                DeletePackageAsync,
                ImportPackageAsync,
                SetTemporarilyDisabledAsync,
                SetTemporarilyHiddenAsync,
                SaveShortcutConfigAsync,
                SaveSecretAsync,
                DeleteSecretAsync,
                ApproveDllsAsync,
                ClearDllApprovalsAsync);

            await _tutorialBootstrap.EnsureFirstLaunchPackageStateAsync(cancellationToken);
            await _packageManager.InitializeAsync(cancellationToken);
            _packageManager.ActivePackageChanged += _ => RefreshSettingsWindow();
            _packageManager.InstalledPackagesChanged += _ => RefreshSettingsWindow();

            await ApplyCurrentPackageAsync(cancellationToken);
            _apiKeyConfigured = _desktopAdapter.TryLoadSecret("llm.api_key", out _);
            RefreshSettingsWindow();

            await ApplyAppStateAsync();

            if (isFirstLaunch)
            {
                await ShowSplashAsync(cancellationToken);
            }

            _pluginLoader.ActivateApprovedPlugins();
            _isInitialized = true;
            await RaiseAppStartedAsync(isFirstLaunch, cancellationToken);
        }

        private void OnApplicationQuit()
        {
            SaveStateOnExit();
        }

        private void OnDestroy()
        {
            ResetWindowController();
            _desktopAdapter?.Shutdown();
            _lifetime?.Cancel();
            _lifetime?.Dispose();
        }

        private void ResetWindowController()
        {
            if (_windowController == null)
            {
                return;
            }

            _windowController.isTopmost = false;
            _windowController.isTransparent = false;
            _windowController.isHitTestEnabled = false;
            _windowController.allowDropFiles = false;
        }

        private void EnsureSceneReferences()
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                _mainCamera = cameraObject.AddComponent<Camera>();
                cameraObject.tag = "MainCamera";
            }

            _mainCamera.orthographic = true;
            _mainCamera.orthographicSize = 5f;
            _mainCamera.transform.position = new Vector3(0f, 0f, -10f);
            _mainCamera.clearFlags = CameraClearFlags.SolidColor;
            _mainCamera.backgroundColor = Color.clear;

            _windowController = FindFirstObjectByType<UniWindowController>();
            if (_windowController == null)
            {
                var windowControllerObject = new GameObject("UniWindowController");
                _windowController = windowControllerObject.AddComponent<UniWindowController>();
            }

            _windowController.currentCamera = _mainCamera;
            _windowController.autoSwitchCameraBackground = true;
        }

        private void BuildRuntimeObjects()
        {
            var canvasObject = new GameObject("YuukeiRuntimeCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            _runtimeCanvas = canvasObject.GetComponent<Canvas>();
            _runtimeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _runtimeCanvas.sortingOrder = 100;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);

            _mascotRuntime = gameObject.AddComponent<MascotRuntime>();
            _mascotRuntime.Initialize(_mainCamera);

            _speechBubbleController = gameObject.AddComponent<SpeechBubbleController>();
            _speechBubbleController.Initialize(_runtimeCanvas, _mainCamera, () => _mascotRuntime.SpeechAnchorWorldPosition);

            _choiceOverlayController = gameObject.AddComponent<ChoiceOverlayController>();
            _choiceOverlayController.Initialize(_runtimeCanvas);

            _settingsWindow = gameObject.AddComponent<SettingsWindow>();
            _inputContextMonitor = gameObject.AddComponent<InputContextMonitor>();
        }

        private async UniTask ApplyCurrentPackageAsync(CancellationToken cancellationToken)
        {
            var content = _packageManager.GetResolvedActiveContent();
            var report = _packageManager.ValidateActivePackage();
            foreach (var warning in report.Warnings)
            {
                Debug.LogWarning("[ResidentAppController] " + warning);
            }

            await _mascotRuntime.LoadCharacterAsync(content.CharacterPath, cancellationToken);
            await _mascotRuntime.LoadMotionsAsync(content.MotionPaths, cancellationToken);
            _speechBubbleController.ApplyTheme(
                content.TexturePaths.TryGetValue("speechBubble.background", out var backgroundPath) ? backgroundPath : string.Empty,
                content.TexturePaths.TryGetValue("speechBubble.tail", out var tailPath) ? tailPath : string.Empty);
            _pluginLoader.Scan(content.DllPaths);
            await _daihonBridge.ApplyActivePackageAsync(content, _packageManager.ActivePackage?.Manifest.Aliases, cancellationToken);
            RefreshSettingsWindow();
        }

        private async UniTask ApplyAppStateAsync()
        {
            await SetTemporarilyDisabledAsync(_persistenceStore.Data.AppState.IsTemporarilyDisabled);
            await SetTemporarilyHiddenAsync(_persistenceStore.Data.AppState.IsTemporarilyHidden);
            if (_isSettingsVisible)
            {
                ApplySettingsMode();
            }
            else
            {
                ApplyMascotMode();
            }

            UpdateShellState();
        }

        private async UniTask RaiseAppStartedAsync(bool firstLaunch, CancellationToken cancellationToken)
        {
            var context = new Dictionary<string, object>
            {
                ["_event_is_first_launch"] = firstLaunch,
                ["_event_active_package_id"] = _packageManager.ActivePackage?.PackageId ?? string.Empty,
            };
            await _daihonBridge.RaiseEventAsync("app_started", context, cancellationToken);
        }

        private async UniTask ShowSplashAsync(CancellationToken cancellationToken)
        {
            var splash = new GameObject("SplashPanel", typeof(RectTransform), typeof(Image));
            splash.transform.SetParent(_runtimeCanvas.transform, false);
            var splashRect = splash.GetComponent<RectTransform>();
            splashRect.anchorMin = Vector2.zero;
            splashRect.anchorMax = Vector2.one;
            splashRect.offsetMin = Vector2.zero;
            splashRect.offsetMax = Vector2.zero;
            splash.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.16f, 0.94f);

            var labelObject = new GameObject("SplashLabel", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(splash.transform, false);
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(760f, 120f);
            labelRect.anchoredPosition = Vector2.zero;
            var label = labelObject.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 32;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.text = "Yuukei を起動しています";

            await UniTask.Delay(TimeSpan.FromSeconds(1.6f), cancellationToken: cancellationToken);
            Destroy(splash);
        }

        private void HandleShortcutTriggered(ShortcutAction action)
        {
            switch (action)
            {
                case ShortcutAction.OpenSettings:
                    if (_isSettingsVisible)
                    {
                        CloseSettingsAsync().Forget();
                    }
                    else
                    {
                        OpenSettingsAsync().Forget();
                    }
                    break;
                case ShortcutAction.ToggleDisabled:
                    SetTemporarilyDisabledAsync(!_persistenceStore.Data.AppState.IsTemporarilyDisabled).Forget();
                    break;
                case ShortcutAction.ToggleHidden:
                    SetTemporarilyHiddenAsync(!_persistenceStore.Data.AppState.IsTemporarilyHidden).Forget();
                    break;
            }
        }

        private void HandleTrayCommandRequested(TrayCommand command)
        {
            switch (command)
            {
                case TrayCommand.OpenSettings:
                    OpenSettingsAsync().Forget();
                    break;
                case TrayCommand.ToggleDisabled:
                    SetTemporarilyDisabledAsync(!_persistenceStore.Data.AppState.IsTemporarilyDisabled).Forget();
                    break;
                case TrayCommand.ToggleHidden:
                    SetTemporarilyHiddenAsync(!_persistenceStore.Data.AppState.IsTemporarilyHidden).Forget();
                    break;
                case TrayCommand.Exit:
                    ExitApplicationAsync().Forget();
                    break;
            }
        }

        private void OnRuntimeEventRaised(string canonicalName, IReadOnlyDictionary<string, object> context)
        {
            if (_isSettingsVisible)
            {
                return;
            }

            _daihonBridge.RaiseEventAsync(canonicalName, context, _lifetime.Token).Forget();
        }

        private UniTask OpenSettingsAsync()
        {
            _isSettingsVisible = true;
            ApplySettingsMode();
            UpdateShellState();
            RefreshSettingsWindow();
            return UniTask.CompletedTask;
        }

        private UniTask CloseSettingsAsync()
        {
            _isSettingsVisible = false;
            ApplyMascotMode();
            UpdateShellState();
            RefreshSettingsWindow();
            return UniTask.CompletedTask;
        }

        private async UniTask SwitchPackageAsync(string packageId)
        {
            await _packageManager.SwitchActivePackageAsync(packageId, _lifetime.Token);
            _daihonBridge.CancelAndClear();
            await ApplyCurrentPackageAsync(_lifetime.Token);
            await FlushPendingSaveAsync(_lifetime.Token);
        }

        private async UniTask DeletePackageAsync(string packageId)
        {
            await _packageManager.DeletePackageAsync(packageId, _lifetime.Token);
            await ApplyCurrentPackageAsync(_lifetime.Token);
            await FlushPendingSaveAsync(_lifetime.Token);
        }

        private async UniTask ImportPackageAsync(string folderPath)
        {
            if (await _packageManager.ImportPackageFromFolderAsync(folderPath, _lifetime.Token))
            {
                RefreshSettingsWindow();
            }
        }

        private async UniTask SetTemporarilyDisabledAsync(bool disabled)
        {
            _persistenceStore.UpdateAppState(state => state.IsTemporarilyDisabled = disabled);
            _persistenceStore.RequestSave();
            _daihonBridge.SetTemporarilyDisabled(disabled);
            _inputContextMonitor.SetInputEnabled(!disabled && !_isSettingsVisible && !_persistenceStore.Data.AppState.IsTemporarilyHidden);
            UpdateShellState();
            await FlushPendingSaveAsync(_lifetime.Token);
            RefreshSettingsWindow();
        }

        private async UniTask SetTemporarilyHiddenAsync(bool hidden)
        {
            _persistenceStore.UpdateAppState(state => state.IsTemporarilyHidden = hidden);
            _persistenceStore.RequestSave();
            _mascotRuntime.SetVisible(!hidden && !_isSettingsVisible);
            if (hidden)
            {
                _speechBubbleController.Hide();
            }

            _inputContextMonitor.SetInputEnabled(!hidden && !_isSettingsVisible && !_persistenceStore.Data.AppState.IsTemporarilyDisabled);
            UpdateShellState();
            await FlushPendingSaveAsync(_lifetime.Token);
            RefreshSettingsWindow();
        }

        private async UniTask SaveShortcutConfigAsync(ShortcutConfigData shortcutConfig)
        {
            _persistenceStore.UpdateAppState(state => state.ShortcutConfig = shortcutConfig ?? new ShortcutConfigData());
            _desktopAdapter.ApplyShortcuts(_persistenceStore.Data.AppState.ShortcutConfig);
            _persistenceStore.RequestSave();
            await FlushPendingSaveAsync(_lifetime.Token);
            RefreshSettingsWindow();
        }

        private UniTask SaveSecretAsync(string key, string value)
        {
            _desktopAdapter.SaveSecret(key, value ?? string.Empty);
            _apiKeyConfigured = _desktopAdapter.TryLoadSecret("llm.api_key", out _);
            RefreshSettingsWindow();
            return UniTask.CompletedTask;
        }

        private UniTask DeleteSecretAsync(string key)
        {
            _desktopAdapter.DeleteSecret(key);
            _apiKeyConfigured = _desktopAdapter.TryLoadSecret("llm.api_key", out _);
            RefreshSettingsWindow();
            return UniTask.CompletedTask;
        }

        private UniTask ApproveDllsAsync()
        {
            _pluginLoader.ApproveAllPending();
            RefreshSettingsWindow();
            return UniTask.CompletedTask;
        }

        private UniTask ClearDllApprovalsAsync()
        {
            _pluginLoader.ClearApprovals();
            RefreshSettingsWindow();
            return UniTask.CompletedTask;
        }

        private async UniTask ExitApplicationAsync()
        {
            SaveStateOnExit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif

            await UniTask.CompletedTask;
        }

        private void SaveStateOnExit()
        {
            _persistenceStore?.SaveImmediately();
        }

        private void ApplyMascotMode()
        {
            _settingsWindow.SetVisible(false);
            _runtimeCanvas.enabled = true;
            _windowController.isTransparent = true;
            _windowController.isTopmost = true;
            _windowController.allowDropFiles = true;
            _windowController.isHitTestEnabled = true;
            _windowController.hitTestType = UniWindowController.HitTestType.Raycast;

            if (_desktopAdapter != null)
            {
                var bounds = _desktopAdapter.GetVirtualDesktopBounds();
                if (bounds.width > 0 && bounds.height > 0)
                {
                    _windowController.windowPosition = new Vector2(bounds.xMin, bounds.yMin);
                    _windowController.windowSize = new Vector2(bounds.width, bounds.height);
                }
            }

            _mascotRuntime.SetVisible(!_persistenceStore.Data.AppState.IsTemporarilyHidden);
            _inputContextMonitor.SetInputEnabled(!_persistenceStore.Data.AppState.IsTemporarilyDisabled && !_persistenceStore.Data.AppState.IsTemporarilyHidden);
        }

        private void ApplySettingsMode()
        {
            _runtimeCanvas.enabled = false;
            _speechBubbleController.Hide();
            _choiceOverlayController.CancelCurrent();
            _windowController.isTransparent = false;
            _windowController.isTopmost = false;
            _windowController.allowDropFiles = false;
            _windowController.isHitTestEnabled = false;
            _windowController.windowSize = new Vector2(1220f, 820f);
            _settingsWindow.SetVisible(true);
            _mascotRuntime.SetVisible(false);
            _inputContextMonitor.SetInputEnabled(false);
        }

        private void RefreshSettingsWindow()
        {
            _settingsWindow?.Refresh(_persistenceStore, _packageManager, _pluginLoader, _apiKeyConfigured, _desktopAdapter?.GetShortcutStatuses());
            UpdateShellState();
        }

        private AppShellState BuildShellState()
        {
            return new AppShellState(
                _isSettingsVisible,
                _persistenceStore?.Data?.AppState?.IsTemporarilyDisabled ?? false,
                _persistenceStore?.Data?.AppState?.IsTemporarilyHidden ?? false);
        }

        private void UpdateShellState()
        {
            _desktopAdapter?.UpdateShellState(BuildShellState());
        }

        private async UniTask FlushPendingSaveAsync(CancellationToken cancellationToken)
        {
            if (_persistenceStore != null)
            {
                await _persistenceStore.FlushPendingSaveAsync(cancellationToken);
            }
        }
    }
}
