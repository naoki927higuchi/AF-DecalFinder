# AF-DecalFinder 1.9.2 — 別PC向けリリース検証

検証日：2026-11-02。基準は1.9.1（e436178）。配布方法の変更に伴うパッチ版。OCRアルゴリズム・モデル・前処理・UI機能・データ形式は変更していません。

## 成果物と容量

配布フォルダー：`bin/AF-DecalFinder-1.9.2-win-x64/`。配布ZIP：`Distribution/AF-DecalFinder-1.9.2-win-x64.zip`。

|項目|バイト|MB（10進）|
|---|---:|---:|
|展開後合計|350,020,499|350.02|
|ZIP|144,419,358|144.42|
|.NET関連|119,754,608|119.75|
|Pythonランタイム・標準ライブラリ|22,501,247|22.50|
|OCR関連（Python追加ライブラリ、ネイティブDLL、モデル、ワーカー）|199,480,756|199.48|
|アプリ本体・初期JSON|1,036,011|1.04|
|その他（ライセンス、LGPLソース、案内、診断）|6,997,637|7.00|
|FILES.json自身|250,240|0.25|

ZIPは137.73 MiB、展開後333.81 MiB。200,000,000 bytes基準でも合格です。削減のためにOCRライブラリやモデルを変更していません。静止画OCRに不要なOpenCV動画プラグイン30,876,160 bytes、tests・pyc・PDB・開発用scriptsを除外しました。展開後1,115ファイル（FILES.jsonに自身を除く1,114ファイルを記録）。ビルド依存、画像原本、実ユーザーキャッシュは配布物に含みません。

SHA256：`8BBF9F62ACFF469FEF5A9A7369CC84D94601CD3BC4DC52B299AF5934A25969E1`。

## 採用方式・依存監査

.NET9.0.20 self-contained win-x64 + 公式CPython3.12.10 embeddable + 固定wheel + tesserocrのTesseract5.5.3/Leptonica1.87.0 DLL + eng.traineddata。EXEを凍結するPython方式は採用せず、フォルダー単位で配布します。比較・版・ライセンス・必要ファイルは[DEPENDENCIES.md](DEPENDENCIES.md)、著作権表示と条件は[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。wheelのURL/SHA256と実ファイルのハッシュを固定しています。

MIT/BSD/Apache/PSF等の原文・通知をlicensesへ同梱。GNU libiconv/charset1.18はLGPL2.1のため、対応するGNU完全ソースと実際のconda-forgeコミットのWindowsパッチ・ビルドレシピもsourcesへ同梱しました。元conda DLLとwheel DLLのコードセクションを照合済み。MSVCP/VCRUNTIMEはVisual Studioの再配布条件・REDIST一覧と同梱ライセンスを確認し、原文を保持しています。一般公開する製品全体のライセンスは未決定で、今回は本人の別PC用の非公開配布です。

静的PE import/delay-importを全EXE/DLL/PYDについて検査しました（[pe-imports.json](pe-imports.json)）。同梱外はWindows OS標準DLL/API-setのみです。動的なPythonロード先も検査し、同梱フォルダーまたはWindows OS/日本語LanguageExperiencePackだけでした。開発Python、ユーザーsite-packages、システムTesseractは参照していません。実アプリのhostfxr/hostpolicy/coreclr/本体DLLも配布フォルダーからロードされました。

## テスト条件と結果

Windows開発PC上で、PATHをSystem32だけ、PYTHONHOME/PYTHONPATH/DOTNET_ROOTを存在しないフォルダー、DOTNET_MULTILEVEL_LOOKUP=0にして実行。起動時の作業ディレクトリも配布先とは別にしました。既存ソフトウェアをアンインストールした検証ではありません。

- Releaseビルド／self-contained publish：成功、警告0・エラー0。
- 既存機能回帰：**558項目成功**。既存202、編集/再解析34、棚卸/UNDO34、移行26、画像直接編集26、Ctrl反転18、線幅106、統一矩形/フィルタ40、キー/塗り72。
- 同梱OCR統合：**98項目成功**。8原本、初回OCR、再読込、画像別修正・手動追加・済み復元、更新日時/サイズの不一致、旧キャッシュ退避、遅延結果の混入抑止、0件、失敗、取消し、25/50/100/200%パン。
- 配布専用：**11項目成功**。相対Pythonパス、任意cwd、非同期実行、実OCR、旧181件保持、キャッシュ再読込、固定ID/編集/済み/手動追加復元、棚卸、原本不変。同じ11項目を最終ZIPの日本語・空白を含む新しい展開先でも再実行。
- 独立した自動確認項目は合計**667項目**。再実行分を水増ししていません。ZIP全1,114掲載ファイルのSHA256一致と診断コマンドも別途確認。
- 既存の画像フォルダー検索・曖昧一致・遅延紐付け・移行先の旧データ保護は26項目の移行テストで確認。破壊的な検証は専用テストストアで行い、利用中の編集データを使用していません。

実画面では実際の配布EXEを隔離環境で起動し、600dpiの既存テスト画像キャッシュ276件、2ビュー、102検索・Enterパン、画像クリック逆引き、青枠と赤枠、タイトルのアイコンを確認しました。EXEアイコン抽出も既存自動テストに含みます。利用者が開いていた1.9.1ウィンドウは終了していません。実画面操作前後の既存Cacheファイルは全件SHA256不変でした。

## 600dpiの再現性

開発版1.9.1（Python3.12.14）と配布版（Python3.12.10）を同一原本で実行し、276候補の位置と全276レコードの番号・bbox・信頼度・未認識フラグ・circle情報が一致しました。実行ごとに新規発行するIDだけを比較から除外しています。196件は数字認識、80件は未認識。Phase1の181件は番号/bbox一致で全件保持しています。「全候補が181件」という意味ではありません。

ワーカー単体の1回測定：開発版5.548秒、配布版（日本語展開先）5.275秒、ピークRSS547.46 MiB。速度改善を目的とした比較ではなく、測定揺れを含むため高速化とは評価しません。アプリからの8画像測定は[OCR-INTEGRATION-192.md](../OCR-INTEGRATION-192.md)に記録しています。600dpiはOCRプロセス起動から結果検証まで5.417秒、画像表示を含む初回8.806秒、キャッシュ再読込2.142秒でした。

## 検証で修正した問題

日本語展開先ではTesseractのWindows narrow-path APIが絶対tessdataパスを開けませんでした。ワーカーが入力画像パスを先に絶対化してからOcrへcwdを設定し、tessdataを相対参照することで解決しました。画像読込はPillowのUnicodeパスのままです。日本語展開先で8画像98項目を再確認済み。Phase1のbenchmark.pyは変更していません。

既存OCRテストはinputs直下を列挙して8枚と仮定していたため、利用者が追加した同じ原本の別名コピーにより事前条件が失敗しました。テストだけを既存目視評価データに記録された8画像へ固定し、追加画像を削除せず再実行しました。

## 未確認と利用先での確認

実際のプラモ部屋PC、完全なクリーンWindows VM、Windows10実機、他CPU/ARM64、長時間運用、別PCのExplorer/タスクバーのアイコンキャッシュは未確認です。開発PCで環境変数を隔離した結果を「クリーンOS検証済み」とは扱いません。別PCではZIP全体を展開し、起動→画像OCR→移行ZIPのインポート→棚卸/編集/済み→終了・再起動を確認してください。画像原本と旧編集データのバックアップは残してください。

## 再生成

開発側に.NET9 SDKとネットワーク接続が必要です（利用側には不要）。`Packaging/Build-Portable.ps1 -OutputDirectory <新規フォルダー>` で生成。依存ダウンロードは固定SHA256で検証します。公開済み同版ZIPは上書きしません。新バージョンで `-Zip` を指定するか `Packaging/Package-Release.ps1` で最終ZIPを作成します。build-time PE監査だけはpefileを使います。配布にpipやpefileは含めません。

自動確認：Checksのself-contained出力で `<repository> --portable <配布フォルダー>`、`<repository> --portable-ocr <配布フォルダー/Ocr>`。通常の回帰スイートは既存オプションのままです。
