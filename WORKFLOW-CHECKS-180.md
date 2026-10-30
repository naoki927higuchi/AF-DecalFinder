# 1.8.0 修正ワークフロー自動検証

Release版の実MainForm、専用の検証画像・設定・キャッシュで実行。ネイティブLabelEditのキーボード入力は実画面で別途確認し、このテストではモデル更新・イベント処理・保存・復元を検査する。

実行：

```powershell
dotnet run --project Viewer.Checks/AF.DecalFinder.Checks.csproj -c Release -- W:\dev\AF-DecalFinder --workflow
```

結果：

```text
PASS: Schema 2 stores content evidence and state flags
PASS: Numeric/Latin/other/unrecognized sort and leading zero retained
PASS: Literal question mark search excludes unrecognized placeholder
PASS: Unrecognized candidate selectable by fixed ID
PASS: Unknown accepts kana/alphanumeric correction
PASS: Correction keeps ID/selection/viewport and clears unknown state
PASS: Empty/whitespace-only edit rejected
PASS: Exact string search: A1
PASS: Exact string search: あ
PASS: Exact string search: あ1
PASS: Exact string search: 04
PASS: Search does not normalize case
PASS: Delete asks selected count; cancel changes nothing
PASS: Context menu exposes explicit false-positive deletion
PASS: Delete removes selected fixed IDs from list and blue selection
PASS: Logical deletion persisted; deleted done records excluded from count
PASS: Deleted records excluded from search
PASS: Deleted record excluded from image reverse lookup
PASS: Manual addition accepts mixed label
PASS: Ctrl+A selects all active records and blue overlays
PASS: Floating list string correction keeps selected fixed ID and viewport
PASS: Redocking retains corrected string, selection and persisted record
PASS: Startup restores correction/manual data and keeps tombstones hidden
PASS: Same bytes reanalysis retains tombstone ID and suppresses rediscovered false positive
PASS: Same bytes reanalysis retains edits/manual additions
PASS: Changed image bytes never receive old coordinates; archive remains
PASS: Legacy schema loads all work without destructive migration
PASS: First schema 2 save backs up exact legacy document
PASS: Worker unread circle remains a searchable-by-position record
PASS: Overlapping protected records keep every independent fixed ID
PASS: Valid cache never automatically reruns OCR after upgrade
PASS: Explicit reanalysis keeps deletion and removes repeated false candidate
PASS: Explicit reanalysis retains work and adds fresh candidates without normalization
PASS: Explicit reanalysis preserves viewport and restores controls
PASS: 34 workflow checks
```
