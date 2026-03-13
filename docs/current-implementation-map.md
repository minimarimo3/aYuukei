# 現状実装マップ

更新日: 2026-03-14  
調査対象: `Yuukei/Assets/Scripts/Runtime/`、`Yuukei/Assets/Scenes/SampleScene.unity`、`Yuukei/ProjectSettings/`、`Yuukei/Assets/StreamingAssets/StarterPackages/`、Unity MCP で取得した Scene / Component 情報  
参照した仕様: `docs/yuukei_spec/00_README.md`、`01_概要と固定前提 .md`、`02_キャラクター体験とユーザー接点.md`、`03_UIと状態管理.md`、`04_パッケージと保存仕様.md`、`05_Daihon実行契約.md`、`06_Unity実装構成と受け入れ条件.md`、および `docs/daihon_spec/01-07, 付録`

この文書は「仕様どおりであるべき姿」ではなく、**現時点のコードと Scene が実際にどう動いているか**を整理したものです。

## 読み方

- `確認できた事実`: コード、Scene YAML、Unity MCP、ProjectSettings で直接確認できた内容
- `要確認 / 推測`: その場で断定しきれない内容。可能な限り根拠も併記
- `active path`: 現在の Scene / ランタイム初期化から実際に通る経路

## 1. 全体要約

- 起動エントリは `YuukeiBootstrap.EnsureRuntimeController()` だが、通常は `SampleScene` に既に置かれている `YuukeiRuntime` の `ResidentAppController` が使われる。
- `ResidentAppController.Awake()` で Camera / UniWindowController を確保し、`MascotRuntime`、`SpeechBubbleController`、`ChoiceOverlayController`、`SettingsWindow`、`InputContextMonitor` を動的生成する。
- `ResidentAppController.Start()` から `InitializeAsync()` が走り、`WindowsDesktopAdapter`、`PersistenceStore`、`PackageManager`、`TutorialBootstrap`、`PluginLoader`、`AliasRegistry`、`YuukeiVariableStore`、`DaihonBridge` を順に初期化する。
- 初回起動判定後、スターターパッケージを `persistentDataPath/package/...` へシードし、`PackageManager.InitializeAsync()` でインストール済みパッケージを再スキャンし、アクティブパッケージを決める。
- `ResidentAppController.ApplyCurrentPackageAsync()` が VRM、モーション、吹き出しテクスチャ、DLL 候補、Daihon スクリプト群を一括適用する。
- 初回起動なら簡易スプラッシュを Canvas 上に出した後、`app_started` を `DaihonBridge` に流す。
- 常時更新の入口は `ResidentAppController.Update()` で、毎フレーム `WindowsDesktopAdapter.Tick()`、`InputContextMonitor.Tick()`、`MascotRuntime.SetDesktopContext()`、`MascotRuntime.Tick()` が呼ばれる。
- 入力は Unity の UI イベントではなく、`InputContextMonitor` が `Mouse.current` を直接ポーリングし、クリック / ダブルクリック / ドラッグ / アイドル / periodic_tick / ファイルドロップを canonical event として発火する。
- イベントは `ResidentAppController.OnRuntimeEventRaised()` 経由で `DaihonBridge.RaiseEventAsync()` に積まれ、FIFO キューで順次処理される。`periodic_tick` だけは coalesce される。
- Daihon 実行時は `YuukeiVariableStore` に `_event_*` コンテキストが注入され、`DaihonScriptRuntime` が各 `.daihon` を順に実行する。
- セリフ本文は `SpeechBubbleController.ShowDialogueAsync()`、`show_dialog(...)` は `SpeechBubbleController.ShowImmediate()`、`show_choices(...)` は `ChoiceOverlayController.ShowChoicesAsync()`、表情・モーション・小物は `MascotRuntime` に流れる。
- 保存は `PersistenceStore` が `save.json` を持ち、永続変数は `YuukeiVariableStore -> PersistenceStore.SetPersistentVariable()` で更新される。アプリ状態やパッケージ切替も `PersistenceStore` に集約される。
- 終了時は `ResidentAppController.OnApplicationQuit()` / `ExitApplicationAsync()` から `PersistenceStore.SaveImmediately()` が呼ばれる。

## 2. 実装上の主要コンポーネント一覧

### 2.1 現在の active path に入っている主要クラス

| クラス | ファイルパス | 役割 | 主な public API / 入口 | 主な依存先 | Unity ライフサイクルとの接点 |
| --- | --- | --- | --- | --- | --- |
| `YuukeiBootstrap` | `Yuukei/Assets/Scripts/Runtime/YuukeiBootstrap.cs` | `SampleScene` 読み込み後に `ResidentAppController` の存在を保証する静的ブートストラップ | なし。`EnsureRuntimeController()` が自動実行 | `SceneManager`, `ResidentAppController` | `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` |
| `ResidentAppController` | `Yuukei/Assets/Scripts/Runtime/ResidentAppController.cs` | アプリ全体の起動、常駐制御、モード切替、サービス組み立て、パッケージ適用、保存フラッシュ | `Awake()`, `Start()`, `Update()` が実質的な入口。内部 API は `ApplyCurrentPackageAsync()`, `SetTemporarilyDisabledAsync()`, `SetTemporarilyHiddenAsync()` など | `WindowsDesktopAdapter`, `PersistenceStore`, `PackageManager`, `PluginLoader`, `AliasRegistry`, `YuukeiVariableStore`, `DaihonBridge`, `MascotRuntime`, `InputContextMonitor`, `SettingsWindow`, `UniWindowController` | `Awake`, `Start`, `Update`, `OnApplicationQuit`, `OnDestroy`, `OnValidate` |
| `WindowsDesktopAdapter` | `Yuukei/Assets/Scripts/Runtime/WindowsDesktopAdapter.cs` | Windows のトレイ、ショートカット、モニタ情報、フォアグラウンド状態、秘密情報保存をまとめる OS アダプタ | `Initialize()`, `Shutdown()`, `Tick()`, `ApplyShortcuts()`, `UpdateShellState()`, `GetDisplays()`, `IsForegroundWindowFullscreen()`, `TryLoadSecret()` | `WindowsNativeShellHost`, Win32 API, `ShortcutConfigData` | Unity ライフサイクルは持たず、`ResidentAppController.InitializeAsync()` と `Update()` から呼ばれる |
| `WindowsNativeShellHost` | `Yuukei/Assets/Scripts/Runtime/WindowsShellHost.cs` | ネイティブトレイアイコン、コンテキストメニュー、グローバルホットキー登録 | `Initialize()`, `Shutdown()`, `Tick()`, `ApplyShellState()`, `ApplyShortcuts()` | Win32 API, `ShortcutBinding`, `TrayCommand`, `ShortcutAction` | Unity ライフサイクルは持たず、`WindowsDesktopAdapter` から呼ばれる |
| `PersistenceStore` | `Yuukei/Assets/Scripts/Runtime/PersistenceStore.cs` | `save.json` のロード / セーブ、永続変数・オーバーライド・アプリ状態の single source | `LoadAsync()`, `RequestSave()`, `FlushPendingSaveAsync()`, `SaveImmediately()`, `SetPersistentVariable()`, `UpdateAppState()` | `YuukeiSaveData`, `Newtonsoft.Json` | Unity ライフサイクルは持たず、各サービスから呼ばれる |
| `StarterPackageSeeder` | `Yuukei/Assets/Scripts/Runtime/StarterPackageSeeder.cs` | StreamingAssets のスターターパッケージを persistentDataPath 側へコピー | `EnsureInstalledAsync()`, `IsInstalled()` | `FileSystemStarterPackageSource`, `PackageDirectoryUtility` | Unity ライフサイクルなし。`TutorialBootstrap` から呼ばれる |
| `TutorialBootstrap` | `Yuukei/Assets/Scripts/Runtime/TutorialBootstrap.cs` | save.json の有無で初回起動判定し、スターターパッケージ導入を補助 | `IsFirstLaunch()`, `EnsureFirstLaunchPackageStateAsync()` | `PersistenceStore`, `StarterPackageSeeder` | Unity ライフサイクルなし。`ResidentAppController.InitializeAsync()` から呼ばれる |
| `PackageManager` | `Yuukei/Assets/Scripts/Runtime/PackageManager.cs` | パッケージ再スキャン、アクティブ切替、ローカルフォルダ取り込み、削除、最終コンテンツ解決 | `InitializeAsync()`, `ReloadInstalledPackagesAsync()`, `SwitchActivePackageAsync()`, `ImportPackageFromFolderAsync()`, `DeletePackageAsync()`, `GetResolvedActiveContent()` | `PersistenceStore`, `PackageManifest`, `ResolvedPackage` | Unity ライフサイクルなし。`ResidentAppController` から呼ばれる |
| `PluginLoader` | `Yuukei/Assets/Scripts/Runtime/PluginLoader.cs` | DLL 候補のスキャン、承認状態のメモリ保持、承認済み DLL の `Assembly.LoadFrom` | `Scan()`, `ApproveAllPending()`, `ClearApprovals()`, `ActivateApprovedPlugins()`, `BuildWarningText()` | `Assembly`, DLL ファイルパス | Unity ライフサイクルなし。`ResidentAppController` と `SettingsWindow` から使われる |
| `AliasRegistry` | `Yuukei/Assets/Scripts/Runtime/AliasRegistry.cs` | イベント / 関数 alias の built-in + package overlay 解決 | `ResetToBuiltins()`, `LoadPackageAliases()`, `RegisterEventAlias()`, `RegisterFunctionAlias()`, `TryResolveEventName()`, `TryResolveFunctionName()` | `PackageAliasManifest` | Unity ライフサイクルなし。`DaihonBridge`, `DaihonFunctionDispatcher`, `DaihonScriptRuntime`, `MascotRuntime` が参照 |
| `YuukeiVariableStore` | `Yuukei/Assets/Scripts/Runtime/YuukeiVariableStore.cs` | Daihon 用の永続変数・一時変数・動的時間変数・イベントコンテキストを統合管理 | `GetValue()`, `SetValue()`, `SetDefaultValue()`, `InjectEventContext()`, `ResetTransientState()` | `PersistenceStore`, `DaihonValueUtility`, `Daihon.IVariableStore` | Unity ライフサイクルなし。`DaihonBridge` / `DaihonScriptRuntime` から使われる |
| `DaihonBridge` | `Yuukei/Assets/Scripts/Runtime/DaihonBridge.cs` | Daihon スクリプト読み込み、イベント alias 解決、キュー処理、キャンセル、関数ディスパッチ接続 | `ApplyActivePackageAsync()`, `RaiseEventAsync()`, `SetTemporarilyDisabled()`, `CancelAndClear()`, `RegisterFunction()` | `AliasRegistry`, `YuukeiVariableStore`, `SpeechBubbleController`, `ChoiceOverlayController`, `MascotRuntime`, `DaihonScriptRuntime`, `DaihonFunctionDispatcher` | 自前の async キュー (`ProcessQueueAsync`) を持つ。MonoBehaviour ではない |
| `DaihonFunctionDispatcher` | `Yuukei/Assets/Scripts/Runtime/DaihonFunctionDispatcher.cs` | `show_dialog`, `set_expression`, `play_motion`, `set_prop_visible`, `show_choices`, `set_persistent` の実装 | `RegisterFunction()`, `InvokeAsync()` | `AliasRegistry`, `SpeechBubbleController`, `ChoiceOverlayController`, `MascotRuntime`, `YuukeiVariableStore` | Unity ライフサイクルなし。`DaihonBridge` から呼ばれる |
| `DaihonScriptRuntime` | `Yuukei/Assets/Scripts/Runtime/DaihonScriptRuntime.cs` | `.daihon` を parse し、シーン選択・条件評価・ジャンプ処理を行う | `Parse()`, `RunEventAsync()`, `EvaluateConditionAsync()` | `DaihonLexer`, `DaihonParser`, `DaihonScriptVisitor` | Unity ライフサイクルなし。`DaihonBridge` から呼ばれる |
| `InputContextMonitor` | `Yuukei/Assets/Scripts/Runtime/InputContextMonitor.cs` | マウス入力、ドラッグ、アイドル、periodic_tick、ファイルドロップ、許可ディスプレイ計算 | `Initialize()`, `SetInputEnabled()`, `Tick()`, `RecalculateAllowedDisplays()`, `BusyScore` | `IDesktopPlatformAdapter`, `UniWindowController`, `MascotRuntime`, `FileKindClassifier`, `Mouse.current` | `ResidentAppController.Update()` から毎フレーム呼ばれる。`OnDestroy()` で OnDropFiles を解除 |
| `MascotRuntime` | `Yuukei/Assets/Scripts/Runtime/MascotRuntime.cs` | マスコットの VRM / プレースホルダ表示、モーション、表情、ドラッグ、表示可否、画面座標変換 | `Initialize()`, `SetDesktopContext()`, `LoadCharacterAsync()`, `LoadMotionsAsync()`, `SetExpression()`, `PlayMotion()`, `SetPropVisible()`, `SetVisible()`, `MoveByScreenDelta()`, `HitTestScreenPoint()`, `Tick()` | `Camera`, `UniVRM10`, `UniGLTF`, `PlayableGraph`, `GlideLocomotionSettings`, `DragMotionSettings` | `ResidentAppController.Update()` から `Tick()`, 自身の `LateUpdate()`, `OnDestroy()` |
| `SpeechBubbleController` | `Yuukei/Assets/Scripts/Runtime/SpeechBubbleController.cs` | 吹き出し UI の生成、即時表示、待機付き表示、位置追従、テーマ差し替え | `Initialize()`, `ShowDialogueAsync()`, `ShowImmediate()`, `Hide()`, `ApplyTheme()` | `Canvas`, `Camera`, `TextMeshProUGUI`, `Image` | 自身の `LateUpdate()` と async auto-hide |
| `ChoiceOverlayController` | `Yuukei/Assets/Scripts/Runtime/ChoiceOverlayController.cs` | `show_choices(...)` 用のボタンオーバーレイ | `Initialize()`, `ShowChoicesAsync()`, `CancelCurrent()`, `CancelFromRuntimeCancellation()` | `Canvas`, `EventSystem`, `InputSystemUIInputModule`, `Button` | 自身の `Update()` で Esc 監視 |
| `SettingsWindow` | `Yuukei/Assets/Scripts/Runtime/SettingsWindow.cs` | サイドバー式設定 UI をランタイム生成する | `Initialize()`, `SetVisible()`, `Refresh()` | `UIDocument`, `PanelSettings`, `PersistenceStore`, `PackageManager`, `PluginLoader` | MonoBehaviour だが `Update()` は持たない。UI Toolkit を runtime 構築 |
| `VRMShaderPreloader` | `Yuukei/Assets/Scripts/VRMShaderPreloader.cs` | シェーダ用ダミーマテリアル参照をビルドに残す受け皿 | public field `preloadMaterials` のみ | `VRMShaderPreloaderPrefab.prefab` | Scene 上の prefab instance として存在するが、実行コードからは読まれない |

### 2.2 仕様名は存在するが、責務は部分実装のまま

以下は「クラス自体は active path にあるが、仕様で想定される責務をまだ全部は担っていない」箇所です。

| クラス / 領域 | 確認できた事実 | 影響 |
| --- | --- | --- |
| `MascotRuntime` | `Tick(float deltaTime, float busyScore)` は `busyScore` を受け取るが未使用。`_desktopPosition` を時間経過で更新する処理もなく、位置変更は初期安全位置・禁止ディスプレイからの退避・ドラッグ時のみ | 「忙しい時は移動抑制」「常時うろつき」は現状未実装。見た目のふわふわは `VisualRoot.localPosition/localRotation` のみ |
| `InputContextMonitor` | `RecalculateAllowedDisplays()` は `IsForegroundWindowFullscreen()` と foreground display index しか見ていない | フルスクリーン回避は一部あり。特定アプリ使用中ディスプレイ回避は現コード上見つからない |
| `PackageManager` / `MascotRuntime` | `PackageContentSelection.AssetPaths` は解決されるが、参照先がない | package の `assets` / 小物差し替えは manifest で解決はされるが、実際の表示・生成には未接続 |
| `SettingsWindow` | 外見ページは現在値の表示中心で、override 編集 UI はない。`PersistenceStore.SetOverrides()` も呼ばれていない | package 準拠 / 個別指定の「見える化」はあるが、変更導線は未実装 |
| `PluginLoader` | 承認状態は `_approvedPaths` のメモリ保持のみ。承認後に `ActivateApprovedPlugins()` を呼ぶ導線も無い | DLL 承認 UI はあるが、承認の永続化と実際の有効化フローは未完成 |
| `DaihonFunctionDispatcher` | `InvokeAsync()` は positional args のみを扱い、`BridgeActionHandler.CallFunctionAsync()` も `namedArgs` を捨てている | Daihon の名前付き引数構文が Unity 側組み込み関数では活きていない |
| `DaihonScriptRuntime` | signal を持たない condition-only scene を「常時監視」せず、**イベントが来たタイミングで**評価している | 仕様書の常時監視イメージとは異なり、現実装では event-driven にしか動かない |

### 2.3 存在はするが現行ルートで未接続、または現在ビルドへ直結していないもの

| 対象 | ファイル / 場所 | 状態 | 根拠 |
| --- | --- | --- | --- |
| `RuntimeEventData` structs | `Yuukei/Assets/Scripts/Runtime/RuntimeEventData.cs` | 未使用 | `FileDropEventData`, `PeriodicTickEventData` の参照が他ファイルに無い |
| `AllowedDisplaysChanged` event | `Yuukei/Assets/Scripts/Runtime/InputContextMonitor.cs` | 未購読 | 発火はあるが購読箇所が無い |
| `PersistenceStore.SetOverrides()` | `Yuukei/Assets/Scripts/Runtime/PersistenceStore.cs` | 未使用 | `ResetOverrides()` は使われるが `SetOverrides()` 呼び出しが見当たらない |
| UI Toolkit asset `PanelSettings.asset` | `Yuukei/Assets/UI Toolkit/PanelSettings.asset` | 未接続 | `SettingsWindow.Initialize()` は `ScriptableObject.CreateInstance<PanelSettings>()` を使用 |
| `InputSystem_Actions.inputactions` | `Yuukei/Assets/InputSystem_Actions.inputactions` | Scene UI からは未使用 | ProjectSettings では default actions asset として登録されているが、Scene の `InputSystemUIInputModule` は package 既定 asset を参照 |
| `Daihon/Unity/*` | `Daihon/Unity/` | 現 Unity プロジェクトには未接続 | `Assets/**` 内から参照が無く、Unity の compile path (`Assets/`) 外 |
| `Daihon/src/*` | `Daihon/src/` | 現 Unity プロジェクトには未接続 | 文法 / visitor ソースはあるが、実際の Unity 側は `Assets/Plugins/Daihon.dll` を参照している |
| `VRMShaderPreloader.preloadMaterials` | `Yuukei/Assets/Scripts/VRMShaderPreloader.cs` | 受け身の参照保持 | 編集用 generator から代入されるだけで、実行時コードがこの配列を読んでいない |

## 3. 実行経路マップ

### 3.1 起動時のエントリポイント

確認できた active path:

1. Unity が `Assets/Scenes/SampleScene.unity` をロードする
2. `[RuntimeInitializeOnLoadMethod]` の `YuukeiBootstrap.EnsureRuntimeController()` が走る
3. `SampleScene` には既に `YuukeiRuntime` GameObject + `ResidentAppController` があるため、bootstrap は通常何もしない
4. `ResidentAppController.Awake()`
5. `ResidentAppController.EnsureSceneReferences()`
6. `ResidentAppController.BuildRuntimeObjects()`
7. `ResidentAppController.Start()` -> `InitializeAsync().Forget()`
8. `InitializeAsync()` 内で各サービス初期化、初回導入、パッケージ適用、保存読込、Daihon ブリッジ初期化、shell state 更新、`app_started` 発火

### 3.2 `ResidentAppController.InitializeAsync()` の主経路

call chain:

- `WindowsDesktopAdapter.Initialize()`
- `InputContextMonitor.Initialize(_desktopAdapter, _windowController, _mascotRuntime, _choiceOverlayController)`
- `SettingsWindow.Initialize(...)`
- `PersistenceStore.LoadAsync()`
- `TutorialBootstrap.IsFirstLaunch()`
- `TutorialBootstrap.EnsureFirstLaunchPackageStateAsync()`
- `PackageManager.InitializeAsync()`
- `PackageManager.GetResolvedActiveContent()`
- `ApplyCurrentPackageAsync()`
- `PluginLoader.ActivateApprovedPlugins()`
- `RefreshSettingsWindow()`
- `ApplyPersistedState()`
- 初回起動なら `ShowSplashAsync()`
- `DaihonBridge.RaiseEventAsync("app_started", RuntimeEventContext.Empty, _lifetimeCts.Token)`
- `UpdateShellState()`

補足:

- `ApplyCurrentPackageAsync()` の中で alias 再構築、Daihon スクリプト再読込、VRM 読込、モーション読込、吹き出しテーマ適用、DLL スキャンまで行う
- 起動処理の集中点はほぼ `ResidentAppController` 一箇所

### 3.3 常時更新の入口

毎フレームの active path は `ResidentAppController.Update()` が入口。

call chain:

- `_desktopAdapter.Tick()`
- `_inputContextMonitor.Tick(Time.deltaTime)`
- `_mascotRuntime.SetDesktopContext(_desktopAdapter.GetVirtualDesktopBounds(), _desktopAdapter.GetDisplays(), _inputContextMonitor.RecalculateAllowedDisplays())`
- `_mascotRuntime.Tick(Time.deltaTime, _inputContextMonitor.BusyScore)`

そこから派生する更新:

- `InputContextMonitor.Tick()` 内でクリック / ダブルクリック / ドラッグ / アイドル / periodic_tick 判定
- `MascotRuntime.Tick()` 内で表示可否判定、禁止ディスプレイ退避、ドラッグ継続
- `MascotRuntime.LateUpdate()` 内で `VisualRoot.localPosition` / `localRotation` を揺らす
- `SpeechBubbleController.LateUpdate()` 内で吹き出し位置を追従
- `ChoiceOverlayController.Update()` 内で Esc キャンセル監視
- `WindowsNativeShellHost.Tick()` は `_desktopAdapter.Tick()` の内側で処理される

### 3.4 入力イベントの流れ

#### クリック / ダブルクリック

call chain:

- `ResidentAppController.Update()`
- `InputContextMonitor.Tick()`
- `Mouse.current.leftButton.wasPressedThisFrame`
- `MascotRuntime.HitTestScreenPoint(screenPoint)`
- `BeginPendingClick()`
- 既定時間内に再クリックが来なければ `FlushPendingClick()`
- `RaiseCanonicalEvent("click", context)`
- `ResidentAppController.OnRuntimeEventRaised(eventName, context)`
- `DaihonBridge.RaiseEventAsync(...)`

ダブルクリック時:

- `HandlePress()` 内で pending click を `double_click` に昇格
- 単発 `click` はキャンセルされる

#### ドラッグ

call chain:

- `InputContextMonitor.Tick()` で press 中かつ移動量が `_dragThresholdPixels` 超過
- `TryBeginDrag(screenPoint)`
- `MascotRuntime.BeginDrag(screenPoint, offset)`
- `RaiseCanonicalEvent("drag_start", context)`
- press 継続中は `UpdateDrag(screenPoint)`
- `MascotRuntime.MoveByScreenDelta(deltaPixels)` または `UpdateDrag` 内部での追従
- ボタンを離すと `EndDrag()`
- `MascotRuntime.EndDrag()`
- `RaiseCanonicalEvent("drag_end", context)`

#### ファイルドロップ

call chain:

- `UniWindowController.OnDropFiles`
- `InputContextMonitor.HandleDropFiles(string[] paths)`
- `FileKindClassifier.Classify(paths)`
- `RaiseCanonicalEvent("file_drop", RuntimeEventContext.WithValue(...))`
- `DaihonBridge.RaiseEventAsync("file_drop", ...)`

#### アイドル / periodic_tick

call chain:

- `InputContextMonitor.Tick()` が `_secondsSinceInput` を更新
- 一定秒数で `idle`
- 一定間隔で `periodic_tick`
- `periodic_tick` は `DaihonBridge.RaiseEventAsync()` 内で既存 pending を置き換える coalesce 動作あり

### 3.5 Daihon 実行要求の流れ

call chain:

- 任意イベント発火元 (`InputContextMonitor`, 初回起動処理, tray command など)
- `ResidentAppController.OnRuntimeEventRaised()`
- `DaihonBridge.RaiseEventAsync(eventName, context, token)`
- `AliasRegistry.TryResolveEventName()`
- queue へ `QueuedRuntimeEvent`
- `ProcessQueueAsync()`
- `YuukeiVariableStore.InjectEventContext(context, canonicalEventName)`
- 各 `LoadedScript.Runtime.RunEventAsync(canonicalEventName, scriptContext, cancellationToken)`
- `DaihonScriptRuntime.RunEventAsync()`
- `BridgeActionHandler.CallFunctionAsync()` / `CallNamedFunctionAsync()`
- `DaihonFunctionDispatcher.InvokeAsync()`
- `SpeechBubbleController` / `ChoiceOverlayController` / `MascotRuntime` / `YuukeiVariableStore` へ反映

補足:

- `DaihonBridge` は `_isTemporarilyDisabled` の間、新規イベントを無視する
- 新しい package 適用時は `CancelAndClear()` でキューと UI を落としてからスクリプトを再ロードする

### 3.6 表示更新の流れ

#### マスコット本体

- `ApplyCurrentPackageAsync()` -> `MascotRuntime.LoadCharacterAsync()` / `LoadMotionsAsync()`
- `MascotRuntime.UpdateDisplayVisibility()` が `_root.gameObject.SetActive(...)`
- `MascotRuntime.Tick()` が desktop context と drag を処理
- `MascotRuntime.LateUpdate()` が `VisualRoot.localPosition` / `VisualRoot.localRotation` を上書き
- `DaihonFunctionDispatcher.set_expression/play_motion/set_prop_visible` が必要時に見た目を変更

#### 吹き出し

- Daihon のセリフ block -> `SpeechBubbleController.ShowDialogueAsync()`
- `show_dialog(...)` -> `SpeechBubbleController.ShowImmediate()`
- `SpeechBubbleController.LateUpdate()` がアンカー追従

#### 選択 UI

- `show_choices(...)` -> `ChoiceOverlayController.ShowChoicesAsync()`
- 選択完了 or Esc で panel 非表示

#### 設定ウィンドウ

- tray / shortcut / 初回フローから `ResidentAppController.OpenSettings()`
- `ApplySettingsMode()` でウィンドウサイズと click-through を変更
- `SettingsWindow.SetVisible(true)`

### 3.7 保存 / 復元の流れ

#### 起動時ロード

- `ResidentAppController.InitializeAsync()`
- `PersistenceStore.LoadAsync()`
- `ApplyPersistedState()`
- `PackageManager.InitializeAsync()` が `PersistenceStore.Data.app.activePackageId` を参照
- `WindowsDesktopAdapter.TryLoadSecret("openai_api_key", out value)` が API key UI 初期値に使われる

#### ランタイム中の保存

- 行動設定変更: `SettingsWindow` -> `ResidentAppController` -> `PersistenceStore.UpdateAppState(...)`
- 永続変数更新: `DaihonFunctionDispatcher.HandleSetPersistentAsync()` -> `YuukeiVariableStore.SetValue(..., persistent: true)` -> `PersistenceStore.SetPersistentVariable()`
- package 切替: `PackageManager.SwitchActivePackageAsync()` -> `PersistenceStore.UpdateAppState(...)`
- save は原則 `RequestSave()` で遅延キュー

#### 終了時

- `ResidentAppController.OnApplicationQuit()`
- `_persistenceStore.SaveImmediately()`
- `_desktopAdapter.Shutdown()`

## 4. 所有権と状態管理

### 4.1 single source of truth 一覧

| 状態 | 主な保持先 | 補助キャッシュ / 読み取り側 | 備考 |
| --- | --- | --- | --- |
| アプリ設定・永続変数・active package id | `PersistenceStore.Data` | `PackageManager`, `YuukeiVariableStore`, `SettingsWindow`, `ResidentAppController` | 永続化の基準点は `PersistenceStore` |
| Daihon 永続変数 | `PersistenceStore.Data.variables` | `YuukeiVariableStore` がミラー | 数値は save 時に `double` へ正規化 |
| Daihon 一時変数 / `_event_*` | `YuukeiVariableStore` | `DaihonBridge`, `DaihonScriptRuntime` | script 実行ごと / event ごとにクリア・再注入 |
| 現在アクティブな package | `PersistenceStore.Data.app.activePackageId` + `PackageManager._activePackage` | `ResidentAppController`, `SettingsWindow` | runtime 上の実働参照は `PackageManager`、永続化の基準は `PersistenceStore` |
| DLL 承認状態 | `PluginLoader._approvedPaths` | `SettingsWindow` | メモリのみ。永続ソースなし |
| マスコットの desktop 位置 | `MascotRuntime._desktopPosition` | `InputContextMonitor` は読み取りのみ | 現在位置の single source |
| マスコット可視状態 | `MascotRuntime._isVisible`, `_temporarilyHidden`, `_allowedDisplayIndices` | `ResidentAppController` が一時フラグを更新 | 実際の GameObject active は `UpdateDisplayVisibility()` が反映 |
| 吹き出し表示状態 | `SpeechBubbleController` 内部 (`_displayVersion`, `_bubbleRoot`, `_messageLabel.text`) | `DaihonBridge.CancelAndClear()` が `Hide()` を呼ぶ | 単一吹き出し制御 |
| settings 表示状態 | `ResidentAppController._settingsVisible` | `SettingsWindow` | UniWindow のモード切替とも同期 |
| 一時無効 / 一時非表示 | `ResidentAppController._temporarilyDisabled`, `_temporarilyHidden` | `DaihonBridge`, `InputContextMonitor`, `MascotRuntime` | shell 状態や表示更新へ伝播 |
| busy 指標 | `InputContextMonitor.BusyScore` | `ResidentAppController.Update()` から `MascotRuntime.Tick()` へ渡す | ただし現 `MascotRuntime` では未使用 |
| 現在の VRM / motion / expression | `MascotRuntime` (`_characterRoot`, `_instance`, `_motionClips`, `_currentEmotion`, `_activeMotionHandle`) | `DaihonFunctionDispatcher` | VRM 再ロードで入れ替わる |

### 4.2 グローバル状態に近い集中点

確認できた事実:

- `ResidentAppController` が依存オブジェクトをほぼ全部所有している
- `PersistenceStore` が永続情報の最終保存先
- `MascotRuntime` が見た目本体の実体を所有
- `DaihonBridge` が Daihon 実行キューの集中点

要確認 / 推測:

- 現在の設計は「サービスロケータ無しの手組み dependency graph」に近く、今後機能追加は `ResidentAppController` に集まりやすい

### 4.3 busy 状態の実態

確認できた事実:

- `InputContextMonitor.BusyScore` は `1f - Mathf.Clamp01(_secondsSinceInput / 15f)` で計算される
- つまり入力直後ほど `1.0` に近く、15 秒放置で `0.0`
- 仕様文脈の「busy」よりも、実装実態は「最近入力があったか」の activity score
- `ResidentAppController.Update()` から `MascotRuntime.Tick(deltaTime, busyScore)` に渡しているが、`MascotRuntime.Tick` 内で未使用

結論:

- busy の single source は現状 `InputContextMonitor`
- ただし視覚挙動へはまだ効いていない

## 5. transform / 表示更新の書き込み経路

この章は「どこがどの transform / visible state を書き換えるか」をコード上の実経路で整理したものです。

### 5.1 マスコット本体の位置

#### `MascotRuntime._desktopPosition` -> world position

ファイル:

- `Yuukei/Assets/Scripts/Runtime/MascotRuntime.cs`

書き込み箇所:

- `SetDesktopPositionInternal(Vector2 desktopPosition, bool clampToBounds)`
- `MoveByScreenDelta(Vector2 deltaPixels)`
- `UpdateDrag(Vector2 screenPoint)` の内部
- `Tick()` 内の禁止ディスプレイ退避
- `Initialize()` / `SetDesktopContext()` 内の初期安全位置決定

world / local:

- `_desktopPosition` は desktop space の独自 2D 座標
- 反映先は `_root.transform.position` で、**world position** を書く

更新タイミング:

- 毎フレームではない
- 初期化時、drag 中、禁止ディスプレイへ入った時、文脈更新時のみ

競合可能性:

- 現状の active path では `_root.transform.position` を直接書く中心は `MascotRuntime` のみ
- ただし今後 wander や tween を別クラスで追加すると、高確率で競合する

#### `VisualRoot.localPosition` / `localRotation`

書き込み箇所:

- `MascotRuntime.LateUpdate()`

world / local:

- `VisualRoot.localPosition`
- `VisualRoot.localRotation`
- **local** 書き込み

更新タイミング:

- 毎フレーム (`LateUpdate`)

競合可能性:

- 表情差し替えとは競合しない
- 将来、アニメーション・IK・手動オフセットが `VisualRoot` を直接いじると衝突しやすい

### 5.2 マスコット表示 / 非表示

書き込み箇所:

- `MascotRuntime.SetVisible(bool visible)`
- `MascotRuntime.SetTemporarilyHidden(bool hidden)`
- `MascotRuntime.SetDesktopContext(...)`
- `MascotRuntime.Tick(...)`
- これらから `UpdateDisplayVisibility()` が呼ばれ、最終的に `_root.gameObject.SetActive(shouldBeVisible)` を実行

更新タイミング:

- イベント時 + 毎フレーム `Tick()` の中

single source:

- `_isVisible`, `_temporarilyHidden`, `_allowedDisplayIndices`, `_currentDisplayIndex`

競合可能性:

- 他クラスが `_root` 配下 child を個別非表示にする場合は二重管理化しやすい
- `SetActive(false)` で root を落とすため、child 側の独自表示制御は消される

### 5.3 吹き出し位置

ファイル:

- `Yuukei/Assets/Scripts/Runtime/SpeechBubbleController.cs`

書き込み箇所:

- `LateUpdate()`
- `UpdateBubblePosition()`

world / local:

- 吹き出しは Canvas 上の `RectTransform.anchoredPosition`
- screen space overlay ではなく camera 付き Canvas なので、実装上は screen/world 変換を経由した UI 座標

更新タイミング:

- 毎フレーム `LateUpdate()`

競合可能性:

- 現状 single writer は `SpeechBubbleController`
- ただし将来アニメーションや複数吹き出し化を入れると UI レイアウト競合が起きやすい

### 5.4 選択 UI の表示状態

ファイル:

- `Yuukei/Assets/Scripts/Runtime/ChoiceOverlayController.cs`

書き込み箇所:

- `ShowChoicesAsync()` で overlay root の active、button 再構築、`CanvasGroup` 相当の見た目を設定
- `HideInternal()` で非表示

更新タイミング:

- イベント時のみ

競合可能性:

- 現状 single writer
- `EventSystem` と入力禁止状態の整合は `InputContextMonitor.SetInputEnabled()` と関係するため、入力仕様変更時は合わせて確認が必要

### 5.5 設定ウィンドウの表示

ファイル:

- `Yuukei/Assets/Scripts/Runtime/ResidentAppController.cs`
- `Yuukei/Assets/Scripts/Runtime/SettingsWindow.cs`

書き込み箇所:

- `ResidentAppController.OpenSettings()`
- `ResidentAppController.CloseSettings()`
- `ApplySettingsMode()` / `ApplyMascotMode()`
- `SettingsWindow.SetVisible(bool)`

状態変更の実体:

- `UniWindowController` の `isTopmost`, `isTransparent`, `isClickThrough`, `windowPosition`, `windowSize`
- `UIDocument.rootVisualElement.style.display`

競合可能性:

- 設定画面表示は window mode と UI 表示の二重制御
- 片方だけ触ると「ウィンドウは設定モードだが UI が出ない」または逆が起こりうる

## 6. Scene / Inspector / SerializeField 接続

### 6.1 現在使われている Scene

確認できた事実:

- `Yuukei/ProjectSettings/EditorBuildSettings.asset` の build scene は `Assets/Scenes/SampleScene.unity` のみ
- Unity MCP の active scene も `Assets/Scenes/SampleScene.unity`

結論:

- 現行 active path は `SampleScene` を前提に見てよい

### 6.2 `SampleScene` の主要 root GameObject

Unity MCP で確認できた root:

- `Main Camera`
- `Directional Light`
- `Global Volume`
- `YuukeiRuntime`
- `VRMShaderPreloaderPrefab`
- `EventSystem`
- `UniWindowController`

### 6.3 主要 GameObject と重要コンポーネント

| GameObject | 重要コンポーネント | 補足 |
| --- | --- | --- |
| `YuukeiRuntime` | `ResidentAppController` | 現行ランタイムの実質的ルート。多くの runtime object はこの子として動的生成される |
| `Main Camera` | `Camera`, `UniversalAdditionalCameraData`, `AudioListener` | `ResidentAppController.EnsureSceneReferences()` が投影方式や clear color を起動時に上書き |
| `EventSystem` | `EventSystem`, `InputSystemUIInputModule` | `ChoiceOverlayController` の UI 操作に必要 |
| `UniWindowController` | `UniWindowController` | click-through / transparent / topmost 制御の実体 |
| `VRMShaderPreloaderPrefab` | `VRMShaderPreloader` | shader stripping 回避向けの可能性が高い。要確認 |
| `Global Volume` | `Volume` | 現 runtime コードから直接触ってはいない |

### 6.4 `ResidentAppController` の主要 SerializeField

`YuukeiRuntime` の Inspector 相当として確認できたもの:

- `_glideLocomotionSettings`
- `_dragMotionSettings`
- `_settingsFont`

詳細:

- `_dragMotionSettings.Clip` は `Assets/Motions/X Bot@Female Dynamic Pose.anim`
- `_settingsFont` は `Assets/Fonts/DotGothic16-Regular.ttf`
- `_camera`, `_windowController` は SerializeField だが、未設定でも `EnsureSceneReferences()` が `FindAnyObjectByType<>()` で補完する

壊れやすいポイント:

- `ResidentAppController` のフィールド名変更は Scene YAML 参照破壊の可能性
- `DragMotionSettings` / `GlideLocomotionSettings` の serializable field 名変更も Inspector 値ロストの可能性

### 6.5 動的生成される runtime GameObject

`ResidentAppController.BuildRuntimeObjects()` が生成するもの:

- `MascotRuntime`
- `YuukeiRuntimeCanvas`
- `SpeechBubbleController`
- `ChoiceOverlayController`
- `SettingsWindow`
- `InputContextMonitor`

意味:

- Scene に見えていないから未使用、とは言えない
- 逆に prefab 化されていないため、コンポーネント名変更や required component 追加時は `BuildRuntimeObjects()` 側も同時に直す必要がある

### 6.6 名前変更・型変更で Scene 接続が壊れそうな箇所

優先度高:

- `ResidentAppController` の SerializeField 名
- `UniWindowController` 型そのもの
- `InputSystemUIInputModule` の存在
- `Main Camera` の `Camera` コンポーネント
- `YuukeiRuntime` GameObject 上の `ResidentAppController`

理由:

- これらは `FindAnyObjectByType<>()` でのフォールバックか、Scene 直参照に依存している
- 型変更・削除時はコンパイルが通っても runtime 初期化で null になりやすい

## 7. 機能別マップ

### 7.1 キャラクター移動

担当クラス:

- `ResidentAppController`
- `InputContextMonitor`
- `MascotRuntime`
- `WindowsDesktopAdapter`

流れ:

- 毎フレーム `ResidentAppController.Update()`
- `InputContextMonitor.RecalculateAllowedDisplays()`
- `MascotRuntime.SetDesktopContext(...)`
- 必要なら `MascotRuntime.Tick()` で safe display へ退避
- ドラッグ中は `InputContextMonitor` -> `MascotRuntime.UpdateDrag()`

確認できた事実:

- 自律移動はまだ無い
- 位置書き換えはほぼ drag / safe-position 補正のみ

変更時にまず見るべきファイル:

- `Yuukei/Assets/Scripts/Runtime/MascotRuntime.cs`
- `Yuukei/Assets/Scripts/Runtime/InputContextMonitor.cs`
- `Yuukei/Assets/Scripts/Runtime/ResidentAppController.cs`

### 7.2 ドラッグ

担当クラス:

- `InputContextMonitor`
- `MascotRuntime`
- `DaihonBridge`

流れ:

- press 判定
- threshold 超えで `drag_start`
- `MascotRuntime.BeginDrag()` / `UpdateDrag()` / `EndDrag()`
- 開始・終了時のみ Daihon event 発火

確認できた事実:

- drag 中の毎フレーム event は飛ばしていない
- drag motion は `DragMotionSettings` 経由で clip 再生される

まず見るべきファイル:

- `Yuukei/Assets/Scripts/Runtime/InputContextMonitor.cs`
- `Yuukei/Assets/Scripts/Runtime/MascotRuntime.cs`

### 7.3 吹き出し表示

担当クラス:

- `DaihonBridge`
- `SpeechBubbleController`
- `DaihonFunctionDispatcher`

流れ:

- 通常セリフ block -> `ShowDialogueAsync()`
- `show_dialog(...)` -> `ShowImmediate()`
- `LateUpdate()` で追従

確認できた事実:

- 同時に一つだけ
- 新表示で `_displayVersion` が進み、旧 auto-hide が無効化される

まず見るべきファイル:

- `Yuukei/Assets/Scripts/Runtime/SpeechBubbleController.cs`
- `Yuukei/Assets/Scripts/Runtime/DaihonBridge.cs`
- `Yuukei/Assets/Scripts/Runtime/DaihonFunctionDispatcher.cs`

### 7.4 モーション再生

担当クラス:

- `MascotRuntime`
- `DaihonFunctionDispatcher`

流れ:

- package 適用時 `LoadMotionsAsync()`
- Daihon `play_motion(...)`
- `MascotRuntime.PlayMotion()`
- `PlayableGraph` / `AnimationPlayableOutput` に接続

確認できた事実:

- motion clip 名は file name ベースで登録される
- drag pose も別経路で `DragMotionSettings` から使う

まず見るべきファイル:

- `Yuukei/Assets/Scripts/Runtime/MascotRuntime.cs`
- `Yuukei/Assets/Scripts/Runtime/DaihonFunctionDispatcher.cs`

### 7.5 Daihon イベント配送

担当クラス:

- `InputContextMonitor`
- `ResidentAppController`
- `DaihonBridge`
- `AliasRegistry`
- `DaihonScriptRuntime`

流れ:

- event source
- `OnRuntimeEventRaised()`
- `DaihonBridge.RaiseEventAsync()`
- alias canonical 化
- queue
- `RunEventAsync()`

確認できた事実:

- 1 event at a time
- FIFO
- `periodic_tick` は coalesce
- package alias が built-in より優先

まず見るべきファイル:

- `Yuukei/Assets/Scripts/Runtime/DaihonBridge.cs`
- `Yuukei/Assets/Scripts/Runtime/AliasRegistry.cs`
- `Yuukei/Assets/Scripts/Runtime/DaihonScriptRuntime.cs`

### 7.6 package 読み込み

担当クラス:

- `TutorialBootstrap`
- `StarterPackageSeeder`
- `PackageManager`
- `ResidentAppController`
- `PluginLoader`

流れ:

- 初回のみ starter seed
- `PackageManager.InitializeAsync()`
- active package 解決
- `ResidentAppController.ApplyCurrentPackageAsync()`
- character / motions / bubble / daihon / aliases / dll scan

確認できた事実:

- DLL は scan されるが自動 load しない
- package 削除は active package なら先に fallback package を探す

まず見るべきファイル:

- `Yuukei/Assets/Scripts/Runtime/PackageManager.cs`
- `Yuukei/Assets/Scripts/Runtime/ResidentAppController.cs`
- `Yuukei/Assets/Scripts/Runtime/PluginLoader.cs`

### 7.7 save/load

担当クラス:

- `PersistenceStore`
- `ResidentAppController`
- `SettingsWindow`
- `YuukeiVariableStore`
- `WindowsDesktopAdapter`

流れ:

- 起動時 `LoadAsync()`
- runtime 中 `RequestSave()`
- 終了時 `SaveImmediately()`
- API key は secure file へ別保存

確認できた事実:

- キャラ位置は save 対象に見当たらない
- active package, behavior flags, overrides, variables は保存対象

まず見るべきファイル:

- `Yuukei/Assets/Scripts/Runtime/PersistenceStore.cs`
- `Yuukei/Assets/Scripts/Runtime/ResidentAppController.cs`
- `Yuukei/Assets/Scripts/Runtime/YuukeiVariableStore.cs`

### 7.8 busy / フルスクリーン / 特定アプリ回避

担当クラス:

- `WindowsDesktopAdapter`
- `InputContextMonitor`
- `MascotRuntime`

流れ:

- `WindowsDesktopAdapter.GetForegroundWindowHandle()` / `IsForegroundWindowFullscreen()`
- `InputContextMonitor.RecalculateAllowedDisplays()`
- `MascotRuntime.SetDesktopContext(...)`
- `MascotRuntime.Tick()` で許可外ディスプレイなら退避

確認できた事実:

- fullscreen 回避は一部動いている
- 特定アプリ blacklist / whitelist 回避はコード上未確認
- busy score は算出されるが移動制御には未接続

まず見るべきファイル:

- `Yuukei/Assets/Scripts/Runtime/InputContextMonitor.cs`
- `Yuukei/Assets/Scripts/Runtime/WindowsDesktopAdapter.cs`
- `Yuukei/Assets/Scripts/Runtime/MascotRuntime.cs`

## 8. 未実装 / 仮実装 / 死んでいるコード

### 8.1 仕様上の期待に対して未完成と見える箇所

- `MascotRuntime` の自律移動・busy 抑制
- 特定アプリ利用中ディスプレイ回避
- package `assets` の実利用経路
- settings 上での override 編集
- DLL 承認の永続化と承認直後アクティベート
- Daihon named arguments の組み込み関数反映
- condition-only scene の常時監視

### 8.2 使われていない可能性が高いコード

- `RuntimeEventData.cs`
- `AllowedDisplaysChanged` event
- `PersistenceStore.SetOverrides()`
- `PanelSettings.asset`
- top-level `Daihon/Unity/*`, `Daihon/src/*` の Unity runtime 直結コード

### 8.3 active path から外れている可能性があるもの

- `YuukeiBootstrap` 自体は active だが、Scene に controller が存在する通常ケースでは実質フォールバック専用
- `WindowsDesktopAdapter.OpenUrl()` は参照が見つからず、要確認
- `VRMShaderPreloader` はビルド補助用と思われるが、runtime logic では未参照

### 8.4 将来整理候補

- `ResidentAppController` の責務集中
- `MascotRuntime` の巨大化
- `SettingsWindow` の runtime UI 構築コード肥大化
- `PluginLoader` の承認状態管理を save / secure storage に移す必要
- `Daihon.dll` と repo 内 `Daihon/src` の二重管理状態

## 9. 実装依頼のための注意点

今後 Codex / ChatGPT に依頼するとき、最初に共有した方がよい注意点を以下にまとめます。

1. 現行 active scene は `Assets/Scenes/SampleScene.unity` のみで、runtime の主要コンポーネントは Scene 直置きではなく `ResidentAppController.BuildRuntimeObjects()` で動的生成される。
2. 起動と常時更新の中心は `ResidentAppController` で、`Update()` から `InputContextMonitor.Tick()` と `MascotRuntime.Tick()` が毎フレーム呼ばれる。
3. キャラクター位置の single source は `MascotRuntime._desktopPosition`。`_root.transform.position` と `VisualRoot.localPosition/localRotation` は別責務なので混ぜない。
4. `MascotRuntime.LateUpdate()` が毎フレーム `VisualRoot.localPosition/localRotation` を上書きするため、見た目の揺れや姿勢変更を足すときは競合注意。
5. 入力は Unity UI Event ではなく `InputContextMonitor` が `Mouse.current` を直接ポーリングしている。クリック、ダブルクリック、ドラッグ、アイドル、periodic_tick はここが入口。
6. Daihon event は `ResidentAppController.OnRuntimeEventRaised()` -> `DaihonBridge.RaiseEventAsync()` -> FIFO queue -> `DaihonScriptRuntime.RunEventAsync()` の順で流れる。
7. alias 解決は `AliasRegistry` に集中している。イベント名・関数名を追加するときはここを通す。
8. 永続状態の single source は `PersistenceStore`。`save.json` 形状を変える変更は影響が大きい。
9. API key は `save.json` ではなく `WindowsDesktopAdapter` の secure file 経由で保存される。
10. settings UI は `SettingsWindow` が UI Toolkit を runtime 構築しており、Scene 上の `PanelSettings.asset` は active path ではない。
11. package 切替時は `ResidentAppController.ApplyCurrentPackageAsync()` が VRM / motions / aliases / daihon / bubble / DLL scan をまとめて切り替える。
12. DLL 承認フローは未完成気味で、`PluginLoader._approvedPaths` は永続化されていない。ここを触る実装は特に慎重に。
13. busy score は `InputContextMonitor` が持つが、現状 `MascotRuntime` で使っていない。仕様書どおりと思い込まない方がよい。
14. fullscreen 回避は一部実装済みだが、特定アプリ回避は未確認。関連改修では `WindowsDesktopAdapter` と `InputContextMonitor.RecalculateAllowedDisplays()` を先に確認する。
15. `Daihon.dll` を使う active path と、repo 内 `Daihon/src` のソースが分かれているため、文法・runtime を触るときは「どちらが実際にビルドへ入っているか」を先に確認する。

## 10. 変更頻度の高そうなホットスポット

### 10.1 `ResidentAppController`

理由:

- 起動、Scene 接続、runtime object 生成、package 適用、保存、settings、tray command まで一手に持つ

波及:

- ここを触ると起動不能、参照切れ、settings 破損、package 切替不整合が同時に起きうる

### 10.2 `MascotRuntime`

理由:

- 位置、表示、VRM、motion、expression、drag、小物、LateUpdate 見た目補正が集まっている

波及:

- transform 競合、motion 競合、visible state 競合が起きやすい

### 10.3 `InputContextMonitor`

理由:

- マウス入力、idle、periodic、file drop、display 許可判定が集約

波及:

- 入力仕様をいじると Daihon event、drag、busy 算出、fullscreen 回避へ一気に波及

### 10.4 `DaihonBridge` と `DaihonFunctionDispatcher`

理由:

- イベント配送・alias 解決・組み込み関数実行の中心

波及:

- 関数追加やキャンセル仕様変更が、吹き出し、選択 UI、永続変数、モーションに連鎖する

### 10.5 `PersistenceStore`

理由:

- 保存形式の single source

波及:

- ここを変えると package 状態、behavior 設定、永続変数、将来 migration に影響

### 10.6 `SettingsWindow`

理由:

- 見た目は UI だが、package 操作・API key・behavior 設定・DLL 承認の操作面が集まる

波及:

- UI 改修でも backend 呼び出し経路を壊しやすい

## 付録: 次回以降、Codex に実装依頼するときに最初に貼る短い要約

```text
現行の active scene は Assets/Scenes/SampleScene.unity のみです。
ランタイムの中心は YuukeiRuntime 上の ResidentAppController で、Awake で runtime object を動的生成し、Start -> InitializeAsync で各サービスを組み立てています。
毎フレームの入口は ResidentAppController.Update() で、WindowsDesktopAdapter.Tick(), InputContextMonitor.Tick(), MascotRuntime.SetDesktopContext(), MascotRuntime.Tick() が呼ばれます。
入力は Unity UI イベントではなく InputContextMonitor が Mouse.current を直接ポーリングして処理しています。
クリック、ダブルクリック、ドラッグ、idle、periodic_tick、file_drop は InputContextMonitor から canonical event として上がります。
イベント配送は ResidentAppController.OnRuntimeEventRaised() -> DaihonBridge.RaiseEventAsync() -> FIFO queue -> DaihonScriptRuntime.RunEventAsync() です。
alias 解決は AliasRegistry に集中しています。イベント名や関数名追加時はここを通してください。
永続状態の single source は PersistenceStore です。save.json の形状変更は要注意です。
API key は save.json ではなく WindowsDesktopAdapter の secure file に保存しています。
アクティブ package は PackageManager が解決し、ResidentAppController.ApplyCurrentPackageAsync() が VRM / motions / bubble / aliases / daihon / DLL scan を一括適用しています。
キャラクター位置の single source は MascotRuntime._desktopPosition です。
MascotRuntime は _root.transform.position を world 側、VisualRoot.localPosition/localRotation を見た目の揺れ用 local 側として別々に更新しています。
VisualRoot.localPosition/localRotation は MascotRuntime.LateUpdate() が毎フレーム上書きします。transform 競合に注意してください。
現状、自律移動は未実装で、位置変更は主に drag・初期安全位置・禁止ディスプレイ退避のみです。
busy score は InputContextMonitor が持っていますが、現 MascotRuntime では未使用です。
fullscreen 回避は一部ありますが、特定アプリ回避は未確認です。
SettingsWindow は UI Toolkit を runtime 構築しており、Scene 上の PanelSettings.asset は active path ではありません。
DLL 承認フローは未完成で、PluginLoader._approvedPaths はメモリ保持のみ、承認直後の activate も未接続です。
Daihon の built-in 関数は DaihonFunctionDispatcher にあり、named args は現状ほぼ活きていません。
condition-only scene は常時監視ではなく、イベント発火時に評価される実装です。
大きな改修前には ResidentAppController, MascotRuntime, InputContextMonitor, DaihonBridge, PersistenceStore の5点を先に確認してください。
```
