# 1.8.2 選択反転の自動確認

2026-10-31、Windows Release。RX-78 Ver.3.0の原本コピーと専用キャッシュを使用。実キー併用のCtrl操作は未確認、Ctrl状態を指定したビューのマウスイベントと実MainFormの処理を結合して検証。詳細はREADME。

```text
PASS: RX-78: filtered Ctrl+A selects all unknown records and blue overlays
PASS: Overlap: nearest center wins after hidden records are excluded
PASS: Ctrl miss preserves selection and filter
PASS: Ctrl click deselects exactly one fixed ID
PASS: Toggle changes neither image center nor zoom
PASS: Second toggle preserves other selections
PASS: Repeat Ctrl click adds target back
PASS: Filtered recognized records excluded before hit ranking
PASS: Ctrl drag returning to origin never toggles selection
PASS: Ctrl drag pans image without altering selections
PASS: MouseUp displacement over threshold without MouseMove never toggles
PASS: Floating list supports toggle and keeps unknown filter
PASS: Deselected unknowns remain listed after redocking
PASS: Delete confirmation count and cancellation
PASS: Delete only selected unknowns, retaining both deselected records
PASS: Logical deletions autosaved to image cache
PASS: Normal click remains single-record reverse lookup
PASS: Restart restores deletion and preserves deselected unknown IDs
PASS: 18 toggle checks; RX-78 unknowns=237; W:\dev\AF-DecalFinder\viewer-checks\toggle-bed81d1d-18fa-4022-81fc-de09fd4a91c5
```
