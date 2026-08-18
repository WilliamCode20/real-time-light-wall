# Real-Time Light Wall

Windows desktop controller and Arduino firmware for a music-reactive 5x7 light wall.

## Project goals

- Capture system audio on Windows
- Analyze music in real time
- Generate reactive patterns for a 5x7 bulb wall
- Send wall frame data to an Arduino Mega over serial
- Drive 35 individually addressable bulbs through relay outputs

## Repository structure

- `CLAUDE.md` — build commands, protocol spec, architectural rules, and the
  practices worth knowing before changing anything. **Read this first.**
- `docs/CURRENT_STATUS.md` — what exists and why it works the way it does
- `docs/NEXT_STEPS.md` — what is left, in order
- `docs/` — planning notes, hardware notes, and the original Arduino sketch
- `desktop-app/` — Visual Studio solution and C# desktop application
- `arduino-firmware/` — Arduino Mega firmware

## Current status

**The whole chain works**: music → capture → analysis → engine → packets →
firmware → real bulbs. The hardware mapping was verified against the physical
wall on 2026-08-04.

- Time-driven effect engine with **25 effects**, ten of them audio-reactive
- Real-time audio analysis: loudness, seven frequency bands, onset detection,
  tempo estimation and a metronome locked to it
- A virtual wall decoded from the actual packets, running beside the real one as
  a permanent diagnostic
- Tested 9-byte serial protocol and Arduino Mega firmware
- **382 tests pass**

What remains is refinement rather than missing foundation: tuning beat detection
against real music, and DJ-facing scene control. See `docs/NEXT_STEPS.md`.

## Getting started

Build and test:

```bash
dotnet build desktop-app/LightWallController/LightWallController.slnx
dotnet test  desktop-app/LightWallController/LightWall.Tests/LightWall.Tests.csproj
```

Run the simulator:

```bash
dotnet run --project desktop-app/LightWallController/LightWall.App
```

Build a single self-contained .exe to hand to someone who does not have .NET
installed:

```bash
dotnet publish desktop-app/LightWallController/LightWall.App -p:PublishProfile=SelfContained
```

## Naming conventions

Repo names: kebab-case
Example: real-time-light-wall

Solution names: PascalCase
Example: LightWallController

Project names: LightWall.[Purpose]
Example: LightWall.App, LightWall.Core, LightWall.IO

Folders inside projects: PascalCase or clear nouns
Example: Models, Patterns, Audio, Serial
