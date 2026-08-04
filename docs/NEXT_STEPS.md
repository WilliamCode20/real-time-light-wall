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

## Current Priority

### 1. Serial transport in `LightWall.IO`

Implement `IWallTransport` over a real port. Everything upstream already works,
so this is the only new code needed.

- enumerate COM ports
- connect and disconnect
- send a packet
- report connection state

Needs the `System.IO.Ports` package.

**Handle the port-open reset.** Opening a serial connection to a Mega toggles the
DTR line, which reboots the board. For roughly the first 1.5 to 2 seconds
afterwards the bootloader is running and will swallow anything sent. Wait before
starting to talk.

`SerialPort.Write` is already off the UI thread — the output service has its own.

### 2. A minimal hardware test path in the UI

Added conservatively, not as a UI overhaul:

- choose transport: virtual wall or a COM port
- connect / disconnect
- send the current frame once

### 4. Arduino firmware

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

### 7. Audio capture only

Do not jump straight to reactive logic. First add Windows system audio capture
(WASAPI loopback, most easily via NAudio) plus basic level meters for debugging.

The threading pattern is already established: analysis will arrive on an audio
callback thread and hand features to the engine the same way the window hands it
slider changes — through the clock, under a lock, never by reaching in.

### 8. Audio feature extraction

Overall level, bass/mid/treble energy, smoothing, onset detection, then beat
confidence and BPM estimation.

### 9. Map audio features to visuals

Only once the layers above work. `EffectContext` is the place audio features
would arrive, so effects can read them the same way they read time today.

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
