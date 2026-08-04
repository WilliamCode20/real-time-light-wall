# CLAUDE.md

Guidance for Claude Code when working in this repository.

## What this project is

A Windows desktop app that will drive a physical 5x7 light wall (35 individually
switched bulbs) in real time, eventually reacting to music playing on the
computer. The app is the brain; an Arduino Mega 2560 is the output device.

The end goal is something a DJ or venue operator could be handed and use — not a
developer tool.

## Working style for this repo

The repository owner is new to C#, .NET and WPF. That shapes how to work here:

- **Comments are part of the deliverable.** This codebase deliberately carries
  much heavier comments than a typical professional project. Match the existing
  density. Explain *why* a thing is done, not just what it does, and write for
  someone who has not met the language feature before.
- **One layer at a time.** Do not combine a refactor, a new subsystem and a UI
  overhaul in one change. Build, run, and commit between layers.
- **Prefer clarity over cleverness.** If a shorter expression needs a paragraph
  of explanation, write the longer obvious version instead.
- Explain reasoning in replies rather than only stating conclusions.

## Commands

Build everything:

```bash
dotnet build "desktop-app/LightWallController/LightWallController.slnx"
```

Run the tests:

```bash
dotnet test "desktop-app/LightWallController/LightWall.Tests/LightWall.Tests.csproj"
```

Run the simulator:

```bash
dotnet run --project "desktop-app/LightWallController/LightWall.App"
```

Build a single self-contained .exe to hand to a non-developer:

```bash
dotnet publish "desktop-app/LightWallController/LightWall.App" -p:PublishProfile=SelfContained
```

## Project layout

| Project | Target | Purpose |
|---|---|---|
| `LightWall.Core` | `net10.0` | Wall model, effects, engine, clock, transport, packet format, virtual wall. No UI references. |
| `LightWall.App` | `net10.0-windows` | WPF simulator window. The only project that knows about WPF. |
| `LightWall.IO` | `net10.0` | Real hardware and system I/O — serial, audio. **Currently empty.** |
| `LightWall.Tests` | `net10.0` | xUnit tests for Core. 115 of them. |

Shared build settings live in
`desktop-app/LightWallController/Directory.Build.props`.

## Architectural rules

These are load-bearing. Breaking them causes real problems later.

1. **`LightWall.Core` never references WPF.** It targets plain `net10.0`
   specifically so this is impossible to do by accident. Logic that would still
   make sense with no screen attached belongs in Core.

2. **`WallEngine` is the single authority on what the wall shows.** The window
   draws what the engine says; it does not decide anything itself. Do not
   reintroduce wall state into the window.

   `WallShowClock` owns the engine and runs it on a background thread. Nothing
   else touches the engine directly — go through `clock.Modify(...)` to change
   it and `clock.CopyCurrentFrameTo(...)` to read it. Do not add locking inside
   `WallEngine` itself; it is deliberately a simple single-threaded class.

3. **Effects are driven by time, not frame count.** `EffectContext.TimeSeconds`
   is the input. This decouples animation pace from redraw rate, lets the screen
   and the hardware run at different rates, and is a prerequisite for beat sync.

4. **Effects must be repeatable.** The same time value must produce the same
   frame. Random effects use `EffectContext.CreateRandomForStep(step)` rather
   than a shared generator — otherwise they flicker, because the screen redraws
   far more often than they change. There are tests enforcing this across the
   whole catalog.

5. **Effects must clear the frame they are given.** The engine reuses one frame
   object rather than allocating one per frame, so it arrives holding the
   previous contents. There is a test enforcing this too.

6. **Adding an effect means adding one entry to `EffectCatalog`.** The window
   builds its buttons from the catalog. Do not hard-code effect buttons in XAML.

7. **Shared state crosses threads only as a copy, taken under a lock.** Two
   background threads exist — the show clock and the output service. Neither
   reaches into anything else's state. This is the pattern audio will use when
   analysis arrives on a callback thread.

8. **Output is rate-limited and never queued.** The wall is fed at 30 packets a
   second regardless of how fast the engine ticks, and frames generated in
   between are skipped rather than stored. Queueing would make the wall lag
   further behind reality over time; dropping keeps it at worst one frame late.

## The output pipeline

```
WallShowClock  --> MainWindow          (draws, ~60 Hz)
               --> WallOutputService   (samples 30 Hz, builds packets)
                        --> LoopbackTransport  (virtual wall, works today)
                        --> SerialTransport    (not yet written)
```

`VirtualWallReceiver` is a software model of the firmware's receiving half. It is
both the thing that makes hardware-free development possible and the reference
the C++ firmware should be translated from — it is written byte-at-a-time with a
fixed buffer and no allocation, deliberately in the shape an Arduino needs.

`LoopbackTransport` can drop and corrupt bytes on purpose via
`ByteDropProbability` and `ByteCorruptionProbability`. Use it when changing
anything in the receive path.

## Serial protocol

Defined in `LightWall.Core/Serialization/WallFrameSerializer.cs`, which carries
the full specification in its comments. Summary:

- Fixed **9-byte** packets: `AA 55 <command> <5 payload bytes> <checksum>`
- Checksum is XOR of the command byte and the five payload bytes
- 35 bulbs, one bit each, **row-major**, packed **least-significant-bit first**
- Bulb number `N` = `row * 7 + column`, which matches `allLights[N]` in the
  original hand-written sketch exactly

Two things to be careful about:

- **Two sync bytes, not one.** `0xAA` alone is a perfectly ordinary bulb pattern
  and appears in payloads regularly. A receiver must never treat a lone `0xAA` as
  proof a packet is starting, and must still validate the checksum.
- **Command numbers are permanent.** Once firmware is deployed, existing values
  in `PacketCommand` must never be reassigned.

## Hardware facts

Confirmed from the original working sketch in `docs/OLD_ARDUINO_CODE/`:

- Pin map, row-major:
  ```
  Row 0:  2,  3,  4,  5,  6,  7,  8
  Row 1:  9, 10, 11, 12, 13, 22, 23
  Row 2: 24, 25, 26, 27, 28, 29, 30
  Row 3: 31, 32, 33, 34, 35, 36, 37
  Row 4: 38, 39, 40, 41, 42, 43, 44
  ```
- **Active HIGH** (`#define PIX_ON HIGH`) — the SSR control is non-inverting
- Serial was never used by the old sketch, so the port is fully free
- No PWM anywhere; the wall is strictly ON/OFF
- `analogRead(0)` was used as a noise source for `randomSeed`

**Switching speed.** The old show reliably used dwell times down to ~15 ms, with
most effects in the 30–80 ms range, and it ran correctly. So roughly 30 updates
per second is a comfortable ceiling for the hardware, and 60 is about at the
proven edge. The simulator redraws at 60 fps, but the serial layer should send at
its own slower, rate-limited pace rather than forwarding every drawn frame.

Bulbs are LEDs. Arduino pins reach the SSRs via a breadboard with no known
intermediate driver stage. The original installation runs all 35 outputs without
trouble.

## Things deliberately not built yet

Do not add these speculatively:

- Serial transport (goes in `LightWall.IO`, implementing `IWallTransport`). When
  it is written it must handle the port-open reset: opening a serial connection
  to a Mega toggles DTR and reboots the board, so roughly the first 1.5–2
  seconds of anything sent is swallowed by the bootloader.
- Arduino firmware (only a README exists so far)
- Audio capture, analysis, beat detection
- Per-effect parameter systems — `EffectParameters` is a single shared object on
  purpose while there is only one effect-specific setting
- Brightness or dimming — the relays are ON/OFF only. If it ever became
  desirable, `SetCell(row, column, bool)` can stay as an overload so existing
  effects keep working.
