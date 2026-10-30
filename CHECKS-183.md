# 1.8.3 自動確認結果

2026-10-31 Windows Release。合計426件成功。実画面確認・未確認範囲はREADME参照。

## image-edit

```text
PASS: RX-78 unknown Ctrl+A selects 237 candidates
PASS: First Ctrl click is deferred, not toggled before double click
PASS: Dialog opens for hit fixed ID without changing selection
PASS: Cancel leaves record, complete selection and cache bytes unchanged
PASS: Cancel preserves both viewports and zoom
PASS: Real WinForms timer commits a single Ctrl click after interval
PASS: Second single click restores target selection
PASS: Double-click second press dragged beyond threshold neither edits nor toggles
PASS: Floating edit opens without altering selection
PASS: OK corrects only target and removes only target from selection
PASS: Corrected unknown disappears from filtered list
PASS: Correction persisted with fixed ID and viewport unchanged
PASS: Hidden recognized record can be directly edited
PASS: Recognized edit cancellation preserves filter and all state
PASS: Hidden recognized edit OK preserves unrelated selection and filter
PASS: Recognized edit saved under original fixed ID
PASS: Delete removes remaining unknown selection, preserving corrected record
PASS: Restart restores correction and deletions
PASS: Dialog initial text/select-all/focus and work-area clipping: 
PASS: Whitespace cannot confirm
PASS: Mixed Unicode label accepted
PASS: Dialog initial text/select-all/focus and work-area clipping: A102
PASS: Whitespace cannot confirm
PASS: Mixed Unicode label accepted
PASS: Modal dialog keyboard result: Enter
PASS: Modal dialog keyboard result: Escape
PASS: 26 image-edit checks
```

## frames

```text
PASS: Missing settings default to 2px
PASS: Absent/invalid frame value falls back safely: {}
PASS: Absent/invalid frame value falls back safely: {"FrameWidthPx":0}
PASS: Absent/invalid frame value falls back safely: {"FrameWidthPx":5}
PASS: Absent/invalid frame value falls back safely: {"FrameWidthPx":"3"}
PASS: Absent/invalid frame value falls back safely: {"FrameWidthPx":null}
PASS: Frame save preserves history, placement and unknown fields
PASS: MainForm restores dropdown and both views
PASS: Changing dropdown redraws both views and saves immediately
PASS: Restart restores selected width
PASS: Detail 25% red requested=1px measured=1px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 25% blue requested=1px measured=1px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 25% red requested=2px measured=2px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 25% blue requested=2px measured=2px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 25% red requested=3px measured=3px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 25% blue requested=3px measured=3px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 25% red requested=4px measured=4px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 25% blue requested=4px measured=4px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 100% red requested=1px measured=1px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 100% blue requested=1px measured=1px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 100% red requested=2px measured=2px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 100% blue requested=2px measured=2px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 100% red requested=3px measured=3px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 100% blue requested=3px measured=3px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 100% red requested=4px measured=4px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 100% blue requested=4px measured=4px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 200% red requested=1px measured=1px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 200% blue requested=1px measured=1px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 200% red requested=2px measured=2px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 200% blue requested=2px measured=2px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 200% red requested=3px measured=3px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 200% blue requested=3px measured=3px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 200% red requested=4px measured=4px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Detail 200% blue requested=4px measured=4px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 25% red requested=1px measured=1px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 25% blue requested=1px measured=1px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 25% red requested=2px measured=2px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 25% blue requested=2px measured=2px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 25% red requested=3px measured=3px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 25% blue requested=3px measured=3px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 25% red requested=4px measured=4px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 25% blue requested=4px measured=4px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 100% red requested=1px measured=1px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 100% blue requested=1px measured=1px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 100% red requested=2px measured=2px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 100% blue requested=2px measured=2px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 100% red requested=3px measured=3px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 100% blue requested=3px measured=3px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 100% red requested=4px measured=4px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 100% blue requested=4px measured=4px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 200% red requested=1px measured=1px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 200% blue requested=1px measured=1px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 200% red requested=2px measured=2px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 200% blue requested=2px measured=2px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 200% red requested=3px measured=3px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 200% blue requested=3px measured=3px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 200% red requested=4px measured=4px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PASS: Overview 200% blue requested=4px measured=4px
PASS: Fill, source/UI geometry, hit region and viewport unchanged
PAINT RX-78 25% 2px mean=21.530ms p95=23.204ms n=40 (DrawToBitmap included)
PAINT RX-78 25% 4px mean=21.377ms p95=22.416ms n=40 (DrawToBitmap included)
PAINT RX-78 100% 2px mean=5.098ms p95=7.255ms n=40 (DrawToBitmap included)
PAINT RX-78 100% 4px mean=5.345ms p95=5.674ms n=40 (DrawToBitmap included)
PAINT RX-78 200% 2px mean=6.032ms p95=7.211ms n=40 (DrawToBitmap included)
PAINT RX-78 200% 4px mean=5.989ms p95=6.974ms n=40 (DrawToBitmap included)
PASS: 106 frame width checks
```

## regression

```text
PASS: 202 checks
```

## workflow

```text
PASS: 34 workflow checks
```

## toggle

```text
PASS: 18 toggle checks; RX-78 unknowns=237; W:\dev\AF-DecalFinder\viewer-checks\toggle-5b77e7c1-72d9-49b6-aa02-71e0689ab3ca
```

## geometry

```text
PASS: 40 geometry/filter checks
```
