# Builds the one FlightCompanion.exe that lives in the repo root. It carries everything it needs and must stay under GitHub's 100 MB limit.
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $env:TEMP 'FlightCompanionExe'
dotnet publish "$root\FlightCompanion.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=none -o $out -m:1 -nodeReuse:false -p:UseSharedCompilation=false --nologo -v q
Copy-Item "$out\FlightCompanion.exe" "$root\FlightCompanion.exe" -Force
