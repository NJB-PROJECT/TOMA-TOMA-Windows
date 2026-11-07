param([string]$WixPath, [string]$OutputDirectory, [string]$PayloadDirectory)
$ErrorActionPreference = 'Stop'
$tomaRoot = $PSScriptRoot
if (!$WixPath) {
    $tomaLocalWix = Join-Path $tomaRoot '.tools/wix/wix.exe'
    $WixPath = if (Test-Path -LiteralPath $tomaLocalWix) { $tomaLocalWix } else { (Get-Command wix -ErrorAction Stop).Source }
}
if (Test-Path -LiteralPath (Join-Path $tomaRoot '.tools/dotnet/dotnet.exe')) { $env:DOTNET_ROOT = Join-Path $tomaRoot '.tools/dotnet' }
$tomaPayload = if ($PayloadDirectory) { [IO.Path]::GetFullPath($PayloadDirectory) } else { Join-Path $tomaRoot 'dist' }
$tomaExe = Join-Path $tomaPayload 'TOMA TOMA.exe'
if (!(Test-Path -LiteralPath $tomaExe)) { throw 'Jalankan ./build.ps1 -Publish terlebih dahulu.' }
$tomaVersion = ([version](Get-Item -LiteralPath $tomaExe).VersionInfo.FileVersion).ToString(3)
$tomaOutput = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $tomaRoot 'installer-out' }
New-Item -ItemType Directory -Path $tomaOutput -Force | Out-Null
$tomaSdkRoot = if ($env:DOTNET_ROOT) { $env:DOTNET_ROOT } else { Split-Path (Get-Command dotnet -ErrorAction Stop).Source -Parent }
if (!(Test-Path -LiteralPath (Join-Path $tomaPayload 'DOTNET-LICENSE.txt'))) { Copy-Item -LiteralPath (Join-Path $tomaSdkRoot 'LICENSE.txt') -Destination (Join-Path $tomaPayload 'DOTNET-LICENSE.txt') }
if (!(Test-Path -LiteralPath (Join-Path $tomaPayload 'DOTNET-THIRD-PARTY-NOTICES.txt'))) { Copy-Item -LiteralPath (Join-Path $tomaSdkRoot 'ThirdPartyNotices.txt') -Destination (Join-Path $tomaPayload 'DOTNET-THIRD-PARTY-NOTICES.txt') }
& $WixPath build (Join-Path $tomaRoot 'installer/Package.wxs') -arch x64 -ext WixToolset.UI.wixext -d "AppVersion=$tomaVersion" -d "ProjectRoot=$tomaRoot" -d "Payload=$tomaPayload" -o (Join-Path $tomaOutput 'TOMA-TOMA-Windows-Setup-x64.msi')
if ($LASTEXITCODE -ne 0) { throw 'Build MSI gagal.' }
Get-FileHash -LiteralPath (Join-Path $tomaOutput 'TOMA-TOMA-Windows-Setup-x64.msi') -Algorithm SHA256
