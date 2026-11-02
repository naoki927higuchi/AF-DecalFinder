param([string]$OutputDirectory, [switch]$Zip)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = (Get-Content "$root/VERSION.txt" -Raw).Trim()
if (!$OutputDirectory) { $OutputDirectory = "$root/bin/Portable-$version" }
$out = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path $out) { throw "Output must be a new directory: $out" }
$downloads = "$PSScriptRoot/downloads"
New-Item -ItemType Directory -Force $downloads | Out-Null
function Fetch($url, $name, $sha) {
    $file = Join-Path $downloads $name
    if (!(Test-Path $file)) { Invoke-WebRequest $url -OutFile $file }
    if ((Get-FileHash $file -Algorithm SHA256).Hash -ne $sha) { throw "SHA256 mismatch: $name" }
    return $file
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$python = Fetch 'https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip' 'python-3.12.10-embed-amd64.zip' '4ACBED6DD1C744B0376E3B1CF57CE906F9DC9E95E68824584C8099A63025A3C3'
$wheels = @(Get-Content "$PSScriptRoot/python-lock.json" -Raw | ConvertFrom-Json | ForEach-Object { Fetch $_.url $_.file $_.sha256 })
dotnet publish "$root/Viewer/AF.DecalFinder.Viewer.csproj" -c Release -r win-x64 --self-contained true -p:RuntimeFrameworkVersion=9.0.20 -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $out
if ($LASTEXITCODE) { throw 'Self-contained publish failed' }
$ocr = "$out/Ocr"
New-Item -ItemType Directory -Force "$ocr/.deps", "$ocr/tessdata" | Out-Null
[IO.Compression.ZipFile]::ExtractToDirectory($python, "$ocr/python")
foreach ($wheel in $wheels) { [IO.Compression.ZipFile]::ExtractToDirectory($wheel, "$ocr/.deps") }
Copy-Item "$root/benchmark.py", "$root/Ocr/worker.py", "$root/Ocr/candidates.py", "$root/Ocr/recognition.py" $ocr
$model = "$root/tessdata/eng.traineddata"
if (!(Test-Path $model)) {
    New-Item -ItemType Directory -Force "$root/tessdata" | Out-Null
    Invoke-WebRequest 'https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/main/eng.traineddata' -OutFile $model
}
if ((Get-FileHash $model).Hash -ne '7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2') { throw 'Unexpected language model' }
Copy-Item $model "$ocr/tessdata"
[IO.File]::WriteAllText("$ocr/python/python312._pth", "python312.zip`n.`n..`n../.deps`n")
[IO.File]::WriteAllText("$ocr/runtime.json", '{"Python":"python/python.exe"}')
# Only prune freshly extracted output; never touch project dependencies or user data.
function Remove-Staged($path) {
    $absolute = [IO.Path]::GetFullPath($path)
    if (!$absolute.StartsWith($out + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe staging path: $absolute" }
    if (Test-Path -LiteralPath $absolute) { Remove-Item -LiteralPath $absolute -Recurse -Force }
}
Get-ChildItem "$ocr/.deps" -Directory -Recurse -Force | Where-Object { $_.Name -in @('tests','test','__pycache__') } | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object { Remove-Staged $_.FullName }
Get-ChildItem $out -File -Recurse -Force | Where-Object { $_.Extension -in @('.pdb','.pyc') -or $_.Name -like 'opencv_videoio_ffmpeg*.dll' } | ForEach-Object { Remove-Staged $_.FullName }
Remove-Staged "$ocr/.deps/bin"
Copy-Item "$PSScriptRoot/licenses", "$PSScriptRoot/sources" $out -Recurse
Copy-Item "$ocr/python/LICENSE.txt" "$out/licenses/CPython.txt"
Get-ChildItem "$ocr/.deps" -Directory -Filter '*.dist-info' | ForEach-Object {
    $target = "$out/licenses/$($_.Name)"; New-Item -ItemType Directory -Force $target | Out-Null
    Get-ChildItem $_.FullName -Force | Where-Object { $_.Name -match '^(licenses|LICENSE.*|COPYING.*)$' } | Copy-Item -Destination $target -Recurse -Force
}
Copy-Item "$PSScriptRoot/README-portable.md" "$out/README.md"
Copy-Item "$PSScriptRoot/THIRD-PARTY-NOTICES.md", "$PSScriptRoot/DEPENDENCIES.md", "$PSScriptRoot/Diagnose.ps1", "$PSScriptRoot/diagnose.py" $out
Copy-Item "$PSScriptRoot/python-lock.json", "$PSScriptRoot/native-provenance.json" "$out/licenses"
$inventory = @(Get-ChildItem $out -Recurse -File -Force | ForEach-Object {
    $relative = $_.FullName.Substring($out.Length + 1).Replace('\','/')
    $category = if ($relative.StartsWith('Ocr/python/')) {'Python'} elseif ($relative.StartsWith('Ocr/')) {'OCR'} elseif ($relative -match '^(licenses|sources)/|\.(md|ps1|py)$') {'Other'} elseif ($relative -match '^AF-DecalFinder|^results/') {'Application'} else {'DotNet'}
    [pscustomobject]@{path=$relative;bytes=$_.Length;category=$category;sha256=(Get-FileHash $_.FullName).Hash;fileVersion=$_.VersionInfo.FileVersion}
})
$inventory | ConvertTo-Json -Depth 4 | Set-Content "$out/FILES.json" -Encoding utf8
$inventory | Group-Object category | ForEach-Object { Write-Output "$($_.Name): $(($_.Group | Measure-Object bytes -Sum).Sum) bytes" }
if ($Zip) {
    & "$PSScriptRoot/Package-Release.ps1" -Directory $out
}
Write-Output "Portable: $out"
