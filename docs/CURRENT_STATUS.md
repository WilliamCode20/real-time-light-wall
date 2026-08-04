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

### Serialization layer

Fixed 9-byte packets: two sync bytes, command, five payload bytes, checksum.
Commands defined for frame update, blackout and heartbeat. Packing, unpacking
and validation are all implemented and tested.

A packet preview in the window shows the payload, the full packet and the lit
bulb count for the current frame.

### Tests

80 tests covering the wall model, the exact byte layout of the protocol,
round-trip packing, effect repeatability, and engine behaviour.

## Not Yet Implemented

### Serial communication

Not started. `LightWall.IO` is an empty project.

- COM port enumeration
- connection service
- packet transmission
- connection state reporting

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
