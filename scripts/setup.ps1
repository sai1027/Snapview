$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$model = Join-Path $projectRoot 'tessdata\eng.traineddata'
if (!(Test-Path -LiteralPath $model)) {
    New-Item -ItemType Directory -Force (Split-Path $model -Parent) | Out-Null
    Invoke-WebRequest 'https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/main/eng.traineddata' -OutFile $model
}
Write-Output 'OCR model ready. Build with .NET 10 SDK: dotnet build -c Release'
