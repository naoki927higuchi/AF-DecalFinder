# AF-DecalFinder 配布版

最新版：**1.11.1 / Windows x64 / オールインワン**。

配布ZIP（`AF-DecalFinder-1.11.1-win-x64.zip`、144.43 MB）は、このリポジトリのGitHub Releasesからダウンロードしてください。ZIPはGitにコミットしていません。

- [SHA256](AF-DecalFinder-1.11.1-win-x64.zip.sha256)
- [起動・操作案内](../Packaging/README-portable.md)
- [依存関係・ライセンス一覧](../Packaging/DEPENDENCIES.md)
- [第三者ライセンス](../Packaging/THIRD-PARTY-NOTICES.md)
- [容量・検証結果・未確認項目](../Packaging/RESULTS.md)

ZIP全体を展開してAF-DecalFinder.exeを起動します。Python・Tesseract・.NET Runtimeの事前導入は不要です。展開後350.04 MB。アンインストールは、同梱の `Uninstall.bat` で保存データを削除してからフォルダーごと削除します。画像・編集データは配布物に含まれないため、画像の別途コピーとアプリのインポート／エクスポート機能を利用してください。

AF-DecalFinder本体はApache License 2.0です（[LICENSE](../LICENSE)、[NOTICE](../NOTICE)）。同梱の第三者コンポーネントはそれぞれのライセンスに従います。再配布するときは、LICENSE、NOTICE、THIRD-PARTY-NOTICES.md、licenses/、sources/ を含めたまま配布してください。

1.11.1での主な変更（1.9.3から）：

- スマホで撮った写真のEXIF回転情報に従い、正しい向きで表示・解析（1.10.0）
- 正立させたスマホ写真で番号の丸を見落とす問題を改善。スキャン画像の結果は変わりません（1.11.0）
- 保存データを削除する `Uninstall.bat` を同梱（1.11.0）
- 拡大率の異なるモニター間でウィンドウを移すと、ステータスバーの文字が消える不具合を修正（1.11.1）

OCRの依存物（Python・Tesseract・OpenCVなど）は1.9.3と同一です。詳細は [HISTORY](../HISTORY.md) を参照してください。
