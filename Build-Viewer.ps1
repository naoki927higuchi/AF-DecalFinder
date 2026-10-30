$ErrorActionPreference = 'Stop'
dotnet build "$PSScriptRoot\Viewer\AF.DecalFinder.Viewer.csproj" -c Release --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { throw 'Viewer build failed' }
$version = (Get-Content "$PSScriptRoot\VERSION.txt" -Raw).Trim()
& "$PSScriptRoot\Build-Ocr.ps1"
Write-Output "起動: $PSScriptRoot\bin\Release-$version\AF-DecalFinder.exe"
