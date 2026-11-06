# Flight Companion: design

A bush flying career for MSFS 2024, set on the Finnmarksvidda. People phone you, you talk to them with your real voice,
agree on a flight, go and get them, and live with what happens. It is meant to be played for hours and to keep going
for months on real calendar time.

## What makes it last

- People are permanent. They keep their voice and remember how you flew them. Regulars and referrals become more of
  your work as your name grows.
- Numbers are real. Weight, CG, fuel and strip length are computed by code, never by the AI. Overloading is your call,
  and MSFS flies the result.
- Honesty varies. What people claim on the phone can differ from what the scale says, depending on who they are.
- Time is real. A pickup on Saturday at 14:00 means this actual Saturday. Forget it and they call, then give up.
- Weather is real: METARs and forecasts for the real place and date, plus the real sun, so polar night matters.
- Money and logistics: fares, tips, mechanic bills, weekly loan payments, fuel cached in sheds on the vidda.
- Story arcs run between normal jobs (the lodge rebuild, the salmon society, the reindeer vet, the inspector).
- Work varies by season, and what you just flew becomes less likely.

## How the code is split

- `Game/`: the rules, with no sim, audio or UI references. Everything testable lives here. See `Tests/`.
- `Services/`: the outside world. `SimLink` (SimConnect), `Listener` (mic, voice detection, Whisper), `AudioOut`,
  `Voices` (ElevenLabs), `Brain` (Claude), `Conversation` (one live talk), `WeatherFeed`, and `GameHost`, which runs the
  game on one loop so no two threads change the career at once.
- `ViewModels/`, `Views/`: the window. A picture of the game is built on the game loop, then handed to the UI.

## The voice path

Pilot stops talking, 450 ms of silence ends the turn (Silero voice detection), Whisper transcribes on the GPU, Claude
streams the reply, each finished sentence goes to ElevenLabs Flash, and PCM plays as it arrives. A short filler clip
plays if the reply is slow. Talking over someone cancels the AI, the audio generation and the playback together, and
the history keeps only what was actually heard.

Claude is used twice: a speaker that plays the person, and a separate note taker that returns structured JSON for the
plan card after each exchange. Speech never waits for notes, and only the note taker changes the card.

## Lessons kept from DualLink

- Fuel is written as tank level, never quantity. On the C172 a written quantity reads back about 1.5 gal short.
- Payload stations are found by name, never assumed by index.
- The pilot station is left alone, because the sim manages it when your character walks outside.
- `IS AVATAR` tells when you are on foot.

## Not proven yet

See the end of the commit log and the session notes. Anything touching the sim needs a live flight: station names on
the real 172, whether figures appear in seats from weight alone, `IS AVATAR` position, spawned people, and the takeoff
and landing distances for strips whose scenery has placeholder runways.
