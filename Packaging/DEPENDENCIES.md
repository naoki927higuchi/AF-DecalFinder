# 1.9.2 配布方式・依存関係

## 方式比較

|方式|判断|
|---|---|
|.NET self-contained win-x64、フォルダー出力|採用。Microsoft.NETCore.App / WindowsDesktop.App 9.0.20を内包。端末の.NETを探索しない。単一EXE化・トリミングは不要で、WinFormsの機能を保持する。|
|PythonワーカーEXE化（PyInstaller）|不採用。凍結用フックと動的DLL検出の追加検証が必要で、NumPy/OpenCV/Tesseractの実体容量は消えない。PyInstallerのGPL＋bootloader例外は実行物をGPL化しないが、今回追加する理由がない。凍結版の容量・性能は未測定。|
|Python embeddable 3.12.10 x64|採用。公式ZIPと固定wheelを展開し、python312._pthで同梱パスだけを許可。site/user-site/PYTHONPATHを使わない。pip不要。開発環境3.12.14との差は同一600dpi画像の全レコード（ランダムID除外）で照合。|
|Tesseract本体|既存のtesserocr経由を維持。Tesseract EXEやpytesseractを新規導入せず、wheel内のエンジンDLL・Leptonica・間接DLLとengデータを同梱する。|

公式資料：[.NET配布](https://learn.microsoft.com/en-us/dotnet/core/deploying/)、[Python embeddable](https://docs.python.org/3.12/using/windows.html#the-embeddable-package)、[tesserocr 2.12.0](https://pypi.org/project/tesserocr/2.12.0/)、[PyInstallerライセンス](https://pyinstaller.org/en/stable/license.html)。

## 直接依存と必要ファイル

|正式名称・版|用途・ファイル|ライセンス／再配布条件|
|---|---|---|
|Microsoft .NET / Windows Desktop Runtime 9.0.20|ルートのcoreclr/hostfxr/hostpolicy、System.*、Windows Forms/WPF等publish出力|MIT＋第三者通知。全文と通知を同梱。|
|CPython 3.12.10 Windows embeddable x64|Ocr/pythonのEXE、python312.dll、標準ライブラリZIP、pyd/DLL|PSF-2.0および歴史的ライセンス、第三者条件。全文保持。|
|NumPy 2.5.4|Ocr/.deps/numpy、numpy.libs|BSD-3-Clause、内蔵OpenBLAS/LAPACK/GCC例外等。著作権・免責・全文保持。|
|opencv-python-headless 5.0.0.93 / OpenCV 5.0.0|Ocr/.deps/cv2とdist-info|wheel包装MIT、OpenCV Apache-2.0、下表静的依存。通知保持。|
|Pillow 12.3.0|Ocr/.deps/PIL|MIT-CMUと画像コーデック条件。全文・FTL表示保持。|
|psutil 7.2.2|Ocr/.deps/psutil|BSD-3-Clause。全文保持。|
|tesserocr 2.12.0|Ocr/.deps/tesserocr、tesserocr.libs|MITおよび次表のDLLライセンス。|
|Tesseract fast English model|Ocr/tessdata/eng.traineddata、4,113,088 bytes|Apache-2.0。SHA256 7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2。固定ハッシュで公式tessdata_fastと照合。|

wheelの公式配布URL・SHA256・版はlicenses/python-lock.json。実際に必要なファイル名・ファイルごとのバイト容量・SHA256はFILES.jsonに全件掲載しています。Pythonライブラリの開発用extras（pytest、pywin32、setuptools等）は実行時依存ではなく同梱しません。

## 間接ネイティブ依存

tesserocr.libs内の各DLL（ファイル名にはwheelによるハッシュが付く）：

|ライブラリ|版|ライセンス・必要表示|
|---|---|---|
|Tesseract|5.5.3|Apache-2.0、全文・通知|
|Leptonica|1.87.0|BSD-2-Clause、全文|
|libarchive|3.8.9、lgpl_*構成|BSD系ほか集合ライセンス。全文。GPL/LZO版不使用|
|GNU libiconv / libcharset|1.18 hc1393d2_3|LGPL-2.1、全文＋対応ソースとWindowsパッチを同梱、交換可能性保持|
|giflib|5.2.2|MIT、全文|
|libdeflate|1.25|MIT、全文|
|LERC|4.2.0|Apache-2.0、全文|
|bzip2|1.0.8|bzip2ライセンス、全文|
|ICU / Unicode data|78.3|ICU/Unicode、全文|
|libjpeg-turbo|3.1.2|IJG/BSD/zlib、全文|
|libpng|1.6.59|libpng、全文|
|libtiff|4.7.2|libtiff、全文|
|zlib|1.3.2|zlib、全文|
|libwebp / mux / sharpyuv|1.6.0|BSD-3-Clause、全文・PATENTS|
|OpenJPEG|2.5.4|BSD-2-Clause、全文|
|OpenSSL|3.6.5|Apache-2.0、全文|
|curl|8.23.0|curl、全文|
|libssh2|1.11.1|BSD-3-Clause、全文|
|XZ liblzma|5.8.3|0BSDほか上流集合、全文（xzコマンドは不使用）|
|libxml2|2.15.4|MIT、全文|
|LZ4|1.10.0|BSD-2-Clause、全文|
|libpsl|0.23.1|MIT、全文|
|Zstandard|1.5.7|BSD-3-Clauseを選択、全文|
|Microsoft MSVCP140 / VCRUNTIME140系|各DLLのファイル版はFILES.jsonと診断資料参照|Microsoft Distributable Code。VSライセンス・REDIST対象・原文を保持。Windowsのみ|

版数はエンジンAPI、DLL版数、conda-forgeパッケージとのコードセクション照合で確認。直接DLLを別の軽量版へ交換しません。Windows標準のKernel32/User32/GDI32/UCRT等はOS提供で、コピーしていません。

OpenCV静的依存：libjpeg-turbo 3.1.2、libpng 1.6.57、libtiff 4.7.1、OpenJPEG 2.5.3、WebP（OpenCV5.0.0に組込、decoder/encoder ABI 0x0210、個別パッチ版はビルド情報で未提示）、zlib、Intel IPP 2026.0.0、IPP-IW、ITT、Protobuf 3.19.1、FlatBuffers 25.9.23、CLAPACK、Khronos OpenCLヘッダー等。個別版が公開ビルド情報にない部分はwheel SHA256で実体を固定します。BSD/MIT/Apache/zlib/IJG/Intel Simplifiedの全文はLICENSE-3RD-PARTY.txtにあります。未使用の動画用FFmpeg DLL（30,876,160 bytes）は削除し、動画機能を配布対象に含めません。

NumPy静的依存：OpenBLAS 0.3.34.106.0、LAPACK、GNUランタイム（GPL-3.0＋GCC Runtime Library Exception）。アプリのソース公開を要求する通常GPLリンクとは区別します。MSVCP140もwheel内にあります。

Pillow静的依存：FreeType 2.14.3（FTL選択）、LittleCMS 2.19（MIT）、WebP 1.6.0（BSD）、AVIF 1.4.2およびAV1コーデック（BSD系・特許条項）、libjpeg-turbo 3.1.4.1（IJG/BSD/zlib）、OpenJPEG 2.5.4（BSD）、zlib-ng 2.3.3（zlib互換1.3.1）、libtiff 4.7.1。全文はPillow LICENSEに収録。コード内に静的に組込まれた部分の個別バイト容量は分離できないため、所属pyd容量で報告します。

Python標準ランタイム：OpenSSL 3.0.16（Apache-2.0）、SQLite 3.49.1（public domain）、Expat 2.7.1（MIT）、zlib 1.3.1、bzip2 1.0.8、libffi 3.4.4（MIT）、XZ liblzma 5.2.5（public domain）、libmpdec 2.5.1等の標準同梱第三者コード。Python LICENSE、追加python-*.txtに条件を保持しています。ビルド時専用パッケージは同梱しません。

## 容量・検証

ファイルごとの実測容量はFILES.json。最終配布ZIPの実測値、同条件のOCR照合、回帰テスト結果はリポジトリのPackaging/RESULTS.mdおよびDistribution/README.mdを参照してください。圧縮率の推測で方式を確定せず、作成したZIPを測定しています。配布版には画像原本、ユーザーキャッシュ、開発用SDK、PDB、テスト入力、pip、PyInstallerを含めません。

licenses/は原文、sources/はLGPL対応ソースです。公開先・製品全体のライセンスは別途決定事項です。実際の別PCとクリーンWindows VMの確認は、この開発PC内でのPATH隔離試験とは区別します。

