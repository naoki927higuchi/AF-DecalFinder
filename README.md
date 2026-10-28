# AF-DecalFinder

## Phase 2：最小検索ビューア（1.1.0）

C# / WinForms / .NET 9。既存のOCR結果を検索して画像上の全ヒットを赤枠表示する試作。OCRエンジン・Pythonは起動しない。

### 起動

[AF-DecalFinder.exe](bin/Release-1.1.0/AF-DecalFinder.exe) を起動し、「画像を開く…」で `W:\dev\AF-DecalFinder\inputs\600dpi.jpg` を選ぶ。JPG/JPEG/PNGをウィンドウへ1ファイルだけドラッグ＆ドロップしても開ける。このJPEGは `D:\Picture\テムジンデカール600dpi.zip` 内の画像をPhase 1で無変換抽出した原本。ZIP自体をビューアで直接開く機能はない。

原本を指定して起動する場合：

```powershell
& 'W:\dev\AF-DecalFinder\bin\Release-1.1.0\AF-DecalFinder.exe' 'W:\dev\AF-DecalFinder\inputs\600dpi.jpg'
```

- 番号欄へ半角数字を入力すると、その都度完全一致検索する。102は1件、49と118は各2件。複数ヒットをすべて同時に表示し、空欄で解除する。貼り付けでも半角数字以外は除去する。
- ホイールでカーソル位置を中心にズーム、左ドラッグでパン。「全体表示」でアスペクト比を保った全体表示へ戻る。
- 検索による自動ズーム・自動移動はしない。赤枠は塗りつぶしなし、画面上約2px。原本を編集しない。
- JSONはEXEと同じ出力フォルダー内の `results/600dpi.json` を読む。ビルド時にPhase 1の `results/600dpi.json` をそのままコピーする。画像は別途選択し、前回画像の復元は行わない。
- JSONの画像寸法と実画像寸法が違うと警告して検索不可にする。JSON欠落はステータス欄で通知し、不正JSON・画像破損はエラーダイアログを表示する。JSONの必須項目、数字文字列、bbox範囲、信頼度も検証する。
- 寸法一致は確認するが、同じ寸法の別画像を判別する機能はない。今回のJSONに対応するテムジン原本を選択すること。OCRの誤認識・漏れはPhase 1のまま表示に反映される。

### ビルドと検証

このPCでは.NET SDK 9.0.318 / Windows Desktop Runtime 9.0.20で確認。追加NuGetパッケージは使っていない。

```powershell
Set-Location W:\dev\AF-DecalFinder
.\Build-Viewer.ps1
dotnet run --project .\Viewer.Checks\AF.DecalFinder.Checks.csproj -c Release -- W:\dev\AF-DecalFinder
```

ビルド前には起動中のビューアを終了する。出力は `bin/Release-1.1.0/`。EXEだけを移動せず、DLL・runtimeconfig・resultsを含む出力一式を同じ構成で使う。別PCでは.NET 9 Desktop Runtimeと対象画像を別途用意する。正式配置・インストール・公開用ZIP作成は今回行っていない。

2026-10-29の確認結果：

|確認対象|結果・確認方法|
|---|---|
|Releaseビルド・起動|Windows上で成功|
|600dpi原本の全体表示|ファイル選択ダイアログから7783×6579px画像を読み込み、目視確認|
|102の検索|実画面で1件、括弧内の102を囲む赤枠を確認|
|同一番号の複数ヒット|実画面で49・118それぞれ2件を同時表示|
|ズーム・パン|実画面でホイール拡大とドラッグ移動後も102と枠が一致|
|即時変更・空欄|49→118→空欄で赤枠が切り替わり、画像位置・倍率は変化なし|
|PNG・寸法不一致|120×80pxのPNGで警告表示、番号入力無効・検索不可を実画面確認|
|破損画像|破損JPGでエラーダイアログを実画面確認|
|ドラッグ＆ドロップ|自動テストで実FormのDropイベントに原本パスを渡し、読込→49の2件検索まで成功。Explorerからの実ドラッグ操作は未確認|
|JSON・座標・線幅|不正JSON6ケース、欠落、原本座標、ズームの基準点、パン、2件の描画、塗りつぶしなし、約2px線幅、画像ファイル非ロックなど計27項目の自動確認が成功|

画像描画・変換は `Viewer/ImageViewControl.cs` と `Viewer/ViewTransform.cs`、JSON読込と検索は `Viewer/OcrCatalog.cs`、ファイル選択と検索欄は `Viewer/MainForm.cs` に分離。画像と矩形は共通の変換を使用し、矩形の線幅だけを画面座標で固定している。

## Phase 1（既存成果物）

TesseractのOCR実用性を判断するためのローカル技術検証（1.0.0）。アプリ本体は含まない。

結果は [REPORT.md](REPORT.md)、数値一覧は [results/summary.csv](results/summary.csv)。推奨設定の出力は [300dpi.json](results/300dpi.json)、[600dpi.json](results/600dpi.json)。原本寸法の赤枠画像もresultsにある。

## このPCで再実行

PowerShellで以下を実行する。実行には約3分かかる。既存の測定出力を上書きする。

```powershell
Set-Location W:\dev\AF-DecalFinder
.\run.ps1
```

依存物はこのフォルダーの`.deps`と`tessdata`に導入済み。システムのPythonやPATHは変更していない。別環境ではPython 3.12 x64を用意して以下を実行する。

```powershell
.\setup.ps1 -Python 'C:\path\to\python.exe'
.\run.ps1 -Python 'C:\path\to\python.exe' -SourceDir 'D:\Picture'
```

`setup.ps1`はPyPIと公式tessdata_fastから依存物を取得する。モデルのSHA256が記録値と異なる場合は停止する。依存物・画像原本・確認画像はローカル保管、Gitに含めない。

## 個別実行

```powershell
$py = "$env:USERPROFILE\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe"
& $py benchmark.py --config circle_psm7_wide --repeats 3
& $py evaluate.py
& $py report.py
& $py verify.py
```

全設定を実行済みのフォルダーでの例。`verify.py`は今回の全48実行、各3回の一致、原本SHA256、座標、同一番号の独立性を検証するため、初めて一部設定だけ実行した状態では失敗する。`--repeats 1`は試行用であり最終検証の条件を満たさない。

## 構成

- `prepare_inputs.py`：ZIP内の唯一のJPEGを無変換で抽出し、既存原本との不一致を拒否。
- `benchmark.py`：ネイティブTesseract実行・原本座標への変換・速度/メモリ計測。正解データは一切読まない。
- `ground_truth.json`：OCR開始前に目視作成した38件。`make_ground_truth.py`に手入力した番号・矩形を保存。`evaluation_sources`に領域・位置合わせの記録を保存。
- `evaluate.py`：位置による1対1照合とCSV集計。
- `report.py`：今回の選定設定と実測値を使った報告書・確認画像の再生成。結論文はこの入力・今回の実測に固有。
- `verify.py`：入力不変性と出力契約の検証。
- `environment.json`：今回の実測環境。別PCで再実験する場合は環境・結論を更新し、今回の数値と区別する。

選定に使った38件での評価であり、未使用図面への汎化性能は未検証。C# WinFormsへの組込み、C#バインディングの選定、GUI・キャッシュ等は対象外。
