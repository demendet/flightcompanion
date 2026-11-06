# Flight Companion

Et karrierespill for Microsoft Flight Simulator 2024. Du driver et lite flyselskap i Finnmark med en Cessna 172, folk ringer deg, og du snakker med dem med din egen stemme.

## Slik installerer du

Du trenger Windows 10 eller 11 og en mikrofon. Åpne PowerShell og kjør disse linjene, én om gangen:

```powershell
winget install Git.Git
```

```powershell
winget install Microsoft.DotNet.SDK.9
```

Lukk PowerShell og åpne det på nytt, så Windows finner de nye programmene:

```powershell
git clone https://github.com/demendet/flightcompanion.git
```

```powershell
.\flightcompanion\Start.bat
```

Etterpå er det bare å dobbeltklikke `Start.bat` i mappa `flightcompanion` for å starte spillet.

Første start bygger programmet (et minutt eller to) og laster ned talegjenkjenningen, omtrent 1,1 GB. Du ser framdriften nede til venstre i vinduet.

## Slik oppdaterer du

Lukk spillet og kjør dette inne i mappa `flightcompanion`, og start så `Start.bat` igjen:

```powershell
git pull
```

Karrieren og nøklene dine ligger utenfor mappa, så de blir ikke borte.

## Nøklene

Gå til Innstillinger og lim inn to nøkler, skriv inn navnet ditt og trykk Lagre.

- **Claude:** lag konto på [platform.claude.com](https://platform.claude.com), legg inn kreditter under Settings, Billing (5 dollar holder lenge), og lag en API-nøkkel.
- **ElevenLabs:** lag konto på [elevenlabs.io](https://elevenlabs.io) og lag en API-nøkkel. Gratiskontoen holder for å prøve, men da blir stemmene utenlandske. Med Starter-planen trykker du "Hent norske stemmer" i Innstillinger.

Nøklene lagres kryptert på Windows-brukeren din. De sendes bare til Claude og ElevenLabs.

## Slik spiller du

1. Trykk "Test mikrofonen" og si noe. Ordene dine skal dukke opp på skjermen. Programmet bruker standard mikrofon i Windows.
2. Start simulatoren, sett deg i en Cessna 172, og vent på at telefonen ringer. Eller trykk "Se etter oppdrag".
3. Bli enige om folk, bagasje, tid og pris. Fly dit, parker, og snakk med dem.

## Godt å vite

- Bruk hodetelefoner, ellers hører figurene seg selv. Bruker du høyttalere, slå det på i Innstillinger.
- Scroll Lock demper mikrofonen, så du kan snakke på Discord uten at passasjerene svarer.
- Vekt og drivstoff skrives til flyet. Bare C172 har riktige tall så langt.
- Sparingen din ligger i `%APPDATA%\FlightCompanion`, og loggfilene i `%LOCALAPPDATA%\FlightCompanion\Logs`.
