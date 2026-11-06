@echo off
rem Builds the game if anything changed, then starts it. Run it again after a git pull.
cd /d "%~dp0"
set EXE=bin\run\FlightCompanion.exe
set DLL=bin\run\FlightCompanion.dll
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
if exist "%EXE%" (
    start "" "%EXE%"
    exit /b 0
)
if exist "%DLL%" (
    echo FlightCompanion.exe ble borte etter bygget. Antivirus har antagelig fjernet den.
    echo Sjekk Windows-sikkerhet, Beskyttelseslogg. Spillet starter likevel, via dotnet.
    start "Flight Companion" /min dotnet "%DLL%"
    pause
    exit /b 0
)
echo Bygget sa OK, men fant ikke spillet i bin\run. Skriver detaljer til build.log...
dotnet build FlightCompanion.csproj -c Release -o bin\run --nologo -v n > build.log 2>&1
dotnet --list-sdks >> build.log 2>&1
dir bin\run >> build.log 2>&1
echo Send filen build.log i denne mappa til den som ga deg spillet.
pause
exit /b 1
