# AF-DecalFinder: アプリが保存したデータ（設定・作業データ・未紐付けの移行データ）を削除する。
# 画像ファイル、エクスポートした移行ZIP、このフォルダー自体は削除しない。
param([switch]$Yes, [string]$DataPath)
$ErrorActionPreference = 'Stop'
# アプリと同じ方法で保存先を決める（%LOCALAPPDATA%\AF-DecalFinder）。
if (-not $DataPath) { $DataPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'AF-DecalFinder' }

Write-Host 'AF-DecalFinder のデータ削除'
Write-Host ''
if (Get-Process -Name 'AF-DecalFinder' -ErrorAction SilentlyContinue) {
    Write-Host 'AF-DecalFinder が起動しています。終了してから、もう一度実行してください。'
    exit 2
}
if (-not (Test-Path -LiteralPath $DataPath)) {
    Write-Host "削除するデータはありません：$DataPath"
    Write-Host 'このフォルダー（AF-DecalFinder の配布フォルダー）を削除すれば、アンインストールは完了です。'
    exit 0
}
$files = @(Get-ChildItem -LiteralPath $DataPath -Recurse -Force -File)
$bytes = ($files | Measure-Object -Property Length -Sum).Sum
if (-not $bytes) { $bytes = 0 }
Write-Host "次のフォルダーを、中身ごと削除します：$DataPath"
Write-Host ("  ファイル {0} 個、{1:N1} MB" -f $files.Count, ($bytes / 1MB))
Write-Host '  設定、番号の修正・済み・棚卸などの作業データ、未紐付けの移行データが消えます。元に戻せません。'
Write-Host '  説明書の画像ファイルと、エクスポートした移行ZIPは消えません。'
Write-Host ''
if (-not $Yes) {
    $answer = Read-Host '削除してよければ y を入力して Enter を押してください'
    if ($answer -notin @('y', 'Y')) { Write-Host '中止しました。何も削除していません。'; exit 1 }
}
Remove-Item -LiteralPath $DataPath -Recurse -Force
Write-Host '削除しました。'
Write-Host 'このフォルダー（AF-DecalFinder の配布フォルダー）を削除すれば、アンインストールは完了です。'
exit 0
