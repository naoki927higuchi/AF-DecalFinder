# Phase 4.1 性能検証

[MEASUREMENTS.md](MEASUREMENTS.md) に同条件の比較表、[results](results/) に各反復の数値を保存する。Phase 1のOCR結果とは別の成果物。

## 実行

実アプリを終了して、他の重い処理と同時に実行しない。PowerShell、.NET SDK 9以上で以下を順に実行する。

```powershell
Set-Location W:\dev\AF-DecalFinder
dotnet run --project Performance/AF.DecalFinder.Performance.csproj -c Release -- W:\dev\AF-DecalFinder baseline W:\dev\AF-DecalFinder\Performance\results\baseline.json 30 3
dotnet run --project Performance/AF.DecalFinder.Performance.csproj -c Release -- W:\dev\AF-DecalFinder improved W:\dev\AF-DecalFinder\Performance\results\improved.json 30 3
dotnet run --project Performance/AF.DecalFinder.Performance.csproj -c Release -- W:\dev\AF-DecalFinder quality W:\dev\AF-DecalFinder\Performance\results\quality.json
& 'C:\Program Files\LibreOffice\program\python.exe' Performance/report.py
```

- `baseline` は1.3.0の操作・描画処理を計測カウンターのみ追加して固定した `BaselineImageViewControl.cs`。元コミット6008a2e。共有のViewTransformは同コミットから変更していない。
- `improved` は現在の本番描画と本番の読込時PArgb変換を使用する。JPEGは再解析も変更もしない。
- `quality` は旧版と改善版の画像・赤青枠を比較。920×1008pxの描画面、全6倍率、整数/半画素中心・左上/右下端、2ビューの48条件で3px間隔のRGBを比較する。比較画素に差があれば失敗終了する。全画素・全中心位置を網羅する検査ではない。
- 任意の診断モード `argb` は改善した描画に旧ARGB形式の共有Bitmapを入力する。`pargb` は `improved` と同じ画素形式で、短い探索測定用。
- `roi-argb.json` は中央の再利用＋右の部分描画のみを適用した有効な中間測定（30×3）。`pargb-pilot.json` と `invalid-dpi-conversion.json` はDPI変換不具合のあった試行であり、性能評価から除外する。主比較表には修正後の同条件30×3だけを使用する。
- `quality-roi-first.json` は切り出し位相を揃える前の失敗記録。最終版の検査結果は `quality.json`。

PArgb変換の最初の試行ではDrawImageUnscaledがJPEGの600dpiメタデータに影響され、画像を縮小してコピーする不具合が実画面で判明した。ピクセル単位の原寸矩形を明示する処理へ修正し、600dpiの右下画素までコピーされる回帰テストを追加した。最終quality検査は既に96dpiへ変換済みのBitmapではなく、原本JPEGからの本番変換を直接比較し、性能測定も取り直している。

## 解釈

これは同じOnMouseMove/OnPaintを用いたCPU描画ハーネス。OSの入力待ち行列や画面提示遅延は測らない。再利用するオフスクリーン描画先へ、Invalidateされた各ビューを1更新ずつ描画する。アプリの部分Invalidateより広い全表示領域を描画するため、その最適化による追加効果は主比較に含めない。

P95は各反復のnearest-rank値を生JSONへ保存し、表は3反復中の最大P95。OnPaint平均は実際のpaint回数で加重平均。Bitmap生成/破棄はアプリ管理のBitmapだけを数え、GDI+内部のバッファは含めない。GCは世代別、メモリはProcessのPrivate BytesとWorking Set。初回画像読込・中央縮小画像生成はウォームアップに含め、パンの測定区間から除外する。

## 1.4.1 逆引き追加の回帰比較

1.3.0との比較ファイルを維持し、1.4.0との比較を別ファイルへ保存する。`previous` はfde03afのImageViewControlをクラス名だけ変更した固定スナップショット。操作開始の5pxしきい値による再描画回数差は報告書に明記する。

```powershell
dotnet run --project Performance/AF.DecalFinder.Performance.csproj -c Release -- W:\dev\AF-DecalFinder previous W:\dev\AF-DecalFinder\Performance\results\reverse-before.json 30 3
dotnet run --project Performance/AF.DecalFinder.Performance.csproj -c Release -- W:\dev\AF-DecalFinder improved W:\dev\AF-DecalFinder\Performance\results\reverse-after.json 30 3
& 'C:\Program Files\LibreOffice\program\python.exe' Performance/reverse_report.py
```

[逆引き追加前後の実測値](REVERSE-MEASUREMENTS.md)。描画経路は変更していないため、今回の結果を描画最適化の効果とは扱わない。
