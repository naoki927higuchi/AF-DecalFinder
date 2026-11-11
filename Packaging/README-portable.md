# AF-DecalFinder 1.11.1 ポータブル版

Windows 10/11 x64用です。フォルダー全体を展開して `AF-DecalFinder.exe` を起動してください。ZIP内から直接起動せず、EXEだけを移動しないでください。Python、Tesseract、.NET Runtimeの追加インストール・PATH設定は不要です。ARM64ネイティブ版、32bit Windowsは対象外です。

画像をD&Dまたは「ファイル」から開きます。有効な編集データがなければ同梱OCRが自動解析します。検索文字列を入力すると赤色で強調し、Enterで先頭へ移動します。一覧のEnter/F2で編集、通常モードのSpaceで先頭1件の貼付済み反転、棚卸モードのSpaceで確認、Ctrl+Enterで選択全件を確認します。画像Ctrl+クリックは選択反転、Ctrl+ダブルクリックは直接編集です。UNDO、移行ZIP、画像フォルダー管理等は1.9.1と同じです。

スマホで撮った写真は、EXIFの回転情報に従って正しい向きで表示・解析します（1.10.0以降）。推奨は説明書の600dpi前後のスキャンです。

## 別PCへ作業データを移す

開発PCのアプリのエクスポート機能で設定・デカール情報の移行ZIPを作り、別PCでインポートしてください。**この配布ZIPと作業データの移行ZIPは別物です。** 画像原本は含まれません。別途画像をコピーして画像フォルダーを指定します。未紐付け情報は保持され、後から画像を開いた時も紐付けられます。コピー元のデータは残してください。

設定と編集情報の保存先は従来どおり `%LOCALAPPDATA%\AF-DecalFinder\` です。配布フォルダーへ既存キャッシュを同梱していません。更新は新しいフォルダーへ展開してください。有効な旧キャッシュは再解析せず読み込みます。

## アンインストール

1. AF-DecalFinderを終了します。
2. 配布フォルダーの `Uninstall.bat` をダブルクリックします。アプリが保存したデータ（`%LOCALAPPDATA%\AF-DecalFinder\` の設定・作業データ・未紐付けの移行データ）の場所と件数を表示し、`y` を入力すると削除します。元に戻せません。説明書の画像とエクスポートした移行ZIPは削除しません。
3. 配布フォルダーを、フォルダーごと削除します。

インストーラーやレジストリは使っていないため、これでアンインストールは完了です。作業データを残したい場合は、先に「設定・データをエクスポート」で移行ZIPを作ってください。

## 問題があるとき

- フォルダー全体が展開されているか、`Ocr/python/python.exe`、`Ocr/.deps`、`Ocr/tessdata/eng.traineddata` があるか確認してください。
- PowerShellで `powershell -NoProfile -ExecutionPolicy Bypass -File .\Diagnose.ps1` を実行すると、同梱ライブラリの読込・言語データと実際のロード先を確認できます。この指定はその診断プロセスだけに適用され、OSの実行ポリシーを変更しません。
- OCRも確認する場合は `-Image 'C:\画像\図面.jpg'` を追加します。診断は画像を読取り、結果を標準出力へ出します。編集キャッシュは作成・更新しません。
- アプリは未署名です。セキュリティソフトが隔離した場合は配布元とSHA256を確認してください。
- OCR失敗時も画像閲覧は可能です。診断結果とエラー内容を記録してください。検出0件は実行失敗とは異なります。

## ライセンス

AF-DecalFinder本体は Apache License 2.0 です（`LICENSE`、`NOTICE`）。Copyright 2026 af-ailab (https://af-ailab.com/)。

同梱した.NET、Python、Tesseract、OpenCVなどの第三者コンポーネントは、それぞれのライセンスに従います。再配布する場合は、`LICENSE`、`NOTICE`、`THIRD-PARTY-NOTICES.md`、`licenses/`、`sources/` を含めたフォルダー全体のまま配布してください。

## 同梱物

依存関係・容量の説明は `DEPENDENCIES.md`、ライセンス案内は `THIRD-PARTY-NOTICES.md`、全文は `licenses/`、LGPL対応ソースは `sources/` にあります。`FILES.json` はファイル容量・SHA256・分類の一覧です（一覧自身のハッシュは除外）。第三者文書を削除せずフォルダー全体を扱ってください。

Microsoft Distributable CodeにはMicrosoftの条件が適用されます。Microsoftの著作権表示等を削除せず、Windows上で本アプリの一部として使用してください。Microsoftによる推奨・保証を意味しません。Microsoftコンポーネントへの制限は、LGPLその他のオープンソース部分の権利を制限しません。再配布者と利用者も、Microsoft Distributable Codeについてこれらの条件に従ってください。
