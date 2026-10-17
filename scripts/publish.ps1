param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
& $Dotnet publish (Join-Path $projectRoot 'Snapview.csproj') -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o (Join-Path $projectRoot 'artifacts\Snapview')
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.ps1') -Destination (Join-Path $projectRoot 'artifacts\install.ps1')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination (Join-Path $projectRoot 'artifacts\uninstall.ps1')
foreach ($file in @('README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'AGENTS.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination (Join-Path $projectRoot "artifacts\$file")
}
foreach ($folder in @('docs', 'licenses')) {
    $destination = Join-Path $projectRoot "artifacts\$folder"
    New-Item -ItemType Directory -Force $destination | Out-Null
    Copy-Item -Path (Join-Path $projectRoot "$folder\*") -Destination $destination -Recurse -Force
}
Write-Output 'Portable build ready: artifacts\Snapview\Snapview.exe. Run artifacts\install.ps1 for a stable per-user installation.'
