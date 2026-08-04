# Current Status

## Working Right Now

The desktop WPF simulator runs, and the solution builds clean with 80 passing
tests.

### Core wall model

- `WallFrame` stores the 5x7 ON/OFF wall state
- cell, row, column, all-on/all-off operations
- copying, translated copies, content comparison, lit-cell count

### Effect system

All visuals implement a single `IWallEffect` interface and are driven by elapsed
time rather than by a frame counter. Effects are repeatable: the same moment
always produces the same picture.

**Static patterns (9):** Clear, Fill, Randomize, Row 3, Column 4, Checkerboard,
Border, Cross, Sparkle

**Frame-sequence animations (3):** Row Sweep, Border Pulse, Spiral

**Procedural animations (3):** Meteor, Sparkle Storm, EQ Bumper

All 15 are registered in `EffectCatalog`, and the window builds its buttons from
that list. Adding an effect is a one-entry change.

### Playback engine

`WallEngine` owns wall state and playback. Two modes: playing an effect, or
manual mode where the user's clicked pattern is left alone. Speed is applied by
scaling how fast effect time accumulates, so it can be changed mid-animation
without a jump. Oversized time steps are capped so debugger pauses do not make
animations leap.

### Animation controls

- Speed (10%–300%)
- Center X (-3 to +3)
- Center Y (-2 to +2)
- Meteor Tail Length (1–5)

All apply live, mid-animation. The Center offsets now affect static patterns as
well as animations, which was previously inconsistent.

### Simulator UI

Two-column layout: controls on the left in a scrollable panel, the wall on the
right at a fixed 7:5 aspect. The previous single-column layout pushed the wall
off the bottom of the screen once the controls grew.

- effect buttons generated from the catalog, with descriptions as tooltips
- the active effect's button is highlighted
- status line shows what is playing and what it does
- live frame-rate readout
- redraws via `CompositionTarget.Rendering` at ~60 fps
- only changed cells are restyled; brushes are created once and frozen

### Output pipeline

The engine no longer runs on the window's redraw loop. `WallShowClock` ticks it
on a background thread at around 120 Hz; the window draws from it at the
monitor's rate; `WallOutputService` samples it 30 times a second and sends
packets. Three independent rates, none constraining the others.

Output is rate-limited to 30 packets a second, based on measured behaviour of the
real installation. Frames generated between sends are skipped rather than queued,
so the wall is at worst one frame behind reality rather than accumulating lag.
Every frame is sent even when unchanged, which makes the stream self-healing and
keeps the firmware watchdog fed. Detaching sends a blackout first.

### Virtual wall

`VirtualWallReceiver` is a software model of the Arduino's receiving logic: the
byte-stream state machine, sync hunting, checksum validation, resynchronisation,
and the watchdog. `LoopbackTransport` feeds packets into it, and can be told to
drop or corrupt bytes on purpose to prove recovery works.

The app attaches this at startup, so the entire pipeline runs for real from the
moment it opens. The output readout in the window shows packets sent, packets
accepted, checksum failures and discarded bytes.

Measured on a normal run: 240 packets sent, 240 accepted, zero failures.

### Serialization layer

Fixed 9-byte packets: two sync bytes, command, five payload bytes, checksum.
Commands defined for frame update, blackout and heartbeat. Packing, unpacking
and validation are all implemented and tested.

A packet preview in the window shows the payload, the full packet and the lit
bulb count for the current frame.

### Tests

115 tests covering the wall model, the exact byte layout of the protocol,
round-trip packing, effect repeatability, engine behaviour, the receiver's
stream handling under deliberately injected faults, and the output pipeline
end to end.

### Two walls side by side

The window shows both walls stacked in the right-hand column:

- **Engine** — what the effect decided the wall should look like
- **Virtual wall** — what a real wall would be showing, decoded from the packets
  that actually arrived

While everything is working they are identical, which is the proof that packing,
transmission, framing, checksum validation and unpacking all agree.

Two sliders damage the byte stream on purpose. Turn up "Drop bytes" and the lower
wall starts lagging behind the upper one as damaged packets are discarded, then
snaps back into step when a good one gets through. Observed at 4% byte drop: the
walls visibly diverge by a frame, checksum failures accumulate, bytes get
discarded during resynchronisation, and the wall keeps recovering rather than
staying broken.

That is the genuine recovery path running for real, not a mock-up of it.

## Not Yet Implemented

### Serial communication

Not started. `LightWall.IO` is still an empty project.

The abstraction it plugs into is done: `SerialTransport` needs only to implement
`IWallTransport`, and everything upstream already works. It will need the
`System.IO.Ports` package, and it must handle the port-open reset — opening a
serial connection to a Mega toggles DTR and reboots the board, so roughly the
first 1.5 to 2 seconds of anything sent will be swallowed by the bootloader.

### Arduino firmware

Only a README exists. The protocol is specified and has a reference
implementation in C# to translate from, but no firmware has been written and
nothing has been tested against real hardware.

### Audio system

Not started.

- Windows system audio capture
- level and frequency-band analysis
- onset detection, BPM estimation
- music-to-animation mapping

## Current Development State

The project has a real visual engine, a tested protocol, reusable effect logic,
and a clean separation between logic and interface.

The layer that logically comes next is serial transport, followed by firmware,
followed by audio.
