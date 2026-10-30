"""Summarize within-condition ABBA runs; preserve the first sequential comparison."""
import json
from pathlib import Path

root = Path(__file__).resolve().parent
data = json.loads((root / 'results/edit-paired.json').read_text(encoding='utf-8-sig'))

def samples(scale, operation, variant):
    return [s for c in data['results'] if (c['scale'], c['operation'], c['variant']) == (scale, operation, variant) for s in c['samples']]

def mean(items, *keys):
    values = []
    for sample in items:
        for key in keys:
            sample = sample[key]
        values.append(sample)
    return sum(values) / len(values)

lines = ['# Phase 5 交互測定による描画性能確認', '',
    '2026-10-30。初回の逐次実行では変更のない画像描画にも大きな時間差があったため、同じ倍率・操作内でA→B→B→Aを実行。A=1.4.1固定コード、B=1.5.0全181件済み表示。', '',
    '原本7783×6579px、描画面1838×2016px×2、各30更新×1反復×2回/版。検索49と選択181件は共通。初回測定と同じCPU側GDI+ハーネスで、実モニタ表示遅延を含まない。平均は各版2回の平均、P95は各30点のP95の最大。CPU周波数や他プロセスは固定していない。', '',
    '|倍率・操作|応答平均ms A→B|応答P95ms A→B|中央OnPaint平均 A→B|右OnPaint平均 A→B|Overlay合計平均 A→B|CPU全体比% A→B|',
    '|---|---|---|---|---|---|---|']
for scale in (.25, .5, 1, 2):
    for operation in ('detail-pan', 'overview-drag'):
        before, after = [samples(scale, operation, v) for v in ('before-editing', 'done')]
        assert len(before) == len(after) == 2
        pair = lambda *keys: ' → '.join(f'{mean(s, *keys):.2f}' for s in (before, after))
        p95 = ' → '.join(f"{max(x['response']['p95Ms'] for x in s):.2f}" for s in (before,after))
        overlay = ' → '.join(f"{sum(mean(s,v,'overlay','meanMs') for v in ('overview','detail')):.2f}" for s in (before,after))
        lines.append(f"|{scale*100:g}% {operation}|{pair('response','meanMs')}|{p95}|{pair('overview','paint','meanMs')}|{pair('detail','paint','meanMs')}|{overlay}|{pair('cpuPercentMachine')}|")
all_samples = [s for c in data['results'] for s in c['samples']]
lines += ['', f"全条件Private Bytes：{min(s['privateEndBytes'] for s in all_samples)/2**20:.1f}〜{max(s['privateEndBytes'] for s in all_samples)/2**20:.1f}MiB。Working Set：{min(s['workingEndBytes'] for s in all_samples)/2**20:.1f}〜{max(s['workingEndBytes'] for s in all_samples)/2**20:.1f}MiB。",
    f"パン中Bitmap生成/破棄合計：{sum(s[v]['BitmapCreations'] for s in all_samples for v in ('overview','detail'))}/{sum(s[v]['BitmapDisposals'] for s in all_samples for v in ('overview','detail'))}。GC発生合計：{sum(sum(s['gcCollections']) for s in all_samples)}。", '',
    '[交互測定の生データ](results/edit-paired.json)。初回の大きな時間差も[逐次比較](EDIT-MEASUREMENTS.md)に残す。', '']
(root / 'EDIT-PAIRED.md').write_text('\n'.join(lines), encoding='utf-8')
print('\n'.join(lines))
