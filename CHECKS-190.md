# AF-DecalFinder 1.9.0 確認記録

実施日：2026-11-02。Windows / .NET 9 Release。作業データの自動テストは専用フォルダの設定・キャッシュで隔離し、実画面はRX-78 Ver.3.0のコピー ui-190.png を使用。

## 自動テスト

|スイート|成功件数|
|---|---:|
|既存回帰|202|
|編集・保存|34|
|統一矩形|40|
|Ctrl選択反転|18|
|画像直接編集|26|
|枠線幅・画素測定|106|
|8画像OCR統合|98|
|棚卸・UNDO|34|
|移行|26|
|合計|584|

全スイート成功。今回変更した通常編集後の選択維持と削除後の次候補選択は、旧仕様の期待値だけ更新した。OCR実測は [OCR-INTEGRATION-190.md](OCR-INTEGRATION-190.md)。Phase 1の181件は既存JSONとの比較を維持し、8原本のハッシュ不変も確認。

## 実画面

- メニュー → ツールバー → 検索欄 → 3ペインの配置を確認。初回確認で見つけたDock順序を修正。
- 357件で棚卸開始。Spaceで356件、F2でTEST190を確定して355件、Ctrl+Zで356件へ復元。次候補の青枠と右画像への移動を確認。
- Ctrl+A → Ctrl+Enterで棚卸完了・一覧空。Ctrl+Zで356件と複数選択を復元。
- 設定・移行のエクスポート画面で全チェック解除後、検証画像1件だけ選択。ZIP出力成功、画像原本を含まない旨の表示。
- 同じZIPをインポート。検索フォルダ未登録で読込1件・未紐付け1件を表示。
- ファイル選択で同じ画像を再読込し、既存キャッシュとの衝突ダイアログを表示。既定の「いいえ」で既存を保持し、OCRなしのキャッシュ復元を確認。
- 終了・再起動して通常モード、前回画像357件、UNDO無効を確認。棚卸開始で未確認356件を復元。
- Deleteで対象1件の確認ダイアログ、Esc取消を確認。

## 未確認・限界

実際の別PCへのファイル輸送、物理マルチモニタ/DPI切替、長時間・大量キャッシュ利用は未確認。Spaceキーの物理長押しとCtrlを保持したマウス操作は自動操作環境の制約があり、イベント経路・KeyUp/タイマー制御の自動テストで補完。移行の別パス/別日時/同名同寸法の異内容、複数候補、置換バックアップ、不正ZIPの既存データ保全は同一PCの隔離フォルダで確認した。

## 棚卸・UNDO 詳細ログ

```text
PASS: Legacy records default to unreviewed without mutation
PASS: Same-image reanalysis preserves review-only work and its fixed ID
PASS: Startup is normal mode
PASS: Inventory enters at first unreviewed candidate with one pan
PASS: Space reviews one; keeps done separate; next row, focus and one pan
PASS: Space autorepeat ignored until KeyUp
PASS: Undo review restores fixed ID selection and state
PASS: Undo is one-shot without redo
PASS: Global Space from image focuses next LV row and suppresses repeat
PASS: Ctrl+Enter reviews entire selection in one action
PASS: Bulk confirmation autorepeat ignored
PASS: Bulk review undo restores both selected records
PASS: Inventory label commit reviews and advances once
PASS: Label and review state undo together
PASS: Image-edit cancellation changes no review/cache state
PASS: Image editing uses shared inventory commit
PASS: Filtered zero rows is not completion while recognized unreviewed records remain
PASS: Delete confirmation ON cancels safely
PASS: Confirmation OFF deletes selection and advances once
PASS: Multi-delete undo restores all records and selection
PASS: Normal image edit keeps target selection without reviewing
PASS: Normal last-row delete selects preceding sorted candidate
PASS: Done checkbox undo restores previous done state
PASS: Manual addition recorded
PASS: Manual-add undo removes only new record
PASS: All reviewed shows completion and empty selection
PASS: Review progress persisted per image
PASS: Normal mode shows reviewed records
PASS: Mode/undo command enablement shared across entry points
PASS: Menu Undo uses transaction handler
PASS: Toolbar review shares keyboard operation
PASS: Image switch clears one-transaction undo
PASS: Restart restores review and delete preference, not inventory mode
PASS: Restart has no undo history
PASS: 34 inventory/undo checks
```

## 移行 詳細ログ

```text
PASS: Export inventory enumerates primary image caches, not backups
PASS: ZIP contains JSON manifest only, no originals
PASS: All-image export includes both images
PASS: Selected export/import carries one image
PASS: Import without image folders is durable pending data
PASS: Portable preferences imported
PASS: Destination history, placement and unknown settings preserved
PASS: Pending imports survive service/application recreation
PASS: Matching uses case-insensitive filename and dimensions, ignores path/time/content
PASS: Different dimensions do not match
PASS: Recursive search finds nested image
PASS: Unique association registers destination cache and retires pending item
PASS: Correction/review/done/source text and circle preserved
PASS: Deletion and manual point preserved
PASS: Skip collision preserves existing cache byte-for-byte
PASS: Replace collision applies import
PASS: Replace backs up previous local work
PASS: Multiple candidates remain distinguishable for manual binding
PASS: Explicit choice binds one of multiple candidates
PASS: Folder removal persists locally without deleting images
PASS: Invalid import changes neither local cache nor pending records
PASS: Whole ZIP validated before publishing data or changing preferences
PASS: Bound cache carries destination absolute path and UTC timestamp
PASS: Later explicit open binds pending data without OCR despite different timestamp
PASS: Restart/last-image path restores migrated local cache without OCR
PASS: Migration never changes source originals
PASS: 26 transfer checks

```
