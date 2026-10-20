param([string]$Dotnet = 'dotnet', [switch]$WithCodecFixtures)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'resolve-dotnet.ps1')
$Dotnet = Resolve-SnapviewDotnet -Dotnet $Dotnet
$projectRoot = Split-Path $PSScriptRoot -Parent
if ($WithCodecFixtures) {
    $fixtures = Join-Path $projectRoot '.tools\codec-fixtures'
    New-Item -ItemType Directory -Force $fixtures | Out-Null
    foreach ($extension in @('heic','avif')) {
        $path = Join-Path $fixtures "example.$extension"
        if (!(Test-Path -LiteralPath $path)) {
            Invoke-WebRequest "https://raw.githubusercontent.com/strukturag/libheif/master/examples/example.$extension" -OutFile $path
        }
    }
    $env:SNAPVIEW_CODEC_FIXTURES = $fixtures
}
try {
    & $Dotnet run --project (Join-Path $projectRoot 'Snapview.csproj') -c Release -- --self-test
    if ($LASTEXITCODE -ne 0) { throw 'Self-tests failed. See bin\Release\net10.0-windows\win-x64\self-test-error.txt.' }
    Get-Content (Join-Path $projectRoot 'bin\Release\net10.0-windows\win-x64\test-output\results.json')
}
finally {
    if ($WithCodecFixtures) { Remove-Item Env:SNAPVIEW_CODEC_FIXTURES -ErrorAction SilentlyContinue }
}
