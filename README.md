# Real-Time Light Wall

Windows desktop controller and Arduino firmware for a music-reactive 5x7 light wall.

## Project goals

- Capture system audio on Windows
- Analyze music in real time
- Generate reactive patterns for a 5x7 bulb wall
- Send wall frame data to an Arduino Mega over serial
- Drive 35 individually addressable bulbs through relay outputs

## Repository structure

- `CLAUDE.md` — build commands, protocol spec, and architectural rules
- `docs/` — planning notes, hardware notes, and the original Arduino sketch
- `desktop-app/` — Visual Studio solution and C# desktop application
- `arduino-firmware/` — Arduino Mega firmware (not yet written)

## Current status

The desktop simulator works. It has a time-driven effect engine, 15 effects, live
animation controls, and a tested 9-byte serial packet format. 80 tests pass.

Serial transport and the Arduino firmware are the next layer. Audio comes after
that.

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
