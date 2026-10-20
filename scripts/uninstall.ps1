param([switch]$FromSettings)
$ErrorActionPreference = 'Stop'
trap {
    if ($FromSettings) {
        Add-Type -AssemblyName PresentationFramework
        [System.Windows.MessageBox]::Show($_.Exception.Message, 'Snapview uninstall') | Out-Null
        exit 1
    }
    throw $_
}
$destination = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\Snapview'))
$expected = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\Snapview'))
if ($destination -ne $expected -or (Split-Path $destination -Leaf) -ne 'Snapview') { throw 'Unexpected installation path' }
if (Get-Process Snapview -ErrorAction SilentlyContinue) { throw 'Close Snapview before uninstalling.' }
if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Snapview.lnk'
if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath -Force }
foreach ($key in @('HKCU:\Software\Classes\Applications\Snapview.exe', 'HKCU:\Software\Classes\Snapview.Image', 'HKCU:\Software\Snapview\Capabilities', 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Snapview')) {
    if (Test-Path -LiteralPath $key) { Remove-Item -LiteralPath $key -Recurse -Force }
}
Remove-ItemProperty -LiteralPath 'HKCU:\Software\RegisteredApplications' -Name Snapview -ErrorAction SilentlyContinue
foreach ($extension in @('.jpg','.jpeg','.png','.webp','.bmp','.gif','.tif','.tiff','.ico','.heic','.heif','.avif')) {
    Remove-ItemProperty -LiteralPath "HKCU:\Software\Classes\$extension\OpenWithProgids" -Name 'Snapview.Image' -ErrorAction SilentlyContinue
}
Write-Output 'Uninstalled. Original recovery copies are preserved in %LOCALAPPDATA%\Snapview\Originals. Choose another viewer in Windows Default Apps if needed.'
