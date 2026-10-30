param([string]$Python = "$env:USERPROFILE\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe")
$ErrorActionPreference = 'Stop'
& $Python -c "import PIL; import sys; assert sys.version_info[:2] == (3,12), 'Python 3.12 required for current native dependencies'"
if ($LASTEXITCODE -ne 0) { throw 'Compatible Python 3.12 and Pillow are required' }
$version=(Get-Content "$PSScriptRoot\VERSION.txt" -Raw).Trim()
$destination="$PSScriptRoot\bin\Release-$version\Ocr"
New-Item -ItemType Directory -Force $destination | Out-Null
Copy-Item -LiteralPath "$PSScriptRoot\benchmark.py" -Destination $destination
Copy-Item -Path "$PSScriptRoot\Ocr\*.py" -Destination $destination
Copy-Item -LiteralPath "$PSScriptRoot\.deps" -Destination $destination -Recurse -Force
Copy-Item -LiteralPath "$PSScriptRoot\tessdata" -Destination $destination -Recurse -Force
@{ Python=(Resolve-Path -LiteralPath $Python).Path } | ConvertTo-Json | Set-Content "$destination\runtime.json" -Encoding UTF8
Write-Output "OCR runtime configured: $destination"
