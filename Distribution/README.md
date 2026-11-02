# AF-DecalFinder 配布版

最新版：**1.9.2 / Windows x64 / オールインワン**。

- [配布ZIP（144.42 MB）](AF-DecalFinder-1.9.2-win-x64.zip)
- [SHA256](AF-DecalFinder-1.9.2-win-x64.zip.sha256)
- [起動・操作案内](../Packaging/README-portable.md)
- [依存関係・ライセンス一覧](../Packaging/DEPENDENCIES.md)
- [容量・検証結果・未確認項目](../Packaging/RESULTS.md)

ZIP全体を展開してAF-DecalFinder.exeを起動します。Python・Tesseract・.NET Runtimeの事前導入は不要です。展開後350.02 MB。画像・編集データは配布物に含まれないため、画像の別途コピーとアプリのインポート／エクスポート機能を利用してください。

.NET self-contained 9.0.20、公式Python embeddable3.12.10、固定OCR依存物を同梱。日本語パスでのtessdata読込を修正しました。OCR/棚卸/編集/検索/済み/移行機能は維持。ライセンス原文・NOTICEとLGPL対応ソースを含みます。

開発PCでの隔離試験と自動667項目成功。実際の別PCとクリーンWindows VMは未確認です。ローカル配布物であり、GitHubへの公開・pushは行っていません。
