# Yuukei 完成版実装仕様書 v1.2
## 05. Daihon実行契約

## 13. Daihon 実行契約

### 13.1 方針

Unity 側は Daihon 言語仕様そのものを設計しない。  
実装対象は以下。

- イベント発火源
- 関数実装
- 永続化基盤
- 実行時表示制御
- alias 解決層

### 13.1.1 複数 Daihon の扱い

- パッケージおよび override では複数の Daihon を保持できる
- 有効な Daihon 集合は manifest または save.json の override から決定する
- Daihon は配列順にロードし、同一イベントは有効な全 Daihon へその順で配送する
- ある Daihon が await を伴って停止した場合、後続 Daihon の実行はその完了後に続行する
- 永続変数ストアと alias レジストリは有効 Daihon 群で共有する
- 壊れた Daihon は個別に警告してスキップし、ほかの Daihon 実行は継続する

### 13.2 Canonical 名と別名の両立

MVP では、**内部処理・ログ・保存・デバッグでは canonical 英字識別子を使用する**。  
ただし Daihon 側で記述されるイベント名 / 関数名は、**alias 解決層を通して canonical 名へ正規化** してから実行する。

### 13.3 Alias 解決の目的

- 英語 canonical を内部契約として安定化する
- Daihon 側では日本語や他言語の別名を差し込めるようにする
- 後からパッケージ単位・ロケール単位で別名追加できるようにする
- 「翻訳して処理する」のではなく「識別子の別名として解決する」方式を採る

### 13.4 Alias 解決対象

- `合図:` に書かれるシステムイベント名
- `＜関数名 ...＞` に書かれる関数名

変数名・シーン名・ラベル名には MVP では alias 解決を適用しない。

### 13.5 Alias レジストリ

実行時に以下 2 種類の辞書を持つ。

- `EventAliasRegistry`
- `FunctionAliasRegistry`

どちらも構造は **alias -> canonical** の辞書とする。

### 13.6 Alias レイヤーの優先順位

1. package manifest 内 `aliases`
2. アプリ組み込み alias
3. canonical 名そのもの

### 13.7 Alias 正規化ルール

- 完全一致で解決する
- 前後空白は trim する
- ASCII 英字は大文字小文字を区別しない
- 日本語など non-ASCII はそのまま比較する
- 1 alias が複数 canonical に対応してはならない
- 衝突時は package alias を優先し、警告ログを残す

### 13.8 未解決時

- イベント名未解決: その `合図:` は一致しないものとして扱う
- 関数名未解決: 実行時エラー
- デバッグログには入力名と canonical 解決結果を出してよい

---

## 14. MVP 標準イベント

### 14.1 Canonical event names

- `app_started`
- `character_clicked`
- `character_double_clicked`
- `character_drag_started`
- `character_drag_ended`
- `file_dropped`
- `idle_reached`
- `periodic_tick`

### 14.2 組み込み日本語 alias

- `起動時` -> `app_started`
- `クリック` -> `character_clicked`
- `ダブルクリック` -> `character_double_clicked`
- `ドラッグ開始` -> `character_drag_started`
- `ドラッグ終了` -> `character_drag_ended`
- `ファイルドロップ` -> `file_dropped`
- `放置` -> `idle_reached`
- `定期発火` -> `periodic_tick`

### 14.3 記述例

```daihon
### 起動時の挨拶
合図: ＠app_started
「こんにちは」
```

```daihon
### 起動時の挨拶
合図: ＠起動時
「こんにちは」
```

どちらも同じ canonical イベント `app_started` として処理される。

### 14.4 MVP で標準実装しないイベント

- busy / idle 切替公開イベント
- 一時無効化 / 再有効化イベント
- 一時非表示 / 再表示イベント
- 時刻帯イベントの細分化
- アプリ別文脈イベント
- DLL 由来の高度イベント管理 UI

---

## 15. イベントコンテキスト公開規約

イベントコンテキストは、Daihon 実行時に **一時変数** として注入する。  
変数名はすべて `_event_` 接頭辞を持つ。

### 15.1 共通規則

- 一時変数として扱う
- イベント終了時またはアプリ終了時に破棄
- 座標系は仮想デスクトップ全体に対するスクリーン座標
- 単位はピクセル
- 文字列は Daihon の通常文字列として扱う

### 15.2 公開変数

#### `character_clicked`

- `_event_x`
- `_event_y`
- `_event_character_id`

#### `character_double_clicked`

- `_event_x`
- `_event_y`
- `_event_character_id`

#### `character_drag_started`

- `_event_x`
- `_event_y`
- `_event_character_id`

#### `character_drag_ended`

- `_event_x`
- `_event_y`
- `_event_character_id`

#### `file_dropped`

- `_event_file_name`
- `_event_file_extension`
- `_event_file_kind`
- `_event_character_id`

#### `idle_reached`

- `_event_idle_seconds`

#### `periodic_tick`

- `_event_timestamp`
- `_event_session_elapsed_seconds`

#### `app_started`

- `_event_is_first_launch`
- `_event_active_package_id`

---

## 16. Daihon に公開する標準関数

### 16.1 Canonical function names

- `show_dialog`
- `set_expression`
- `play_motion`
- `set_prop_visible`
- `show_choices`
- `set_persistent`

### 16.2 組み込み日本語 alias

- `吹き出し表示` -> `show_dialog`
- `表情変更` -> `set_expression`
- `モーション再生` -> `play_motion`
- `小物表示` -> `set_prop_visible`
- `選択肢表示` -> `show_choices`
- `永続保存` -> `set_persistent`

### 16.3 `show_dialog(text)`

- 引数: 文字列 1 つ
- 戻り値: なし
- 吹き出しを表示する
- 同時表示は 1 つまで
- 新規発話で既存吹き出しを上書き
- await せず次へ進んでよい

### 16.4 `set_expression(name)`

- 引数: 表情名
- 戻り値: なし
- 未定義名は警告ログ + 継続

### 16.5 `play_motion(name)`

- 引数: モーション名
- 戻り値: なし
- 競合時は最後に呼ばれたものを優先
- 未定義名は警告ログ + 継続

### 16.6 `set_prop_visible(name, visible)`

- 引数: 小物名、真偽値
- 戻り値: なし

### 16.7 `set_persistent(key, value)`

- 引数: 文字列 key、真偽値 / 数値 / 文字列 value
- 戻り値: なし
- Daihon 永続変数ストアへ反映
- 異なる型への再代入は実行時エラー

### 16.8 `show_choices(...)`

Daihon に配列型がないため、**可変長文字列引数** として実装する。

例:

```daihon
_返答=＜show_choices 「はい」 「いいえ」＞
```

```daihon
_返答=＜選択肢表示 「ついていく」 「ここにいる」 「またあとで」＞
```

仕様:

- 引数: 1 個以上の文字列
- 戻り値: 選ばれた選択肢ラベル文字列
- 閉じた場合の戻り値: `「」`
- await して現在イベント実行を停止する
- UI は単一選択のみ
- 0 引数は実行時エラー
- 非文字列引数は実行時エラー

### 16.9 MVP 非必須関数

- 効果音再生
- 現在時刻取得
- 高度なファイルメタ取得
- ランダム値取得
- 強制表示 / 非表示関数

---

## 17. 実行モデル・待機・キャンセル

### 17.1 実行単位

- `DaihonBridge` は 1 度に 1 イベントのみ実行
- 同時並列実行はしない
- 実行中に別イベントが到着した場合は FIFO キューに積む

### 17.2 `periodic_tick`

- 実行中または待機中に新規発火した場合、最新 1 件のみ保持する coalescing を許可する

### 17.3 await を伴う標準関数

- MVP では `show_choices` のみ

### 17.4 キャンセル条件

以下のいずれかで現在イベントをキャンセルしてよい。

- 一時無効化
- 完全終了
- パッケージ切替
- Daihon ランタイム再初期化
- 実行対象キャラクター破棄

### 17.5 キャンセル時の規則

- 現在イベントの残り処理は実行しない
- キュー上の未実行イベントは破棄してよい
- 既に保存済みの永続変数は巻き戻さない
- 一時変数は破棄する

### 17.6 エラー規則

- 未定義関数呼び出しは実行時エラー
- 引数型不正は実行時エラー
- 未定義表情 / 未定義モーション / 未定義小物は警告ログ + 継続
- `show_choices` の 0 引数は実行時エラー
- 未定義イベントコンテキスト変数参照は未定義変数参照エラー

---

## 18. ファイル種別分類ルール

`_event_file_kind` は以下のいずれか。

- `image`
- `audio`
- `video`
- `text`
- `document`
- `archive`
- `model`
- `folder`
- `other`

### 18.1 判定方法

- ディレクトリ: `folder`
- それ以外は拡張子ベース
- 判定不能は `other`

### 18.2 拡張子マップ

**image**  
png, jpg, jpeg, gif, bmp, webp, tif, tiff

**audio**  
mp3, wav, ogg, flac, m4a, aac

**video**  
mp4, mov, avi, mkv, webm, wmv

**text**  
txt, md, json, yaml, yml, xml, csv, log

**document**  
pdf, doc, docx, xls, xlsx, ppt, pptx

**archive**  
zip, 7z, rar, tar, gz

**model**  
vrm, glb, gltf, fbx, obj

### 18.3 補足

- 大文字小文字は区別しない
- 先頭の `.` は除去して判定
- 拡張子なしは `other`
