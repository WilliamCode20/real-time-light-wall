# Architecture

## High-Level Structure

The desktop app is organized around a small number of core concepts:

- `WallFrame` = one 5x7 wall state (the source of truth for what is lit)
- `IWallEffect` = anything that can draw the wall at a given moment in time
- `WallEngine` = decides what the wall shows right now
- `EffectCatalog` = the master list of every available effect
- `WallFrameSerializer` = converts a wall frame into transport bytes
- `MainWindow` = builds buttons, forwards slider values, draws the result

## Current Projects

The Visual Studio solution contains four projects.

### `LightWall.App`

The WPF application. Targets `net10.0-windows` and is the only project allowed
to reference WPF.

It handles:

- the visible simulator window
- building the wall grid and the effect buttons
- user interaction
- the redraw loop
- packet preview display

Deliberately thin. It contains no decisions about what the wall should look
like — it asks the engine and draws the answer.

### `LightWall.Core`

All reusable non-UI logic. Targets plain `net10.0` and references nothing.

- wall-state modeling (`Models/`)
- static pattern drawing routines (`Patterns/`)
- prepared frame sequences (`Animations/`)
- the effect system and catalog (`Effects/`)
- the playback engine (`Engine/`)
- packet serialization (`Serialization/`)

### `LightWall.IO`

Reserved for hardware and system I/O. **Currently empty.**

Intended responsibilities:

- serial communication
- audio input services
- device enumeration

### `LightWall.Tests`

xUnit tests covering `LightWall.Core`. Currently 80 tests.

## Core Model: `WallFrame`

`WallFrame` is the truth model for a single wall state.

Responsibilities:

- store the ON/OFF state of all 35 cells
- support cell/row/column operations
- support clearing/filling/randomizing
- support copying from another frame
- support translated copies for offset controls
- report whether two frames match, and how many bulbs are lit

Important principle: the UI does not own the wall truth; `WallFrame` does.

There are two translation methods. `CreateTranslated` returns a new frame and is
the readable one. `CopyTranslatedFrom` writes into an existing frame and is what
the engine uses on every frame, because allocating a new object sixty times a
second gives the garbage collector needless work. A test asserts the two agree.

## The Effect System

Everything the wall can display implements one interface:

```
string DisplayName { get; }
string Description { get; }
void Render(EffectContext context, WallFrame target);
```

This replaced three unrelated shapes that previously existed — static patterns,
frame lists, and procedural generators each had their own signature. A single
shape means the engine, the future serial layer, a future scene-picker UI, and a
future audio system can all work with any effect without special cases.

### The three kinds of effect

**Static patterns** wrap a still arrangement (`StaticPatternEffect`). Clear,
Fill, Randomize, Row 3, Column 4, Checkerboard, Border, Cross, Sparkle. Random
ones stay still by always using step 0's randomness; pressing the button again
starts a new session with a new seed, giving a fresh arrangement.

**Frame sequences** play a prepared list like a flipbook
(`FrameSequenceEffect`). Row Sweep, Border Pulse, Spiral. Each carries its own
frames-per-second so it advances at its designed pace regardless of redraw rate.

**Procedural effects** calculate their picture from arithmetic. Meteor, Sparkle
Storm, EQ Bumper. Each is its own class.

### Time, not frame number

`EffectContext.TimeSeconds` is the primary input. Effects are asked "what does
the wall look like 3.2 seconds in?" rather than "what does step 47 look like?".

This matters for three reasons:

- animation pace and redraw rate become independent
- the simulator and the physical wall can run at different rates and stay in
  agreement, which is necessary because the relays cannot switch as fast as a
  screen refreshes
- beats happen at points in time, so music sync is only possible against time

### Repeatability

The same time value must always produce the same frame. Effects needing
randomness call `EffectContext.CreateRandomForStep(step)`, which derives the
generator from the step number.

Without this, an effect meant to change 9 times a second would produce different
output on every one of the 60 redraws per second, and would look like a blur
instead of sparkles. Tests enforce this property across the whole catalog.

## `WallEngine`

The engine holds the answer to "what should the wall look like right now?".

It is always in one of two modes:

- **Playing** — an effect is active and paints the wall on every update
- **Manual** — no effect; the wall holds whatever the user clicked

`Advance(deltaSeconds)` accumulates effect time (scaled by the speed setting),
asks the active effect to paint, then applies the offset sliders to produce the
final output frame. Enormous time steps are capped, so a pause at a debugger
breakpoint cannot make an animation leap somewhere unrelated.

Speed is applied as time accumulates rather than by changing a timer interval.
That keeps redrawing smooth at any animation speed, and means changing speed
mid-animation adjusts the pace from that point on instead of causing a jump.

## Rendering Flow

1. the render loop measures real elapsed time
2. `engine.Advance(delta)` moves time forward and repaints the engine's frame
3. the window compares that frame against what is already on screen
4. only changed cells are restyled
5. if anything changed, the packet preview is rebuilt

The window drives this from `CompositionTarget.Rendering`, which fires once per
frame WPF draws. A `DispatcherTimer` was tried first and only reached about 37
fps, because Windows timers have roughly 15.6 ms granularity and cannot deliver
a 16.7 ms interval.

Brushes are created once, frozen, and shared. Creating them inside the drawing
loop produced roughly 70 short-lived objects per frame.

## Serialization Flow

### Wall data

- 35 wall cells, 1 bit per cell
- row-major order: bulb `N` = `row * 7 + column`
- packed least-significant-bit first
- 5 payload bytes (5 spare bits, always zero)

### Packet structure

Fixed 9 bytes:

- Byte 0: sync 1 (`0xAA`)
- Byte 1: sync 2 (`0x55`)
- Byte 2: command (`PacketCommand`)
- Bytes 3-7: payload
- Byte 8: checksum (XOR of command and payload)

Two sync bytes rather than one, because `0xAA` is an ordinary bulb pattern that
appears in payloads regularly. Sparkle Storm produces one every couple of
seconds. A receiver must still validate the checksum and resynchronise on
failure.

`DeserializeFrameData` and `TryParsePacket` exist so tests can prove a round trip
survives intact, and to serve as a known-correct reference for the firmware to be
translated from.

## Intended Next Architecture Step

A real transport path in `LightWall.IO`:

`WallEngine` -> serializer -> serial service -> Arduino receiver

Two design points already settled for that layer:

- **Latest-frame-wins, never a queue.** The wall should always show the newest
  state. A backlog means it lags reality and drifts further behind over time.
- **Rate-limited independently of the simulator.** The screen runs at 60 fps;
  the wall should be driven around 30, sampling from the same engine.
