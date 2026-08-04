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

## Current Priority

Serial transport. Everything needed for it now exists on the app side.

### 1. Serial service in `LightWall.IO`

Build a service that can:

- enumerate COM ports
- connect and disconnect
- send a packet
- report connection state

Two design points already settled:

- **Latest-frame-wins, never a queue.** One slot holding the newest frame,
  overwritten by each update, drained by the writer. A backlog would mean the
  wall lags reality and drifts further behind over time.
- **Rate-limited independently of the simulator.** The screen runs at 60 fps; the
  wall should be driven at around 30, sampling from the same engine. See
  HARDWARE_NOTES.md for the measured evidence behind that figure.

Keep `SerialPort.Write` off the UI thread so a USB hiccup stalls the writer
rather than freezing the window.

### 2. A minimal hardware test path in the UI

Added conservatively, not as a UI overhaul:

- choose COM port
- connect / disconnect
- send the current frame once
- toggle live-send during playback

### 3. Arduino firmware

Use `WallFrameSerializer` as the reference — `DeserializeFrameData` and
`TryParsePacket` exist specifically so the firmware has known-correct logic to
translate from.

The receive loop should:

- wait for `0xAA` followed immediately by `0x55`
- collect the remaining 7 bytes
- verify the checksum, and resynchronise if it fails
- act on the command
- unpack the payload and drive the mapped pins

Note that a lone `0xAA` is not proof a packet is starting; it is an ordinary bulb
pattern that appears in payloads regularly.

**Include a watchdog.** If no valid packet arrives for a set period, the firmware
should take a defined action on its own. For something switching mains, "the app
crashed and the wall froze mid-frame" should be a designed behaviour rather than
an accident. Blanking is the safe default. That is what the heartbeat command is
for.

### 4. Validate end-to-end

- static frames
- mapping correctness (is bulb 0 really top-left?)
- simple animations
- update stability over a sustained period
- confirm the practical maximum frame rate on real hardware

## After Serial Transport Works

### 5. Audio capture only

Do not jump straight to reactive logic. First add Windows system audio capture
(WASAPI loopback, most easily via NAudio) plus basic level meters for debugging.

### 6. Audio feature extraction

Overall level, bass/mid/treble energy, smoothing, onset detection, then beat
confidence and BPM estimation.

### 7. Map audio features to visuals

Only once the layers above work. `EffectContext` is the place audio features
would arrive, so effects can read them the same way they read time today.

## Known Smaller Items

Not urgent, worth knowing:

- `EffectParameters` is one shared object holding effect-specific settings. Fine
  at one setting; wants to become a per-effect parameter system once several
  effects have their own controls.
- The Center X/Y offsets clip rather than wrap. A wrap mode might be worth adding
  as an option.
- `WallEngine` is not thread-safe. That becomes relevant when the serial layer
  wants frames from a background thread; the simplest fix then is to hand that
  thread a copy of the finished frame rather than let it reach into the engine.

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
