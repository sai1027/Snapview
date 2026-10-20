$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'Snapview'
$destination = Join-Path $env:LOCALAPPDATA 'Programs\Snapview'
if (!(Test-Path -LiteralPath (Join-Path $source 'Snapview.exe'))) { throw 'Run this script from the published artifacts folder beside the Snapview folder.' }
if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1'))) { throw 'Keep uninstall.ps1 beside install.ps1.' }
if (Get-Process Snapview -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $destination 'Snapview.exe') }) { throw 'Close the installed Snapview before updating.' }
New-Item -ItemType Directory -Force $destination | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $destination -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination (Join-Path $destination 'uninstall.ps1') -Force
$executable = Join-Path $destination 'Snapview.exe'
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Snapview.lnk'
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
$shortcut.TargetPath = $executable
$shortcut.WorkingDirectory = $destination
$shortcut.IconLocation = "$executable,0"
$shortcut.Save()
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Snapview'
New-Item -Path $uninstallKey -Force | Out-Null
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$uninstallScript = Join-Path $destination 'uninstall.ps1'
$values = @{
    DisplayName = 'Snapview'
    Publisher = 'Snapview'
    DisplayVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable).ProductVersion.Split('+')[0]
    InstallDate = Get-Date -Format 'yyyyMMdd'
    DisplayIcon = "`"$executable`",0"
    InstallLocation = $destination
    UninstallString = "`"$powershell`" -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$uninstallScript`" -FromSettings"
}
foreach ($name in $values.Keys) { New-ItemProperty -LiteralPath $uninstallKey -Name $name -Value $values[$name] -PropertyType String -Force | Out-Null }
foreach ($name in @('NoModify', 'NoRepair')) { New-ItemProperty -LiteralPath $uninstallKey -Name $name -Value 1 -PropertyType DWord -Force | Out-Null }
$installedBytes = (Get-ChildItem -LiteralPath $destination -File -Recurse | Measure-Object -Property Length -Sum).Sum
New-ItemProperty -LiteralPath $uninstallKey -Name EstimatedSize -Value ([int][Math]::Ceiling($installedBytes / 1KB)) -PropertyType DWord -Force | Out-Null
# Register last: its Shell notification must see the completed install metadata.
$registration = Start-Process -FilePath $executable -ArgumentList '--register' -WindowStyle Hidden -PassThru -Wait
if ($registration.ExitCode -ne 0) { throw 'Snapview was copied, but image-type registration failed. Run the installer again.' }
Write-Output "Installed to $destination. Find Snapview in Start and Installed apps. In Snapview choose More > Set as default viewer, then choose your image types in Windows Settings."
