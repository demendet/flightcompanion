# Builds a folder and a zip anyone can run without installing anything. Nothing personal goes in: keys and careers live in the user's own profile.
param([string]$Out = "$PSScriptRoot\..\dist")
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $Out 'FlightCompanion'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force -Confirm:$false }
$p = Start-Process dotnet -PassThru -NoNewWindow -Wait -ArgumentList @('publish', "$root\FlightCompanion.csproj", '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $publish, '-m:1', '-p:DebugType=none', '-nodeReuse:false', '-p:UseSharedCompilation=false', '--nologo', '-v', 'q')
if ($p.ExitCode -ne 0) { throw "publish failed" }
Copy-Item "$root\docs\LES-MEG.txt" $publish
# Refuse to ship anything that looks like a secret.
$bad = Get-ChildItem $publish -Recurse -File | Where-Object { $_.Name -match '^(appsettings.*\.json|keys\.dat|career\.json|settings\.json)$' }
if ($bad) { throw "Personal files in the package: $($bad.Name -join ', ')" }
$zip = Join-Path $Out 'FlightCompanion.zip'
if (Test-Path $zip) { Remove-Item $zip -Force -Confirm:$false }
Compress-Archive -Path $publish -DestinationPath $zip -CompressionLevel Fastest
Write-Output ("{0}: {1:0} MB" -f $zip, ((Get-Item $zip).Length / 1MB))
