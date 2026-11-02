# Third-party notices / 再配布資料

各コンポーネントの著作権と条件は `licenses/` の原文が適用されます。上流wheelのライセンス集合は省略せず保持しています。集合内にはビルド環境用・任意機能用の文書もありますが、それらすべてのバイナリを同梱しているという意味ではありません。

|コンポーネント|ライセンス文書|
|---|---|
|.NET / Windows Desktop 9.0.20|dotnet-LICENSE.txt、dotnet-NOTICES.txt、winforms-LICENSE.txt、winforms-NOTICES.txt、wpf-NOTICES.txt|
|CPython 3.12.10|CPython.txt（PSFおよび同梱第三者）、python-*.txt|
|NumPy / OpenBLAS / LAPACK / GNU runtime|numpy-2.5.4.dist-info/licenses/LICENSE.txt（全文・GCC Runtime Library Exceptionを含む）|
|OpenCVと静的組込ライブラリ|opencv_python_headless-5.0.0.93.dist-info/ のLICENSE、LICENSE-3RD-PARTY.txt|
|Pillowと画像コーデック|pillow-12.3.0.dist-info/licenses/LICENSE|
|psutil|psutil-7.2.2.dist-info/LICENSE|
|tesserocr|tesserocr-2.12.0.dist-info/licenses/LICENSE|
|Tesseract / Leptonicaと間接DLL|tesserocr-2.12.0.dist-info/licenses/.wheel-licenses/各ライブラリ/（隠し名も保持）|
|eng.traineddata|tessdata-LICENSE.txt（Apache-2.0）|
|Microsoftランタイム|python-crt.txt、Visual-Studio-2022-License.docxとREDIST.html、.wheel-licenses/vc14_runtime/の原文DOCX|

MIT/BSD/Apache/PSF/zlib等の表示・全文保持条件を満たすため、著作権表示を含む原文を同梱します。ApacheのNOTICE相当は各集合に含めます。NumPy内のGCCランタイムはRuntime Library Exceptionの適用対象です。FreeTypeはFTLを選択します。Portions of this software are copyright © The FreeType Project (https://www.freetype.org). All rights reserved.

## GNU libiconv / libcharset 1.18

このDLL部分はGNU LGPL 2.1に従います。アプリ全体へGPLを適用するものではありません。`sources/libiconv-1.18.tar.gz` に完全な上流ソース、`sources/libiconv-build/` に実際のconda-forge Windowsビルドレシピ・パッチを同梱します。バイナリのPE .textをconda-forge 1.18 hc1393d2_3と照合済みです。`licenses/native-provenance.json` に照合値があります。

利用者はLGPL部分を改変・再ビルドし、互換DLLへ交換できます。この目的の解析・リバースエンジニアリングを妨げる制限を設けません。Windows x64/MSVCとCMakeを使い、レシピのmeta.yaml、parentの3パッチ、parent/bld.batとinstall.batに従ってビルドします。Ocr/.deps/tesserocr.libsのiconv-*およびcharset-* DLLを対応する互換ライブラリに置き換えてください。wheelのdelvewheelによる名前変更・import参照に合わせたDLL名が必要です。ビルドと置換の詳細はsources/README.mdを参照してください。

動画用FFmpeg DLLは同梱しません（静止画OCRでは使いません）。OpenCVの上流第三者文書は原文保持のためFFmpegの記載を含みます。TesseractのlibarchiveはGPL/LZO版ではなく、上流tesserocrが指定するlgpl_*ビルドです。GPL版iconvコマンドやxzコマンドも同梱しません。

Microsoft CRTの再配布はVisual StudioのDistributable Code条件に従います。この開発PCにはVisual Studio 2022 Professionalが導入されています。第三者DLLを単体商品として再配布するものではありません。再配布者はVisual Studioの権利と該当REDIST一覧を確認してください。Pythonとwheelに元から同梱されたランタイムを使用し、システムのDLLを収集・コピーしていません。


