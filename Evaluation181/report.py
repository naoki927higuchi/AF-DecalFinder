"""Build the report from measured outputs, without changing Phase 1 or inputs."""
import json, subprocess, sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'Evaluation181'
rows=json.loads((OUT/'measurements.json').read_text(encoding='utf8'))
ui={r['Image'][:-5]:r for r in json.loads((OUT/'ui-measurements.json').read_text(encoding='utf8'))}
lines=['# 1.8.1 代表サイズ・統一矩形・2パス探索の検証','',
'8原本を変更せず使用。候補検出・数字OCRは1.8.0と同じコード。代表サイズの算出はC#側で行い、派生UI矩形だけを更新する。','',
'## 代表サイズ','',
'○の実測情報を持つ未削除OCR候補だけを使用し、同じ○の重複を除く。文字bbox・手動追加は代表サイズの母集団に入れない。幅/高さの中央値からそれぞれ±30%以内を残し、平均幅/平均高さを採用。5件未満、残存率50%未満、残存候補の変動係数が幅/高さのいずれかで20%超なら元bboxへフォールバックする。サイズ外れ値も検出レコードからは削除しない。','',
'±20/30/40%を比較した。20%はRGルージュの199候補中31個、カトキ110候補中16個をサイズ算出から外す。40%ではテムジン/MGUCの大型側外れ値も平均へ入る。今回の8画像でばらつきが20%以内に収まり、中央値付近のサイズ差を許容する30%を暫定採用した。真のラベルサイズが大きく混在する図面への保証ではない。円候補なので今回の推定幅/高さは等しい。','',
'UI計算時間は実C#で100回再計算した平均（ソート・中央値・矩形生成を含む）。OCR時間は別プロセス起動/import/decode/OCRを含む各1回で、描画時間ではない。','',
'|画像|候補/レコード数|中央値径px|代表幅×高さpx|算出採用数|UI計算ms|OCR秒|元OCR値の一致|',
'|---|---:|---:|---|---:|---:|---:|---|']
for r in rows:
    file=r['file'];current=json.loads((OUT/(file+'.json')).read_text(encoding='utf8'))
    old_output=ROOT/'Evaluation180'/(file+'.1.8.0.json')
    previous=json.loads(old_output.read_text(encoding='utf8')) if old_output.exists() else json.loads(subprocess.run(
        [sys.executable,'-B',str(ROOT/'bin/Release-1.8.0/Ocr/worker.py'),str(ROOT/'inputs'/file)],
        capture_output=True,text=True,encoding='utf8',check=True).stdout)
    def evidence(data):return sorted((d['number'],tuple(d['bbox']),d['confidence'],d.get('is_unrecognized',False)) for d in data['detections'])
    same=evidence(current)==evidence(previous)
    assert same,file
    u=ui[file];s=u['Estimate']['Size'];n=u['Estimate']['Inliers']
    lines.append(f"|{file}|{r['records']}|{r['median']:.2f}|{s['Width']:.2f} × {s['Height']:.2f}|{n}/{r['candidates']}|{u['MeanDerivationMs']:.3f}|{r['ocr_seconds']:.3f}|全件一致|")
lines += ['',
'旧/新版の番号・元bbox・信頼度・未認識状態は8画像で全件一致（新規解析のランダムIDは比較対象外。キャッシュ内IDは維持）。テムジン600dpiは276件のまま。従来181件の元番号/bboxを含む。元の候補検出を変更していないため、検出件数への影響はない。','',
'## 2パス試行：今回は不採用','',
'Pass 1の代表径を中心に±30%の半径範囲で、Hough param2を20から17へ緩めて追加探索し、円周品質0.9・内部コントラスト60の検証後に既存候補へ合流。既存候補はサイズだけで削除しない。1.8.0と同じ目視正解88個・同じROI・同じ一対一照合基準を使用。','',
'|画像|正検出 1→2パス|漏れ 1→2パス|誤検出 1→2パス|全候補 1→2パス|追加探索秒|',
'|---|---|---|---|---|---:|']
for r in rows:
    a,b=r['old'],r['two_pass']
    lines.append(f"|{r['file']}|{a['tp']}→{b['tp']}|{a['fn']}→{b['fn']}|{a['fp']}→{b['fp']}|{r['candidates']}→{r['two_pass_count']}|{r['extra_pass_seconds']:.3f}|")
lines += ['',
'全体では正検出87→88、漏れ1→0、誤検出17→22。RG RX-78-2で1個を補えたが、重点3画像では正検出増加がなく、全体の誤検出と探索時間が増えた。追加候補の数字OCR時間はこの追加探索秒に含めておらず、製品採用時の総負担はさらに増える。今回は不採用とし、検出処理は1.8.0のまま維持する。','',
'## データ保全・UI','',
'OCR元bbox/信頼度、実測Circle、ManualCenter、派生UiBoundsを分離。赤枠・青枠・済み黒塗り・逆引きが同一UiBoundsを参照する。中心は手動クリック（手動レコード）、実測○中心、元bbox中心の順。画像端では矩形をクリップし、重なりは元の中心までの距離、完全同距離は固定IDで決める。従来の見えない5pxヒット拡張とFitビューの曖昧候補棄却を変更した。','',
'キャッシュschema 1/2を引き続き読める。schema 2に省略可能なCircle/ManualCenterを追加し、UiBoundsは保存しない。1.8.0以前のアプリキャッシュには○情報が保存されていないため、情報がない画像では元bboxへ戻す。有効キャッシュを勝手に再解析せず、必要なら「OCRを再解析」を選ぶ。既存の手動修正・追加・削除・済み情報と固定IDを維持する。旧手動レコードのクリック位置は復元できないので元bbox中心を使い、推測値を実測○として保存しない。','',
'未認識フィルタは内部IsUnrecognizedで判定する。表示文字列の「?」では判定しない。非表示レコードは保存データに残る。フィルタ切替は可視レコードの固定ID選択を保持し、隠れる選択は解除、パン/倍率は維持。編集確定で認識済みになった行は一覧から外れる。逆引きで認識済みを選ぶ場合と手動追加確定時はフィルタを解除して行を表示する。検索/検索Enterは全データを使う。Ctrl+Aは表示中の行を対象とする。','',
'## 再現','',
'Build-Viewer.ps1の後、このPCのPython 3.12でEvaluation181/measure.py、Viewer.Checksの--geometry、Evaluation181/report.pyの順に実行する。report.pyは1.8.0のローカル生出力との完全一致確認も行う。生出力がない場合は保存済みRelease-1.8.0ワーカーを再実行する。旧Releaseもなければ1.8.0コミットを別の作業コピーでビルドする必要がある。measurements.jsonとui-measurements.jsonに数値を記録。正解は変更していないEvaluation180/ground-truth.jsonを参照。','']
(OUT/'REPORT.md').write_text('\n'.join(lines),encoding='utf8')
print('Report and eight-image OCR evidence comparison passed.')
