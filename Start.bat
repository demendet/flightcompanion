@echo off
rem Builds the game if anything changed, then starts it. Run it again after a git pull.
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
    echo Fant ikke .NET. Kjor dette i PowerShell, og start Start.bat igjen etterpa:
    echo     winget install Microsoft.DotNet.SDK.9
    pause
    exit /b 1
)
tasklist /fi "imagename eq FlightCompanion.exe" | find /i "FlightCompanion.exe" >nul
if not errorlevel 1 (
    echo Spillet er allerede apent. Lukk det forst.
    pause
    exit /b 1
)
echo Bygger Flight Companion...
dotnet build FlightCompanion.csproj -c Release -o bin\run --nologo -v q
if errorlevel 1 (
    echo Bygget feilet. Se meldingen over.
    pause
    exit /b 1
)
start "" "bin\run\FlightCompanion.exe"
