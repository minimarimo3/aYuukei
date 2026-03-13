# Yuukei 完成版実装仕様書 v1.2
## 06. Unity実装構成と受け入れ条件

## 20. Unity 実装単位

### 20.1 `ResidentAppController`

責務:

- 起動シーケンス
- 常在アイコン管理
- 完全終了
- 一時無効化 / 一時非表示 / 再表示 / 再有効化
- グローバル状態遷移

### 20.2 `MascotRuntime`

責務:

- VRM ロード
- 画面描画
- ふわふわ移動
- マルチモニタ空間移動
- 吹き出し表示
- 口パク連携
- モーション再生
- 表情切替
- 小物表示管理
- 忙しさ反映

### 20.3 `DaihonBridge`

責務:

- イベント発火
- alias 解決
- 複数 Daihon への順序付きイベント配送
- Daihon 実行要求
- 関数公開
- 実行結果受け取り
- 永続変数更新

### 20.4 `InputContextMonitor`

責務:

- クリック / ダブルクリック / ドラッグ検出
- 放置判定
- 定期発火
- キー入力 / マウス入力頻度監視
- フルスクリーン検知
- 特定アプリ使用中検知
- ファイル D&D 種別判定

### 20.5 `PackageManager`

責務:

- パッケージインポート
- 有効パッケージ切替
- 複数 Daihon の順序付き解決
- 個別差し替え適用
- 切替時の個別差し替え破棄
- 破損要素スキップと警告
- package alias 読み込み

### 20.6 `PersistenceStore`

責務:

- 保存対象読み書き
- 再起動復元
- Daihon 永続変数との整合維持

### 20.7 `SettingsWindow`

責務:

- サイドバー式設定 UI
- 各設定画面切替
- ローカルインポート
- API キー入力
- 警告文表示

### 20.8 `PluginLoader`

責務:

- DLL 候補検出
- 警告 UI 表示
- 保留状態管理
- 明示確認後ロード
- 失敗時通知

### 20.9 `TutorialBootstrap`

責務:

- 初回起動判定
- デフォルトパッケージ導入
- チュートリアル初期データ準備

### 20.10 `AliasRegistry`

責務:

- 組み込み alias 保持
- package alias 統合
- event / function 名の canonical 解決
- 衝突時警告
- デバッグ用解決ログ支援

---

## 21. 推奨 C# API

```csharp
UniTask RaiseEventAsync(string eventName, IReadOnlyDictionary<string, object> context, CancellationToken ct);

void RegisterFunction(
    string canonicalName,
    Func<DaihonValue[], CancellationToken, UniTask<DaihonValue?>> fn);

void RegisterFunctionAlias(string alias, string canonicalName);

void RegisterEventAlias(string alias, string canonicalName);

bool TryResolveEventName(string rawName, out string canonicalName);

bool TryResolveFunctionName(string rawName, out string canonicalName);
```

---

## 22. 受け入れ条件

MVP 完了条件は以下。

1. Windows 上で常駐起動できる  
2. デフォルトキャラが透明ウィンドウ上に表示される  
3. クリック / ダブルクリック / ドラッグ開始 / ドラッグ終了 / ファイル D&D / 放置 / 定期発火が Daihon に届く  
4. canonical 英語名でも、日本語 alias でも同じイベント・関数として実行できる  
5. 吹き出しは常に 1 つだけ表示される  
6. `show_choices` が await と戻り値を伴って動作する  
7. パッケージ切替と個別差し替えができる  
8. `save.json` とセキュアストアが仕様どおり機能する  
9. 一時無効化 / 一時非表示 / 完全終了が成立する  
10. DLL 警告導線が存在する  
11. 設定画面骨格が存在する  
12. フルスクリーン / 特定アプリ使用中ディスプレイ回避が成立する  
13. 忙しさに応じて移動抑制が働く  
14. 破損要素のみスキップして継続できる
15. 吹き出し background texture はアスペクト比を維持して表示される
16. 吹き出しテキストのフォントサイズと文字色は外部から変更可能で、既定は 24pt の黒文字である
