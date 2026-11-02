# libiconvの対応ソース

libiconv-1.18.tar.gz（SHA256 3b08f5f4f9b4eb82f151a7040bfd6fe6c6fb922efe4b1659c66ea933276965e8）は https://ftp.gnu.org/pub/gnu/libiconv/libiconv-1.18.tar.gz の完全なソースです。

libiconv-buildは https://conda.anaconda.org/conda-forge/win-64/libiconv-1.18-hc1393d2_3.conda のinfo/recipeを保持し、parentのレシピとパッチはビルド記録のGitコミット c7fdcb1b68d6579a08d0c5dc5f34cbbf38780538 から完全な原文を取得しています。meta.yamlにコンパイラー・CMake・ホスト依存版数、parentにパッチ、parent/bld.batにビルド・テスト、install.batに配置コマンドがあります。上流ソースを展開し、meta.yamlに記載された順序でparentの3パッチを適用してWindows x64のVisual Studio開発環境でparent/bld.batとinstall.bat相当を実行します。conda-buildを使用する場合は同じレシピを使用してください。

本配布ではtesserocr wheelによってdelvewheelでDLL名がハッシュ付きへ変更されています。ライブラリのコードセクションは元conda DLLと一致します。交換時はiconvとcharsetの両方を対象にします。互換DLLを元の名前に配置したwheelを `delvewheel repair --add-path <DLLフォルダー>` で修復するか、既存のハッシュ付き名とDLL間import名を維持して交換してください。Python3.12 x64/tesserocr2.12.0のABIを維持します。上流ビルドスクリプトは https://github.com/sirfz/tesserocr/tree/v2.12.0/.github/build-scripts です。

元の配布を別フォルダーへ残し、Diagnose.ps1でロードを確認してから画像OCRを試してください。利用者による再ビルドは今回の配布動作テストの対象外です。

