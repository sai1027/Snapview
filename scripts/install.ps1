$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'Snapview'
$destination = Join-Path $env:LOCALAPPDATA 'Programs\Snapview'
if (!(Test-Path -LiteralPath (Join-Path $source 'Snapview.exe'))) { throw 'Run this script from the published artifacts folder beside the Snapview folder.' }
New-Item -ItemType Directory -Force $destination | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $destination -Recurse -Force
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Snapview.lnk'
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $destination 'Snapview.exe'
$shortcut.WorkingDirectory = $destination
$shortcut.Save()
Write-Output "Installed to $destination. Open Snapview and choose More > Set as default viewer to register image types."
