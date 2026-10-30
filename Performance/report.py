"""Summarize same-condition CPU-side rendering measurements; stdlib only."""
import json
from pathlib import Path

root = Path(__file__).resolve().parent
before = json.loads((root / 'results/baseline.json').read_text(encoding='utf-8-sig'))
after = json.loads((root / 'results/improved.json').read_text(encoding='utf-8-sig'))
assert (before['panePixels'], before['frames'], before['repeats']) == (after['panePixels'], after['frames'], after['repeats'])

def avg(case, *keys):
    values = []
    for sample in case['samples']:
        for key in keys:
            sample = sample[key]
        values.append(sample)
    return sum(values) / len(values)

def p95(case):
    return max(s['response']['p95Ms'] for s in case['samples'])

def paint(case):
    count = sum(s[v]['paint']['count'] for s in case['samples'] for v in ('overview', 'detail'))
    return sum(s[v]['paint']['count'] * s[v]['paint']['meanMs'] for s in case['samples'] for v in ('overview', 'detail')) / count

lines = ['# Phase 4.1 描画性能の計測結果', '',
    '実測日：2026-10-30。比較元は1.3.0（6008a2e）の描画・操作コードを計測カウンター付きで固定したもの。', '',
    '## 条件と測定の限界', '',
    '- 入力：7783×6579pxの600dpi原本JPEG。原本から読み込んだBitmapを両ビューで共有。検索49（2件）、選択181件を描画。',
    '- 32インチ4Kを想定した3840×2160pxレイアウト。描画面は各1838×2016px固定。物理インチ数は描画負荷を決めないため、ピクセル寸法で条件を固定。',
    f"- {before['os']} / {before['runtime']} / 論理CPU {before['logicalProcessors']}。CPU識別：{before['cpu']}。", 
    f"- 25/50/100/200%、右パンと中央の表示範囲ドラッグを各{before['frames']} MouseMove×{before['repeats']}回。6回のウォームアップ後に各反復を計測。毎回同じ中心・座標列を使用。",
    '- 本番のMouseMove/OnPaintを呼ぶ再現用ハーネス。GDI+の再利用Bitmapへ描画し、InvalidateされたビューをMouseMoveごとに1回ずつ描画する。OSによるイベント間引き・WM_PAINTの統合・画面転送・モニタ表示遅延は含めない。実際の入力遅延やFPSの直接測定ではない。',
    '- OnPaint時間は画像とオーバーレイを含む。応答代理時間はイベント処理から両ビューの描画終了まで（背景クリア等のハーネス処理を含む）。平均は3反復平均、応答P95は各30サンプルのnearest-rank P95の最大値。',
    '- CPU使用率はプロセスCPU時間÷実時間÷論理CPU数。待機なしで連続投入するため、速度が上がってもCPU使用率は必ずしも下がらない。CPU ms/更新も併記。',
    '- メモリはプロセスPrivate BytesとWorking Setの反復終了値。アプリのBitmap生成/破棄を数え、GDI+内部一時バッファは数えない。デコード元・共有Bitmap・描画先2枚の生成は測定前。', '',
    '## 比較', '',
    '|倍率|操作|応答平均 ms 前→後|応答P95 ms 前→後|OnPaint平均 ms 前→後|CPU ms/更新 前→後|CPU全体比% 前→後|',
    '|---|---|---|---|---|---|---|']
for b, a in zip(before['results'], after['results']):
    assert (b['scale'], b['operation']) == (a['scale'], a['operation'])
    lines.append(f"|{b['scale']*100:g}%|{b['operation']}|{avg(b,'response','meanMs'):.2f} → {avg(a,'response','meanMs'):.2f}|{p95(b):.2f} → {p95(a):.2f}|{paint(b):.2f} → {paint(a):.2f}|{avg(b,'cpuMs')/before['frames']:.2f} → {avg(a,'cpuMs')/after['frames']:.2f}|{avg(b,'cpuPercentMachine'):.2f} → {avg(a,'cpuPercentMachine'):.2f}|")
lines += ['', '## OnPaint P95（各ビュー・3反復中の最大、ms）', '', '|倍率・操作|中央 前→後|右 前→後|', '|---|---|---|']
for b,a in zip(before['results'], after['results']):
    cells = []
    for view in ('overview','detail'):
        cells.append(f"{max(s[view]['paint']['p95Ms'] for s in b['samples']):.2f} → {max(s[view]['paint']['p95Ms'] for s in a['samples']):.2f}")
    lines.append(f"|{b['scale']*100:g}% {b['operation']}|"+'|'.join(cells)+'|')
lines += ['', '## 内訳（ビュー単位の平均 ms）', '', '|倍率・操作|中央画像 前→後|右画像 前→後|中央overlay 前→後|右overlay 前→後|操作対象MouseMove 前→後|', '|---|---|---|---|---|---|']
for b,a in zip(before['results'], after['results']):
    cells=[]
    for view, field in [('overview','image'),('detail','image'),('overview','overlay'),('detail','overlay'),('detail' if b['operation']=='detail-pan' else 'overview','mouse')]:
        cells.append(f"{avg(b,view,field,'meanMs'):.4f} → {avg(a,view,field,'meanMs'):.4f}")
    lines.append(f"|{b['scale']*100:g}% {b['operation']}|"+'|'.join(cells)+'|')
lines += ['', '## 再描画・メモリ・GC', '', '|倍率・操作・版|中央/右paint回数（3反復合計）|Bitmap生成/破棄（ドラッグ内）|Gen0/1/2 GC合計|Private MiB平均|Working Set MiB平均|', '|---|---|---|---|---|---|']
for b,a in zip(before['results'], after['results']):
    for case,label in [(b,'前'),(a,'後')]:
        samples=case['samples']
        counts=[sum(s[v]['paint']['count'] for s in samples) for v in ('overview','detail')]
        creates=sum(s[v]['BitmapCreations'] for s in samples for v in ('overview','detail'))
        disposes=sum(s[v]['BitmapDisposals'] for s in samples for v in ('overview','detail'))
        gc=[sum(s['gcCollections'][i] for s in samples) for i in range(3)]
        lines.append(f"|{case['scale']*100:g}% {case['operation']} {label}|{counts[0]} / {counts[1]}|{creates} / {disposes}|{' / '.join(map(str,gc))}|{avg(case,'privateEndBytes')/2**20:.1f}|{avg(case,'workingEndBytes')/2**20:.1f}|")
lines += ['', '生データ：[baseline.json](results/baseline.json)、[improved.json](results/improved.json)。各反復の最大値・メモリ開始/終了・割当バイト数も収録。', '']
(root/'MEASUREMENTS.md').write_text('\n'.join(lines),encoding='utf-8')
