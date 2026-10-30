"""Compare fixed 1.4.1 and done overlays under identical rendering load."""
import json
from pathlib import Path

root = Path(__file__).resolve().parent
b, a = [json.loads((root / f'results/edit-{name}.json').read_text(encoding='utf-8-sig'))
        for name in ('before', 'done')]
assert (b['panePixels'], b['frames'], b['repeats']) == (a['panePixels'], a['frames'], a['repeats'])

def mean(case, *keys):
    values = []
    for sample in case['samples']:
        for key in keys:
            sample = sample[key]
        values.append(sample)
    return sum(values) / len(values)

def pair(before, after, *keys):
    return f'{mean(before, *keys):.3f} → {mean(after, *keys):.3f}'

lines = ['# 1.5.0 編集・済み表示の描画性能確認', '',
    '測定日：2026-10-30。前は1.4.1（0070af4）を固定したBeforeEditingImageViewControl、後は1.5.0で全181件を済みにした状態。', '',
    '## 条件', '',
    '- 7783×6579px JPEG、PArgb共有画像、各1838×2016pxの2描画面（32インチ4K相当）。検索49＋全181件選択。',
    '- 25/50/100/200%・右パン/中央枠ドラッグ、同じ座標列30更新×3反復。原本描画・補間・既存Fit再利用は維持し、後だけ全181件の黒半透明表示を追加する。',
    '- 本番MouseMove/OnPaintを再利用するオフスクリーンGDI+測定。OSの描画統合・ディスプレイ遅延を含まない。応答はイベント処理から両面描画終了までの代理指標。',
    '- 平均は3反復の平均。P95は各反復のnearest-rank値の最大。CPU全体比はプロセスCPU時間/実時間/論理CPU数。',
    '- 両版とも5pxドラッグ確定待ち。保存は編集確定時のみで、パン中の保存処理はない。全件済みを追加描画負荷の上限ケースとして測定する。',
    '- Bitmapはアプリがビュー内で生成・破棄したもの。事前デコード・共有画像・描画先とGDI+内部バッファは回数に含まない。', '',
    '## 操作応答（前 → 後）', '',
    '|倍率・操作|平均ms|P95ms|CPU全体比%|Private MiB|Working Set MiB|',
    '|---|---|---|---|---|---|']
for before, after in zip(b['results'], a['results']):
    assert (before['scale'], before['operation']) == (after['scale'], after['operation'])
    p95 = ' → '.join(f"{max(s['response']['p95Ms'] for s in c['samples']):.2f}" for c in (before, after))
    mem = [' → '.join(f'{mean(c, key)/2**20:.1f}' for c in (before, after)) for key in ('privateEndBytes', 'workingEndBytes')]
    lines.append(f"|{before['scale']*100:g}% {before['operation']}|{pair(before,after,'response','meanMs')}|{p95}|{pair(before,after,'cpuPercentMachine')}|{mem[0]}|{mem[1]}|")
lines += ['', '## 各ビュー内訳（前 → 後、時間ms、回数は1反復平均）', '',
    '|倍率・操作・面|MouseMove平均|OnPaint平均|画像描画平均|Overlay平均|Paint回数|Bitmap生成/破棄|',
    '|---|---|---|---|---|---|---|']
for before, after in zip(b['results'], a['results']):
    for view in ('overview', 'detail'):
        cells = [pair(before, after, view, key, 'meanMs') for key in ('mouse','paint','image','overlay')]
        bitmap = ' → '.join(f"{mean(c,view,'BitmapCreations'):g}/{mean(c,view,'BitmapDisposals'):g}" for c in (before,after))
        lines.append(f"|{before['scale']*100:g}% {before['operation']} {view}|{'|'.join(cells)}|{pair(before,after,view,'paint','count')}|{bitmap}|")
gc = [sum(sum(s['gcCollections']) for case in data['results'] for s in case['samples']) for data in (b,a)]
lines += ['', f'全測定区間のGC発生合計：{gc[0]} → {gc[1]}。', '',
    '生データ：[変更前](results/edit-before.json)、[変更後](results/edit-done.json)。イベント・描画P95、CPU時間、割当量、メモリ開始/終了値も保持。', '']
(root / 'EDIT-MEASUREMENTS.md').write_text('\n'.join(lines), encoding='utf-8')
print('\n'.join(lines[:25]))
