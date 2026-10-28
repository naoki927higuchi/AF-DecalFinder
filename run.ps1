param(
    [string]$Python = "$env:USERPROFILE\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe",
    [int]$Repeats = 3,
    [string]$SourceDir = 'D:\Picture'
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & $Python prepare_inputs.py --source-dir $SourceDir
    if ($LASTEXITCODE -ne 0) { throw 'Input verification failed' }
    & $Python benchmark.py --repeats $Repeats
    if ($LASTEXITCODE -ne 0) { throw 'OCR benchmark failed' }
    & $Python evaluate.py
    if ($LASTEXITCODE -ne 0) { throw 'Evaluation failed' }
    & $Python report.py
    if ($LASTEXITCODE -ne 0) { throw 'Report generation failed' }
    & $Python verify.py
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed' }
} finally { Pop-Location }
