# AF-DecalFinder Phase 1

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
