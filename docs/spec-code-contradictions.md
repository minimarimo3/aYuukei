# 仕様書 vs 実装コード 矛盾点一覧

`docs/yuukei_spec/` の仕様と現在の Unity C# コードを比較し、検出された矛盾・乖離を以下にまとめる。

---

## 1. 起動シーケンスの順序が仕様と逆

**仕様 (02_キャラクター体験とユーザー接点.md)**
> 1. スプラッシュ表示 → 2. デフォルトキャラ登場 → 3. デフォルト台本有効化 → 4. キャラ主導チュートリアル

**実装 (ResidentAppController.cs:InitializeAsync)**
1. パッケージ読み込み・キャラクター表示 (`ApplyCurrentPackageAsync`)
2. スプラッシュ表示 (`ShowSplashAsync`) ※初回起動時のみ
3. `app_started` イベント発火

**矛盾点:**
- スプラッシュがキャラクター読み込みの**後**に表示される（仕様ではスプラッシュが先）
- スプラッシュが**初回起動時のみ**表示される（仕様では毎回の起動シーケンスに含まれている）

---

## 2. 一時無効化時に自律移動が停止しない

**仕様 (03_UIと状態管理.md - 一時無効化)**
> 常駐は継続 / 表示は継続しうる / **反応・台本実行・自律移動は停止** / 状態は保存

**実装 (ResidentAppController.cs:SetTemporarilyDisabledAsync)**
- `DaihonBridge.SetTemporarilyDisabled(true)` → 台本実行停止 ✓
- `InputContextMonitor.SetInputEnabled(false)` → 入力反応停止 ✓
- `MascotRuntime` の Tick / ApplyFloatingPose は**継続**される → **自律移動は停止しない** ✗

**矛盾点:**
- `MascotRuntime.Tick()` は `ResidentAppController.Update()` で無条件に呼ばれ続け、キャラクターのふわふわ浮遊アニメーションが継続する。仕様が要求する「自律移動の停止」が未実装。

---

## 3. ショートカット設定に仕様外の項目がある

**仕様 (04_パッケージと保存仕様.md - save.json)**
```json
"shortcutConfig": {
  "toggleDisabled": "Ctrl+;",
  "toggleHidden": "Ctrl+Shift+;"
}
```

**実装 (DataModels.cs:ShortcutConfigData)**
```csharp
public string ToggleDisabled = "Ctrl+;";
public string ToggleHidden = "Ctrl+Shift+;";
public string OpenSettings = "Ctrl+Alt+;";   // ← 仕様に存在しない
```

**矛盾点:**
- `openSettings` ショートカットが仕様の `shortcutConfig` 定義に含まれていない。仕様では設定画面へのアクセスはシステムトレイアイコンのみとされている。

---

## 4. トレイメニューの「再表示」が独立項目でない

**仕様 (02_キャラクター体験とユーザー接点.md)**
> トレイから可能な操作: 設定、一時無効化、一時非表示、**再表示**、終了

**実装 (WindowsNativeShellHost.cs:ShowContextMenu)**
```
設定 / 一時無効化 or 再有効化 / 一時非表示 or 再表示 / 終了
```

**矛盾点:**
- 仕様では「再表示」が独立した操作として列挙されているが、実装では「一時非表示」と「再表示」がトグルとして1つのメニュー項目に統合されている。同様に「一時無効化」と「再有効化」もトグル統合されている。仕様の意図がトグルなのか独立項目なのか確認が必要。

---

## 5. periodic_tick のキュー処理優先度

**仕様 (05_Daihon実行契約.md)**
> 実行モデル: 1イベントずつ / 並行実行なし / **FIFOキュー** / periodic_tick合体: 実行中・キュー済みなら最新1件だけ保持

**実装 (DaihonBridge.cs:ProcessQueueAsync)**
```csharp
if (_queue.Count > 0) {
    next = _queue[0];       // 通常イベントを先に処理
    _queue.RemoveAt(0);
} else if (_coalescedPeriodicTick != null) {
    next = _coalescedPeriodicTick;  // キューが空になってから処理
    _coalescedPeriodicTick = null;
}
```

**矛盾点:**
- `periodic_tick` は通常キューとは別に管理され、通常キューが**すべて空になった後**にのみ処理される。仕様の「FIFOキュー」とは異なり、periodic_tick は常に最低優先度となる。仕様通りの FIFO 順序（到着順）で処理されない。

---

## 6. TutorialBootstrap のチュートリアル初期データ未実装

**仕様 (06_Unity実装構成と受け入れ条件.md - TutorialBootstrap)**
> 責務: 初回起動判定 / デフォルトパッケージ導入 / **チュートリアル初期データセットアップ**

**実装 (TutorialBootstrap.cs)**
- 初回起動判定（save.json の有無）✓
- スターターパッケージ導入 ✓
- チュートリアル初期データのセットアップ ✗（該当コードなし）

**矛盾点:**
- チュートリアル用の初期データ（永続変数やフラグ等）のセットアップ処理が存在しない。

---

## 7. 設定画面中のイベント抑制が仕様に明記されていない

**実装 (ResidentAppController.cs:OnRuntimeEventRaised)**
```csharp
if (_isSettingsVisible) return;  // 設定画面表示中はイベントを全て無視
```

**矛盾点:**
- 設定画面表示中にすべてのランタイムイベント（idle_reached, periodic_tick 含む）が無視されるが、この挙動は仕様に明記されていない。一時無効化や一時非表示の状態定義には含まれているが、「設定画面表示中」の動作仕様が不明確。

---

## 8. _event_is_first_launch の型

**仕様 (05_Daihon実行契約.md - app_started コンテキスト)**
> `_event_is_first_launch` （型について明記なし）

**実装 (ResidentAppController.cs:RaiseAppStartedAsync)**
```csharp
["_event_is_first_launch"] = firstLaunch,  // bool
```

**備考:** 仕様では `_event_is_first_launch` の型が明示されていない。実装は `bool` だが、台本変数は `bool / number / string` を扱うため問題にはならないものの、仕様に型の明記がない点は曖昧。

---

## 9. DaihonBridge の推奨 API シグネチャとの差異

**仕様 (06_Unity実装構成と受け入れ条件.md - 推奨 C# API)**
```csharp
void RegisterFunction(
    string canonicalName,
    Func<DaihonValue[], CancellationToken, UniTask<DaihonValue?>> fn);
```

**実装 (Contracts.cs)**
```csharp
public delegate UniTask<DaihonValue?> CanonicalFunctionDelegate(
    DaihonValue[] args, CancellationToken cancellationToken);
```

**備考:** 機能的には同等（delegate vs Func）だが、仕様は `Func<>` を推奨している。実質的な影響はないが厳密には異なる。

---

## 10. 一時非表示時の入力無効化が仕様になし

**仕様 (03_UIと状態管理.md - 一時非表示)**
> 常駐は継続 / **描画停止** / 再表示可能 / フォーカス時・ゲーム中向け

**実装 (ResidentAppController.cs:SetTemporarilyHiddenAsync)**
```csharp
_mascotRuntime.SetVisible(false);         // 描画停止 ✓
_speechBubbleController.Hide();           // 吹き出し非表示
_inputContextMonitor.SetInputEnabled(false);  // ← 入力も無効化
```

**矛盾点:**
- 仕様では一時非表示は「描画停止」のみで、反応・台本実行の停止は明記されていない（それは「一時無効化」の責務）。しかし実装では入力監視も無効化されている。一時非表示で台本実行（idle_reached, periodic_tick等）も止まるべきか否かが仕様上不明確。

---

## 11. 動的変数（時刻系）の仕様記載なし

**実装 (YuukeiVariableStore.cs:RegisterBuiltinTimeVariables)**
- `年`, `月`, `日`, `時`, `分`, `秒`, `ミリ秒`, `曜日`, `週` の動的変数が登録されている

**矛盾点:**
- これらの時刻系動的変数は `docs/yuukei_spec/` のいずれの仕様書にも記載されていない。05_Daihon実行契約.md の「MVP非標準関数」に「現在時刻」が挙げられているが、関数ではなく変数として実装されている。

---

## 12. 表情パレット（色対応）の仕様記載なし

**実装 (MascotRuntime.cs)**
- `ExpressionPalette` として表情名→色のマッピングが定義されている（default=ベージュ, happy=オレンジ, sad=青, angry=赤, surprised=紫）
- プレースホルダーキャラクターのマテリアル色として使用

**矛盾点:**
- 仕様にはキャラクター表情と色の対応関係について記載がない。VRM の表情切り替え機能のみが仕様に定義されている。

---

## まとめ

| # | 矛盾 | 重要度 |
|---|------|-------|
| 1 | 起動シーケンス順序（スプラッシュ→キャラの順が逆・初回のみ） | 高 |
| 2 | 一時無効化時に自律移動が停止しない | 高 |
| 3 | openSettings ショートカットが仕様外 | 中 |
| 4 | トレイメニューの「再表示」が独立項目でない | 低 |
| 5 | periodic_tick のキュー処理が FIFO でなく最低優先 | 中 |
| 6 | チュートリアル初期データ未実装 | 中 |
| 7 | 設定画面中のイベント抑制が仕様に明記なし | 低 |
| 8 | _event_is_first_launch の型が仕様に未明記 | 低 |
| 9 | RegisterFunction のシグネチャ差異（delegate vs Func） | 低 |
| 10 | 一時非表示時の入力無効化が仕様に明記なし | 中 |
| 11 | 時刻系動的変数が仕様に未記載 | 中 |
| 12 | 表情パレット色対応が仕様に未記載 | 低 |
