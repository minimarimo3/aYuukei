# Yuukei Unity MCP 実装指示プロンプト

```text
あなたは Unity 実装エージェントです。
以下の仕様に従って、Windows 向け MVP の Unity プロジェクト実装を行ってください。

# プロジェクト名
Yuukei

# 目的
Yuukei は、Unity と VRM を基盤として動作する 3D デスクトップマスコットであり、PC の中にキャラクターが住み着いているように感じられる体験を提供する居住型キャラクター体験の実行基盤です。
MVP では、Windows 向け常駐型デスクトップマスコットとして成立させてください。

# 固定前提
- Unity 6000.3.6f1
- URP
- 非同期処理は UniTask
- 透明ウィンドウ / 前面表示制御は UniWindowController
- MVP の主対象は Windows
- 将来の macOS / Android / iOS / Linux 対応を壊さない抽象化は歓迎

# コア体験
- キャラクターはデスクトップ上をふわふわ漂う
- マルチモニタを連続空間として移動できる
- フルスクリーンアプリや特定アプリ使用中ディスプレイには侵入しない
- ユーザーが忙しいときは移動頻度または移動距離を抑える
- 吹き出しは 1 つだけ表示し、新しい発話で上書きする
- キャラクタークリックで設定画面を開いてはいけない
- 設定や終了は常在アイコンまたはショートカットキーから行う

# MVP で必須のイベント
Canonical 名:
- app_started
- character_clicked
- character_double_clicked
- character_drag_started
- character_drag_ended
- file_dropped
- idle_reached
- periodic_tick

# MVP で必須のイベント alias
- 起動時 -> app_started
- クリック -> character_clicked
- ダブルクリック -> character_double_clicked
- ドラッグ開始 -> character_drag_started
- ドラッグ終了 -> character_drag_ended
- ファイルドロップ -> file_dropped
- 放置 -> idle_reached
- 定期発火 -> periodic_tick

# イベント alias 方針
- 内部 canonical 名は英語 snake_case で固定
- Daihon の合図に書かれた生のイベント名は alias 解決を通して canonical に正規化する
- package manifest から event alias を追加登録できるようにする
- canonical 名そのものも受理する
- alias 解決失敗時はイベント不一致扱いでよい
- ログには raw 名と canonical 名を出せるようにする

# イベントコンテキスト
Daihon へ一時変数として公開すること。変数名は _event_ 接頭辞。

character_clicked:
- _event_x
- _event_y
- _event_character_id

character_double_clicked:
- _event_x
- _event_y
- _event_character_id

character_drag_started:
- _event_x
- _event_y
- _event_character_id

character_drag_ended:
- _event_x
- _event_y
- _event_character_id

file_dropped:
- _event_file_name
- _event_file_extension
- _event_file_kind
- _event_character_id

idle_reached:
- _event_idle_seconds

periodic_tick:
- _event_timestamp
- _event_session_elapsed_seconds

app_started:
- _event_is_first_launch
- _event_active_package_id

# ファイル種別分類
_event_file_kind は以下のいずれか
- image
- audio
- video
- text
- document
- archive
- model
- folder
- other

拡張子分類:
image: png jpg jpeg gif bmp webp tif tiff
audio: mp3 wav ogg flac m4a aac
video: mp4 mov avi mkv webm wmv
text: txt md json yaml yml xml csv log
document: pdf doc docx xls xlsx ppt pptx
archive: zip 7z rar tar gz
model: vrm glb gltf fbx obj

# MVP で必須の関数
Canonical 名:
- show_dialog(text)
- set_expression(name)
- play_motion(name)
- set_prop_visible(name, visible)
- show_choices(...)
- set_persistent(key, value)

# 関数 alias
- 吹き出し表示 -> show_dialog
- 表情変更 -> set_expression
- モーション再生 -> play_motion
- 小物表示 -> set_prop_visible
- 選択肢表示 -> show_choices
- 永続保存 -> set_persistent

# 関数 alias 方針
- Daihon から呼ばれた関数名は alias 解決後に canonical 関数へディスパッチ
- package manifest から function alias を追加登録できるようにする
- canonical 名そのものも受理する
- 未解決関数名は実行時エラー

# 関数仕様
show_dialog(text):
- 吹き出し表示
- 同時表示は 1 つまで
- 新しい発話で既存吹き出しを上書き
- await 不要

set_expression(name):
- 表情切替
- 未定義名は警告ログ + 継続

play_motion(name):
- モーション再生
- 競合時は最後に呼ばれたものを優先
- 未定義名は警告ログ + 継続

set_prop_visible(name, visible):
- 小物表示 / 非表示切替

set_persistent(key, value):
- Daihon 永続変数ストアに反映
- value は bool / number / string のみ

show_choices(...):
- Daihon には配列型がないため、可変長文字列引数として実装
- 1 個以上の文字列を受け取り、縦並びボタン UI で表示
- ユーザーが選んだ文字列を戻り値として返す
- 閉じる / Esc で空文字を返す
- await してイベント実行を停止する
- 0 引数は実行時エラー
- 非文字列引数は実行時エラー

# 実行モデル
- DaihonBridge は 1 度に 1 イベントのみ実行
- 実行中に別イベントが来たら FIFO キューへ積む
- periodic_tick のみ最新 1 件保持の coalescing を許可
- await を伴う標準関数は show_choices のみ
- キャンセル条件:
  - 一時無効化
  - 完全終了
  - パッケージ切替
  - Daihon ランタイム再初期化
  - キャラクター破棄
- キャンセル時は現在イベントの残りを中断し、未実行キューは破棄してよい
- 既に保存済みの永続変数は巻き戻さない

# パッケージ仕様
インストール先:
Application.persistentDataPath/package/{creator}-{version}-{guid}/

予約ファイル:
- manifest.json
- character.vrm

manifest.json 形式:
{
  "creator": "hoge",
  "version": "v0.0.1",
  "download": "https://example.com/latest",
  "license": "hoge",
  "id": "00000000-0000-0000-0000-000000000000",
  "character": "character.vrm",
  "daihon": "Scripts/main.daihon",
  "textures": {
    "speechBubble": {
      "background": "Textures/speech_bubble_bg.png",
      "tail": "Textures/speech_bubble_tail.png"
    }
  },
  "assets": [
    "Props/item_01.prefab",
    "Props/item_02.prefab"
  ],
  "motions": [
    "Motions/wave.vrma",
    "Motions/idle.vrma"
  ],
  "dlls": [
    "Plugins/example.dll"
  ],
  "aliases": {
    "events": {
      "起動時": "app_started"
    },
    "functions": {
      "吹き出し表示": "show_dialog"
    }
  }
}

ルール:
- すべて相対パス
- 壊れた要素のみスキップして継続
- DLL は自動ロードしない
- alias は manifest から追加登録可能

# 保存仕様
save.json:
{
  "activePackageId": "00000000-0000-0000-0000-000000000000",
  "overrides": {
    "daihon": "",
    "character": "",
    "textures": {},
    "assets": {},
    "motions": {}
  },
  "persistentVariables": {
    "valName": "valValue"
  },
  "appState": {
    "isTemporarilyDisabled": false,
    "isTemporarilyHidden": false,
    "shortcutConfig": {
      "toggleDisabled": "Ctrl+;",
      "toggleHidden": "Ctrl+Shift+;"
    }
  }
}

ルール:
- 保存先は Application.persistentDataPath/save.json
- API キーは OS のセキュアストア
- パッケージ切替時は overrides を初期化
- 一時変数、キャラクター位置、吹き出し状態、実行中選択肢状態、再生中モーション状態は保存しない
- persistentVariables は bool / number / string のみ

# 状態管理
一時無効化:
- 常駐継続
- キャラクター表示は残ってよい
- 反応 / 台本実行 / 自発動作停止

一時非表示:
- 常駐継続
- キャラクター描画停止
- 再表示可能

完全終了:
- 永続保存後に終了

# 設定画面
単一ウィンドウ + サイドバー
項目:
- 外見と振る舞い
- パッケージ
- マーケットプレイス
- 連携
- 動作設定
- About

外見と振る舞い:
- 台本
- VRM
- 小物
- テクスチャ
- ロード済みモーション一覧
- 各項目は「パッケージ準拠 / 個別指定」を持つ

パッケージ:
- 現在有効パッケージ表示
- インストール済み一覧
- 切替
- ローカルインポート
- 削除

連携:
- 他デバイス連携のプレースホルダ
- API キー入力
- 外部送信の説明

動作設定:
- 一時無効化 / 一時非表示の説明
- ショートカットキー設定
- 起動時挙動
- 基本演出設定

About:
- アプリ名
- バージョン
- ライセンス
- クレジット

UI 方針:
- 即時適用でよい
- Apply / Cancel は必須でない
- 選択肢 UI は簡素でよい
- DLL 画面には危険性警告と明示確認 UI を置く

# 初回起動
1. スプラッシュ
2. デフォルトキャラ出現
3. デフォルト Daihon 有効化
4. Daihon ベースのチュートリアル開始

# 実装すべき主要コンポーネント
- ResidentAppController
- MascotRuntime
- DaihonBridge
- InputContextMonitor
- PackageManager
- PersistenceStore
- SettingsWindow
- PluginLoader
- TutorialBootstrap
- AliasRegistry

# 推奨 API
UniTask RaiseEventAsync(string eventName, IReadOnlyDictionary<string, object> context, CancellationToken ct);
void RegisterFunction(string canonicalName, Func<DaihonValue[], CancellationToken, UniTask<DaihonValue?>> fn);
void RegisterFunctionAlias(string alias, string canonicalName);
void RegisterEventAlias(string alias, string canonicalName);
bool TryResolveEventName(string rawName, out string canonicalName);
bool TryResolveFunctionName(string rawName, out string canonicalName);

# 完了条件
- Windows 上で常駐起動できる
- デフォルトキャラが透明ウィンドウ上に表示される
- 主要イベントが Daihon に届く
- canonical 英語名 / 日本語 alias のどちらでも同じイベント・関数として動く
- show_choices が await と戻り値を伴って動く
- パッケージ切替 / 個別差し替え / 保存 / 復元が動く
- 一時無効化 / 一時非表示 / 完全終了が動く
- DLL 警告導線がある
- 設定画面骨格がある
- フルスクリーン / 特定アプリ使用中ディスプレイ回避が働く
- 忙しさに応じて移動抑制が働く

この仕様に沿って、実装しやすいクラス分割と責務分離を保ちながら Unity プロジェクトを構築してください。
不足がある場合は仕様の意図を壊さない最小限のデフォルトを採用し、canonical 名と alias 解決の拡張性を優先してください。
```
