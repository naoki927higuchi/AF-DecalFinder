# 1.8.1 回帰・実画面確認

2026-10-31、Windows、Release。検証用画像コピー・専用テストデータで実施。

|テスト|成功数|
|---|---:|
|既存回帰|202|
|実OCR統合（8画像）|98|
|編集・削除・キャッシュのワークフロー|34|
|統一矩形・未認識フィルタ|40|
|合計|374|

実画面ではRGルージュのコピーを使用。未認識フィルタON、未認識候補のF2空欄編集、73の確定による一覧除外と即時検索赤枠、画像クリックによるフィルタ解除と固定ID選択、済み黒塗りと青枠の同一範囲を確認。編集と右ビュー逆引きで不要なパンなし。フローティング／再ドッキング後も選択・検索・済み状態を維持。

以下は追加自動確認の実行ログ。元OCR値、キャッシュ互換、両ビューの描画画素・当たり判定、フィルタ中の操作を含む。

```text
PASS: Median rejects tiny/large outliers, mean is 50x50
PASS: Outliers retained as records; every UI rectangle uses representative size
PASS: Measured circle center beats off-center OCR text box
PASS: UI derivation leaves original coordinates/confidence unchanged
PASS: Same-image reanalysis enriches protected legacy record only with exact-match measured circle
PASS: Missing center uses OCR center without inventing circle evidence
PASS: Insufficient evidence falls back to original box
PASS: Manual click center retained exactly; UI rectangle clipped at image edges
PASS: Derived UI geometry never persisted as source data
PASS: Measured circle and exact manual click survive cache roundtrip
PASS: Cache roundtrip preserves all original OCR evidence
PASS: Legacy schema 1 loads without destructive migration or guessed circle
PASS: Legacy schema 2 loads without destructive migration or guessed circle
PASS: 50% black fill covers unified box: Detail
PASS: Hit test matches visible UI boundary: Detail
PASS: Red uses same UI boundary: Detail
PASS: Blue uses same boundary over red: Detail
PASS: 50% black fill covers unified box: Overview
PASS: Hit test matches visible UI boundary: Overview
PASS: Red uses same UI boundary: Overview
PASS: Blue uses same boundary over red: Overview
PASS: Filter uses internal unknown flag, preserves visible fixed ID and viewport
PASS: Filtered checkbox saves only target and keeps multiselection
PASS: Filter retains complete underlying dataset
PASS: Search still highlights recognized records outside filtered LV
PASS: Search Enter navigates first all-data hit while filter active
PASS: Correction leaves unknown list and retains other fixed-ID selection without pan
PASS: Filtered delete cancel retains record
PASS: Filtered deletion persists tombstone, not filter hiding
PASS: Filter OFF restores normal sorted records after correction
PASS: Recognized reverse lookup exits filter and selects fixed ID without pan
PASS: 8-image measured geometry/original evidence: 300dpi.jpg.json
PASS: 8-image measured geometry/original evidence: 600dpi.jpg.json
PASS: 8-image measured geometry/original evidence: MGUCペルフェクティビティ2.png.json
PASS: 8-image measured geometry/original evidence: RG RX-78-2 ver 2.png.json
PASS: 8-image measured geometry/original evidence: RGルージュ.png.json
PASS: 8-image measured geometry/original evidence: RX-78 ver 3.0.png.json
PASS: 8-image measured geometry/original evidence: カトキガンダム.png.json
PASS: 8-image measured geometry/original evidence: ルナマリアザクウォーリア.png.json
PASS: All eight originals measured
PASS: 40 geometry/filter checks
```
