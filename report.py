"""Publish selected raw results and original-size red-box review images."""
import sys,json,csv,shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT/'.deps'))
from PIL import Image,ImageDraw
from evaluate import evaluate,csv_write,inside
SELECTED='circle_psm7_wide'

def main():
 truth=json.loads((ROOT/'ground_truth.json').read_text(encoding='utf-8')); summaries=json.loads((ROOT/'results/summary.json').read_text()); env=json.loads((ROOT/'environment.json').read_text())
 chosen=[s for s in summaries if s['config']==SELECTED];rows_all=[]
 for dpi in (300,600):
  src=ROOT/'results'/SELECTED/f'{dpi}dpi-r1.json';dest=ROOT/'results'/f'{dpi}dpi.json';shutil.copyfile(src,dest);data=json.loads(dest.read_text());_,rows=evaluate(data,truth);rows_all+=rows
  im=Image.open(ROOT/data['image']['path']).convert('RGB');draw=ImageDraw.Draw(im);width=2 if dpi==300 else 3
  for d in data['detections']:
   x,y,w,h=d['bbox'];draw.rectangle((x,y,x+w-1,y+h-1),outline='red',width=width)
  im.save(ROOT/'results'/f'{dpi}dpi-detections.jpg',quality=95,subsampling=0)
  for region in truth['regions']:
   x,y,w,h=region[f'bbox_{dpi}'];crop=im.crop((x,y,x+w,y+h));cd=ImageDraw.Draw(crop)
   for g in truth['annotations']:
    if g['region']==region['id']:
     l,t,gw,gh=g[f'bbox_{dpi}'];cd.rectangle((l-x-2,t-y-2,l-x+gw+1,t-y+gh+1),outline='blue',width=1)
   crop.save(ROOT/'results'/f'{dpi}dpi-{region["id"]}.png')
 csv_write(ROOT/'results/comparison-selected.csv',rows_all)
 lines=['# AF-DecalFinder OCR技術検証 Phase 1 — 1.0.0','',
 '実施日：2026-10-29（日本時間）。アプリ本体・WinForms GUIは実装していない。', '',
 '## 結論','',
 'この図面では、**600dpi原本から丸ラベルを自動検出し、内側をPSM 7で個別認識する設定**を次の検証の推奨とする。全体画像をそのままTesseractに渡す方式は実用性が低い。600dpiで33/38件（86.8%）が完全一致したが、4件の誤認識と1件の検出漏れが残り、完全自動で正確な検索索引を作れる段階とは判断しない。','',
 '評価はこの1枚の図面から目視で選んだ38件・6領域に限定する。設定の選択にも同じ標本を用いたため、独立テストによる汎化性能ではない。全図面の正解数・全体精度は未計測。評価領域内で誤検出0件でも、全図面の候補がすべて正しいという意味ではない。','',
 '## 推奨設定の同一領域比較','',
 '|解像度|全体検出数/種類数|正解/38|誤認識|漏れ|領域内誤検出|完全一致再現率|時間中央値 秒|ピークRSS中央値 MiB|OSピーク最大 MiB|',
 '|---|---|---|---|---|---|---|---|---|---|']
 for s in chosen:lines.append(f'|{s["dpi"]}|{s["total_detections"]}/{s["unique_numbers"]}|{s["correct"]}|{s["wrong_number"]}|{s["miss"]}|{s["false_positive"]}|{s["exact_recall"]:.1%}|{s["seconds_median"]:.3f}|{s["peak_rss_mb_median"]:.1f}|{s["peak_wset_mb_max"]:.1f}|')
 lines+=['','## 全設定の比較','',
 '|設定|dpi|検出数|種類数|正解|誤認識|漏れ|誤検出|秒 中央値 [最小–最大]|RSS MiB 中央値|','|---|---|---|---|---|---|---|---|---|---|']
 for s in summaries:lines.append(f'|{s["config"]}|{s["dpi"]}|{s["total_detections"]}|{s["unique_numbers"]}|{s["correct"]}|{s["wrong_number"]}|{s["miss"]}|{s["false_positive"]}|{s["seconds_median"]:.3f} [{s["seconds_min"]:.3f}–{s["seconds_max"]:.3f}]|{s["peak_rss_mb_median"]:.1f}|')
 lines+=['',
 '全8設定×2解像度を各3回、直列・新規Pythonプロセスで測定。番号と矩形の再現性はsummary.csvのrepeat_detections_identicalで確認。時間はJPEGデコード、エンジン初期化、切出し・前処理、認識、API結果抽出、エンジン終了まで。Python起動/import、重複除去、SHA256計算、JSON・画像の保存は含まない。OSファイルキャッシュはフラッシュしていない。メモリはPython・画像・OpenCV・Tesseractを含むプロセス全体で、10ms間隔のRSS最大値とWindows PeakWorkingSetを記録。C#実装時の時間・メモリを保証する数値ではない。', '',
 '## 設定詳細と根拠','',
 '- Tesseract 5.5.3、tesserocr 2.12.0（ネイティブAPI）、OEM 1/LSTM、公式tessdata_fastのeng。日本語モデルは使わない。OMP_THREAD_LIMIT=1、OpenCV 1スレッド。辞書load_system_dawg/load_freq_dawgは無効。信頼度で候補を落とさない。',
 '- 全体認識はPSM 3/6/11と数字ホワイトリスト0123456789を比較。PSM 11では0123456789()+も比較し、文字単位の結果から数字の連続部分だけを抽出。記号を消して数字を連結する実装にはしていない。ただしOCRが区切りを見落とした連結誤認識はそのまま失敗として残る。',
 '- tileは4×4、各セルの周囲に幅・高さの10%の重なり。矩形中心の所有セルで重複を避け、異なるタイルから出た同一番号でIoU>0.5だけ補助的に重複除去する。離れた同一番号は統合しない。Otsu二値化も比較したが改善しなかった。',
 '- 推奨circle_psm7_wideは画像全体を4×4に分けてHough円検出。300dpi幅3549に対して半径12〜18px、minDist=24、param1=120、param2=22。幅比で半径・距離・param2を調整する。正解位置・正解番号・番号範囲は検出処理に使用しない。',
 '- 円内側（半径から1.3×画像幅比を引く）以外を白にした派生切出しを、横3倍・縦2倍に拡大し、10pxの白余白を付加。PSM 7と数字のみで読む。比較対象circle_psm7は横縦とも2倍。小さく細長い数字に対する横拡大で600dpiの正解が32→33件、300dpiが10→12件に増えた。差は小さく、他図面で再確認が必要。',
 '- 丸ごとに独立して読むため、(102)は102、49＋118は49と118に分離できた。元画像の縮小版はOCRに使っていない。全て原本または原本から直接作成した派生切出しを使用。原本は書き換えない。',
 '- 円ラベルがない図面、楕円・大きさの異なるラベル、円に重なる線ではこの方式の適用範囲外になり得る。半径範囲はこの図面の見た目に合わせた値であり、一般化済みの検出器ではない。', '',
 '## 正解データと採点方法','',
 'OCRを実行する前に300dpi原本の等倍切出しを目視し、頭部8件・足元6件・側面14件・背面5件・下部5件の38件を転記。600dpi原本の対応切出しでも確認した。別に塗料番号・混色比のあるF_paintを負例とし、そこから得た数字はすべて誤検出とする。正解作成コードmake_ground_truth.pyには手入力値のみがあり、OCR出力を読まない。','',
 '両画像は3549×3000と7783×6579pxで寸法比約2.193、ちょうど2倍ではない。SIFT特徴2078対応中1922インライアの相似変換で位置を合わせた。残差中央値0.142px（600dpi座標）。600dpi正解矩形はこの変換で転写し、文字列と位置を目視確認。数字インクを囲む矩形で、円・括弧・＋・引出線は含めない。手描き矩形には300dpiで約±2px程度の不確かさがある。','',
 '番号1桁・2桁・3桁、100以上、重複49/118/120/77/78、括弧、小さい3桁印刷、引出線近接、密集表記を含む。選択は代表例の意図的抽出であり、ランダムサンプリングではない。','',
 '各評価領域に中心が入る検出を対象とし、正解とのIoU≥0.1を候補に、IoU降順で1対1対応させる。番号一致を対応付けに利用しない。対応後の文字列一致=正解、相違=誤認識、対応なし正解=漏れ、対応なし検出=誤検出。融合した複数番号は1件の誤認識と残りの漏れとして表れる。完全一致再現率=正解/38、適合率=正解/(正解+誤認識+誤検出)。IoU閾値は番号の検出有無を寛容に評価するための値であり、矩形の良否は別途IoU≥0.5と中心誤差で評価する。','',
 '|dpi|正しく読めた件数|平均IoU|中心誤差中央値 px|IoU≥0.5の正解|完全一致適合率|','|---|---|---|---|---|---|']
 for s in chosen:lines.append(f'|{s["dpi"]}|{s["correct"]}|{s["mean_iou_correct"]:.3f}|{s["median_center_error_px"]:.3f}|{s["correct_iou_ge_05"]}|{s["exact_precision"]:.1%}|')
 lines+=['','## 推奨設定での全38件の照合','', '|ID|正解|300dpi|600dpi|','|---|---|---|---|']
 for gt in truth['annotations']:
  vals=[]
  for dpi in (300,600):
   row=next(r for r in rows_all if r['gt_id']==gt['id'] and r['dpi']==dpi)
   vals.append('漏れ' if row['status']=='miss' else row['recognized']+(' ✓' if row['status']=='correct' else ' 誤認識'))
  lines.append(f'|{gt["id"]}|{gt["number"]}|{vals[0]}|{vals[1]}|')
 lines+=['','## 失敗傾向と次の検証候補','',
 '600dpi推奨設定でも、前面頭部77→7、頭部1→4、側面51→1、50→90、背面頭部9の漏れが残った。同じ77でも背面は正解となり、番号だけでは成功・失敗を説明できない。円の残り、切出し位置、細い字形、スキャンの局所品質の影響を切り分ける必要がある。信頼度は確率ではなく、閾値だけで正しさを保証できない。', '',
 '300dpiは小さい文字・円の境界を分離しにくく、この方式では多くが無出力になった。全体/タイルPSM 11では600dpiの方が悪化する条件も実際にあった。今回600dpiを推す根拠は解像度の一般論ではなく、円ラベル個別認識の実測差である。','',
 '次は別図面の未使用標本で検証し、円候補がない漏れと円候補はあるがOCR無出力の漏れを分離する。続いて円輪郭除去の改善、縦横比と余白の小規模比較、PSM 8/13やtessdata_bestとの比較を候補とする。学習・他エンジンへの拡張はその結果で判断する。今回それらの効果は未検証。', '',
 '## ファイルと座標仕様','',
 '- [300dpi JSON](results/300dpi.json) / [600dpi JSON](results/600dpi.json)：推奨設定の1回目をそのまま公開。候補は個別レコード。numberは文字列（先頭ゼロを勝手に修正しない）、bbox=[x,y,width,height]、同じ値の個別フィールドも保持。原点は原本左上、xは右、yは下、右端・下端は排他的。画像寸法とSHA256を含む。',
 '- confidenceはTesseractの親word信頼度（0〜100）。数字ごとのsymbol confidenceも保存。複合表記分割の場合にword信頼度を共有することを明示している。候補ごとのraw_wordで失敗の元文字列を確認できる。',
 '- [300dpi赤枠画像](results/300dpi-detections.jpg) / [600dpi赤枠画像](results/600dpi-detections.jpg)：原本と同じ寸法、別ファイル。JPEG確認画像には再圧縮があるが原本・OCR入力に影響しない。',
 '- [目視正解JSON](ground_truth.json)、[推奨設定照合CSV](results/comparison-selected.csv)、[全設定照合CSV](results/comparison-all.csv)、[全設定集計CSV](results/summary.csv)。CSVはExcel向けUTF-8 BOM。',
 '- results/<設定名>/に全3回のJSONと各解像度の照合表。画像は推奨設定のものを出力。results/*dpi-<領域>.pngは赤=検出、青=正解の比較切出し。inspectionのoverviewは目視ナビゲーション用でOCRには使わない。', '',
 '## 実測環境・C#組込みへの引継ぎ','',
 f'{env["cpu"]}、論理{env["logical_cpus"]}CPU、RAM {env["ram_gib"]} GiB、{env["platform"]}。Python 3.12.14、OpenCV {env["opencv"]}、Pillow {env["pillow"]}。学習データSHA256: `7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2`。','',
 'C#へ移す際は同じTesseract/学習データ、円検出・マスク・拡大パラメータ、PSM/OEM/ホワイトリストを再現し、ResultIteratorの文字矩形を拡大率と切出しオフセットで原本座標へ戻す。C#ラッパー、配布形式、WinFormsでの動作・性能は今回未検証。Pythonの実測をC#の性能保証として扱わない。','',
 '公式参考：[Tesseractの画像品質・PSM・文字制限](https://tesseract-ocr.github.io/tessdoc/ImproveQuality.html)、[tesserocr](https://github.com/sirfz/tesserocr)、[tessdata_fast](https://github.com/tesseract-ocr/tessdata_fast)。推奨の根拠は上表の本機実測であり、一般的な性能値を引用したものではない。', '',
 '## 目視比較画像（赤=検出、青=正解）','']
 for n in ['A_head','B_feet','C_side','D_rear','E_bottom','F_paint']:
  lines += [f'### {n}', '',f'![600dpi {n}](results/600dpi-{n}.png)','']
 (ROOT/'REPORT.md').write_text('\n'.join(lines),encoding='utf-8')
 print('REPORT.md, selected JSONs, original-size overlays and comparison crops written.')

if __name__=='__main__':main()
