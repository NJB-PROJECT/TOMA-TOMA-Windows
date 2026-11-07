param([switch]$Publish)
$ErrorActionPreference='Stop'
$tomaRoot=$PSScriptRoot
$tomaDotnet=Join-Path $tomaRoot '.tools\dotnet\dotnet.exe'
if(!(Test-Path -LiteralPath $tomaDotnet)){$tomaDotnet=(Get-Command dotnet -ErrorAction Stop).Source}
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
& $tomaDotnet build (Join-Path $tomaRoot 'src\TomaToma.Windows.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Build gagal'}
if($Publish){$tomaStage=Join-Path $tomaRoot '.publish\win-x64'
 & $tomaDotnet publish (Join-Path $tomaRoot 'src\TomaToma.Windows.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $tomaStage
 if($LASTEXITCODE -ne 0){throw 'Publish gagal'}
 $tomaDist=Join-Path $tomaRoot 'dist';New-Item -ItemType Directory -Force -Path $tomaDist | Out-Null
 $tomaTarget=Join-Path $tomaDist 'TOMA TOMA.exe'
 Get-Process -Name 'TOMA TOMA' -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $tomaTarget} | ForEach-Object {Stop-Process -Id $_.Id;[void]$_.WaitForExit(5000)}
 foreach($tomaFile in (Get-ChildItem -LiteralPath $tomaStage -File)){
  for($tomaAttempt=0;$tomaAttempt -lt 4;$tomaAttempt++){try{Copy-Item -LiteralPath $tomaFile.FullName -Destination (Join-Path $tomaDist $tomaFile.Name) -Force;break}catch{if($tomaAttempt -eq 3){throw};Start-Sleep -Milliseconds 500}}
 }
}
