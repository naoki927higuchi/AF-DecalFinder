# AF-DecalFinder 1.9.1 確認記録

実施：2026-11-02、Windows、.NET 9 Release。

## 自動テスト

|スイート|成功件数|
|---|---:|
|既存回帰|202|
|編集・保存|34|
|棚卸・UNDO|34|
|移行|26|
|直接画像編集|26|
|Ctrl選択反転|18|
|線幅・描画|106|
|統一矩形・フィルタ|40|
|新規キー・塗りつぶし|72|
|合計|558|

既存EnterパンとSpace無効化を前提とした期待値のみ更新。新規キー経路はLVのProcessCmdKey→OnKeyDown、繰返しKeyDown→KeyUpを実コントロールで確認。塗りは白背景の画像上で黒Alpha128・赤青Alpha90を画素測定し、同色重複・異色部分重複・青優先を検証した。両ビューで25/100/200%、塗りONの1～4px線幅も一致。元/UI座標・ヒット判定不変。

## 実画面

テムジン600dpiを viewer-checks/ui-191.jpg へコピーし、その新規キャッシュだけを操作。
- LV Enterで標準LabelEdit開始、1を51へ編集してEnter確定。そのままSpaceで貼付済みになり、黒表示を確認。
- 強調塗りON、検索51＋Enter。赤と青が重なる箇所は青、済み黒の上に青塗りを確認。選択解除時は赤を確認。
- Ctrl+Aの全選択と、Spaceで先頭1件の黒表示反転、選択集合・表示位置の維持を確認。
- 棚卸Spaceで未確認276→275、Ctrl+A/Ctrl+Enterで棚卸完了を確認。
- 終了・再起動で通常モード、前回画像、強調塗りONを復元。検証後は塗りOFFに戻した。

## イベント経路

従来：LV OnKeyDownのEnterは位置移動、Spaceは標準チェック一括変更を抑止。ItemActivate/KeyPress/ProcessDialogKeyの独自ハンドラなし。メインフォームAcceptButtonなし。NumberDialogはAcceptButton/CancelButtonあり。

変更：未編集中のLVだけProcessCmdKeyでEnter/Space/Ctrl+Space/棚卸Ctrl+Enterを消費し、共通OnKeyDownへ渡す。編集開始は既存F2と同じEditSelected。編集中は素通しし、標準エディタの確定/取消へ委ねる。通常SpaceはSelectedItems[0].Checkedだけを変更し、既存ItemCheck→BeforeDataChange→自動保存へ接続する。Ctrl+Spaceは選択のみ反転。KeyUpまで同じSpaceを再実行しない。検索TextBox、フォーム全体のEnter、NumberDialogには変更なし。

## 未確認

物理キー長押し、物理Ctrl付きダブルクリック/ドラッグ、別PC・モニタ構成変更、長時間利用。長押しはイベント反復、Ctrlジェスチャは既存自動回帰で補完。フローティングの再配置は実画面確認したが、新Enter/Spaceの別窓内の完了までの操作は自動回帰主体。8画像OCR再実行は今回省略（アルゴリズム変更なし）。テムジン検証コピーでは実OCR実行と再起動キャッシュ復元を確認。

OCR元データ・既存キャッシュスキーマ・Phase 1成果物・入力原本に変更なし。Settings.jsonへHighlightFill（bool、初期OFF）のみ追加し、既存値保持・安全保存・移行設定の対象とした。

## 新規テストログ

```text
PASS: Missing fill setting defaults OFF
PASS: Invalid fill setting defaults OFF
PASS: Preprocessed Space changes first selected only
PASS: Held Space cannot toggle twice
PASS: Space preserves viewport and selection
PASS: Space autosaves done state
PASS: Space is one undo transaction
PASS: New key presses each toggle once in both directions
PASS: Ctrl+Space toggles focused selection without checks
PASS: LV command preprocessing opens one editor for SelectedItems[0]
PASS: Cancel returns focus to LV
PASS: Enter with no selection is consumed without editing
PASS: Inventory Space reviews once and advances
PASS: Inventory Ctrl+Enter remains bulk review
PASS: Inventory Enter still edits
PASS: Fill setting instantly updates both views and saves
PASS: Fill save preserves unknown settings
PASS: Restart restores fill
PASS: Detail 0.25: OFF keeps black done fill
PASS: Detail 0.25: red alpha90 over black128 exactly once
PASS: Detail 0.25: blue excludes red even at partial overlap
PASS: Detail 0.25: blue alpha90 once without done
PASS: Detail 0.25: geometry and hit test unchanged
PASS: Detail 0.25: fill ON keeps 1px red outline
PASS: Detail 0.25: fill ON keeps 2px red outline
PASS: Detail 0.25: fill ON keeps 3px red outline
PASS: Detail 0.25: fill ON keeps 4px red outline
PASS: Detail 1: OFF keeps black done fill
PASS: Detail 1: red alpha90 over black128 exactly once
PASS: Detail 1: blue excludes red even at partial overlap
PASS: Detail 1: blue alpha90 once without done
PASS: Detail 1: geometry and hit test unchanged
PASS: Detail 1: fill ON keeps 1px red outline
PASS: Detail 1: fill ON keeps 2px red outline
PASS: Detail 1: fill ON keeps 3px red outline
PASS: Detail 1: fill ON keeps 4px red outline
PASS: Detail 2: OFF keeps black done fill
PASS: Detail 2: red alpha90 over black128 exactly once
PASS: Detail 2: blue excludes red even at partial overlap
PASS: Detail 2: blue alpha90 once without done
PASS: Detail 2: geometry and hit test unchanged
PASS: Detail 2: fill ON keeps 1px red outline
PASS: Detail 2: fill ON keeps 2px red outline
PASS: Detail 2: fill ON keeps 3px red outline
PASS: Detail 2: fill ON keeps 4px red outline
PASS: Overview 0.25: OFF keeps black done fill
PASS: Overview 0.25: red alpha90 over black128 exactly once
PASS: Overview 0.25: blue excludes red even at partial overlap
PASS: Overview 0.25: blue alpha90 once without done
PASS: Overview 0.25: geometry and hit test unchanged
PASS: Overview 0.25: fill ON keeps 1px red outline
PASS: Overview 0.25: fill ON keeps 2px red outline
PASS: Overview 0.25: fill ON keeps 3px red outline
PASS: Overview 0.25: fill ON keeps 4px red outline
PASS: Overview 1: OFF keeps black done fill
PASS: Overview 1: red alpha90 over black128 exactly once
PASS: Overview 1: blue excludes red even at partial overlap
PASS: Overview 1: blue alpha90 once without done
PASS: Overview 1: geometry and hit test unchanged
PASS: Overview 1: fill ON keeps 1px red outline
PASS: Overview 1: fill ON keeps 2px red outline
PASS: Overview 1: fill ON keeps 3px red outline
PASS: Overview 1: fill ON keeps 4px red outline
PASS: Overview 2: OFF keeps black done fill
PASS: Overview 2: red alpha90 over black128 exactly once
PASS: Overview 2: blue excludes red even at partial overlap
PASS: Overview 2: blue alpha90 once without done
PASS: Overview 2: geometry and hit test unchanged
PASS: Overview 2: fill ON keeps 1px red outline
PASS: Overview 2: fill ON keeps 2px red outline
PASS: Overview 2: fill ON keeps 3px red outline
PASS: Overview 2: fill ON keeps 4px red outline
PASS: 72 final UI checks
```
