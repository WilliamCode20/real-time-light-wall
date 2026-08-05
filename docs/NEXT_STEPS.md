# Next Steps

## Completed Since the Last Handoff

The refactor that was flagged as "the next architecture layer" is done:

- all visuals unified behind a single `IWallEffect` interface
- effects driven by elapsed time rather than a frame counter
- playback and wall state moved out of `MainWindow` into `WallEngine`
- `EffectCatalog` added; the window builds its buttons from it
- protocol hardened: two sync bytes, command enum, blackout and heartbeat,
  unpacking and validation
- 80 tests added
- rendering performance fixed (frozen brushes, change detection, proper render
  loop)
- two-column layout so the wall is actually visible
- self-contained publish profile for handing the app to a non-developer

## Also Completed

The output pipeline, and the virtual wall it feeds:

- `VirtualWallReceiver` — software model of the firmware's receiving logic, with
  fault-injection tests
- `WallShowClock` — engine moved onto its own background thread, so the wall's
  timing no longer depends on the window
- `IWallTransport` + `LoopbackTransport` — transport abstraction with a virtual
  wall behind it, able to drop and corrupt bytes on purpose
- `WallOutputService` — rate-limited to 30 packets a second, latest-frame-wins,
  blackout on detach
- 115 tests

Also done: both walls are now shown in the window, with fault-injection sliders.

Also done: `SerialTransport` and `SerialPortLister` in `LightWall.IO`, including
the port-open reset handling.

## Current Priority

### 1. A minimal hardware test path in the UI

Added conservatively, not as a UI overhaul:

- a dropdown listing ports from `SerialPortLister`, plus a refresh
- connect / disconnect, switching `WallOutputService` between the loopback and a
  `SerialTransport`
- show `IsWaitingForBoardReset` while the board restarts, otherwise the first two
  seconds look identical to a broken connection
- show `LastError` when a connection fails

Note that the virtual wall should keep running even when serial is attached — it
is just as useful as a reference when the physical wall is doing something
unexpected.

### 2. Arduino firmware

Translate `VirtualWallReceiver` into C++. It was written specifically for this:
byte at a time, tiny fixed buffer, no allocation — the same shape an Arduino
needs. Its tests already prove the logic, so this is a translation rather than a
fresh design.

Two details the tests pinned down and the firmware must reproduce:

- On seeing a second `0xAA` while waiting for `0x55`, **stay put**. Restarting
  the hunt eats the real sync byte of an `AA AA 55 ...` sequence and silently
  loses a frame.
- A lone `0xAA` is not proof a packet is starting. It is an ordinary bulb pattern
  that appears in payloads regularly, which is why the checksum still matters.

**Include the watchdog.** If no valid packet arrives for a set period, the
firmware blanks the wall by itself. For something switching mains, "the app
crashed and the wall froze mid-frame" should be a designed behaviour rather than
an accident.

### 5. Bulb identification mode

Walk bulbs 0 to 34 one at a time with an on-screen readout of which index is lit.
Build this before going out to the wall — it turns the mapping check into a few
minutes of confirming rather than an afternoon of guessing in the heat.

This is the one thing no amount of virtual work can settle: whether bulb 0 really
is the top-left one, and whether the wall is mirrored or rotated.

### 6. Validate end-to-end

- static frames
- mapping correctness (is bulb 0 really top-left?)
- simple animations
- update stability over a sustained period
- confirm the practical maximum frame rate on real hardware

## After Serial Transport Works

### 7. Audio capture — DONE

WASAPI loopback capture, RMS/peak measurement, decibel mapping and attack/release
smoothing, with a level meter in the interface. Nothing drives the wall from it
yet, which is deliberate.

### 8. Wire audio into the engine — DONE

`AudioFeatures` reaches effects through `EffectContext`, and EQ Bumper follows
the measured level. Verified against real audio playing.

### 9. Frequency bands — DONE

Seven bands, one per column, bass on the left. Each with its own automatic gain,
which is what makes the treble columns usable. The FFT is written out in Core so
the whole chain is testable without audio hardware.

### 10. Onset and beat detection

Harder, and worth deferring until bands work. Onset detection (spotting a sudden
rise in energy) gets most of the way to something that feels beat-driven. Full
BPM estimation and phase tracking is a much larger problem and may not be needed.

### 11. Scene and mapping controls

Which effects respond to which bands, how strongly, and what a DJ can adjust
live. This is where `EffectParameters` will finally need to become a per-effect
system rather than one shared object.

## Known Smaller Items

Not urgent, worth knowing:

- `EffectParameters` is one shared object holding effect-specific settings. Fine
  at one setting; wants to become a per-effect parameter system once several
  effects have their own controls.
- The Center X/Y offsets clip rather than wrap. A wrap mode might be worth adding
  as an option.
- `WallEngine` itself is still single-threaded and unaware of threads. That is
  deliberate — `WallShowClock` owns it and is the only thing allowed to touch it.
  Do not add locking inside the engine; go through the clock.
- The output statistics never reset, so they mix clean and faulty periods
  together and cannot be read as a delivery rate for either. A "reset counters"
  button beside the fault sliders would make the numbers mean something.
- There is no interface control for detaching output. It is settable in code.

## Near-Term Guardrails

Avoid doing several of these at once:

- serial
- audio
- UI overhaul
- major refactor

Preferred pattern:

- one focused layer at a time
- keep the simulator working
- keep commits small
- run `dotnet test` before committing

## Notes for Future Agent Sessions

Read `CLAUDE.md` at the repository root first — it carries the build commands,
the protocol specification and the architectural rules.

Preserve these truths:

- the simulator remains important
- `WallFrame` remains the source of truth for a wall state
- `WallEngine` remains the authority on what is currently displayed
- effects stay time-driven and repeatable
- the serialization format stays consistent unless intentionally revised, and
  `PacketCommand` values are permanent once firmware ships
- the desktop app is the main intelligence layer; the Arduino is an output target
- comments are part of the deliverable in this repository
