param([string]$Image)
$ErrorActionPreference = 'Stop'
$python = Join-Path $PSScriptRoot 'Ocr/python/python.exe'
& $python -B "$PSScriptRoot/diagnose.py"
if ($LASTEXITCODE) { throw 'Runtime diagnosis failed' }
if ($Image) {
    $absolute = (Resolve-Path -LiteralPath $Image).Path
    & $python -B "$PSScriptRoot/Ocr/worker.py" $absolute
    if ($LASTEXITCODE) { throw 'OCR diagnosis failed' }
}
