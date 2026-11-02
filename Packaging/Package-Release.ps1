param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$version=(Get-Content "$root/VERSION.txt" -Raw).Trim()
$out=(Resolve-Path -LiteralPath $Directory).Path
$archive="$root/Distribution/AF-DecalFinder-$version-win-x64.zip"
if(Test-Path $archive){throw "Existing release archive must not be replaced: $archive"}
if(!(Test-Path "$out/AF-DecalFinder.exe") -or !(Test-Path "$out/Ocr/python/python.exe")){throw 'Not a portable package'}
$files=@(Get-ChildItem $out -Recurse -File -Force | Where-Object { $_.FullName -ne "$out\FILES.json" } | ForEach-Object {
    $relative=$_.FullName.Substring($out.Length+1).Replace('\','/')
    $category=if($relative.StartsWith('Ocr/python/')){'Python'}elseif($relative.StartsWith('Ocr/')){'OCR'}elseif($relative -match '^(licenses|sources)/|\.(md|ps1|py)$'){'Other'}elseif($relative -match '^AF-DecalFinder|^results/'){'Application'}else{'DotNet'}
    [pscustomobject]@{path=$relative;bytes=$_.Length;category=$category;sha256=(Get-FileHash $_.FullName).Hash;fileVersion=$_.VersionInfo.FileVersion}
})
$files | ConvertTo-Json -Depth 4 | Set-Content "$out/FILES.json" -Encoding utf8
$sizes=@($files | Group-Object category | ForEach-Object {[pscustomobject]@{category=$_.Name;bytes=($_.Group|Measure-Object bytes -Sum).Sum}})
Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Force "$root/Distribution" | Out-Null
[IO.Compression.ZipFile]::CreateFromDirectory($out,$archive,[IO.Compression.CompressionLevel]::Optimal,$true)
$hash=(Get-FileHash $archive).Hash
"$hash  $([IO.Path]::GetFileName($archive))" | Set-Content "$archive.sha256" -Encoding ascii
[ordered]@{version=$version;folderBytes=(Get-ChildItem $out -Recurse -File -Force|Measure-Object Length -Sum).Sum;zipBytes=(Get-Item $archive).Length;zipSha256=$hash;filesManifestBytes=(Get-Item "$out/FILES.json").Length;categories=$sizes} | ConvertTo-Json -Depth 4 | Set-Content "$PSScriptRoot/capacity.json" -Encoding utf8
Get-Content "$PSScriptRoot/capacity.json"
