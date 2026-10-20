function Resolve-SnapviewDotnet {
    param([string]$Dotnet = 'dotnet')

    $candidates = @($Dotnet)
    if ($Dotnet -eq 'dotnet') {
        $candidates += Join-Path $env:LOCALAPPDATA 'Snapview\dotnet\dotnet.exe'
        $candidates += Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    }
    foreach ($candidate in $candidates) {
        $command = Get-Command -Name $candidate -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($command) { return $command.Source }
    }
    throw "Cannot find '$Dotnet'. Install the .NET 10 SDK or pass -Dotnet 'C:\path\to\dotnet.exe'. The scripts also check %LOCALAPPDATA%\Snapview\dotnet and %ProgramFiles%\dotnet when using the default."
}
