param([string]$Python = "$env:USERPROFILE\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe")
$ErrorActionPreference = 'Stop'
& $Python -m pip install --target "$PSScriptRoot\.deps" -r "$PSScriptRoot\requirements.txt"
if ($LASTEXITCODE -ne 0) { throw 'Dependency installation failed' }
New-Item -ItemType Directory -Force "$PSScriptRoot\tessdata" | Out-Null
Invoke-WebRequest 'https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/main/eng.traineddata' -OutFile "$PSScriptRoot\tessdata\eng.traineddata"
$digest = (Get-FileHash "$PSScriptRoot\tessdata\eng.traineddata" -Algorithm SHA256).Hash
if ($digest -ne '7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2') { throw 'Model hash changed; do not compare with the recorded benchmark.' }
