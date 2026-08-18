# Architecture

> **Where this sits among the docs.** This file describes the shape of the code —
> what the pieces are and how they fit. `CLAUDE.md` is authoritative for the rules
> that must not be broken and the practices worth knowing; `CURRENT_STATUS.md` is
> authoritative for what exists and why each decision was made. Where they
> disagree with this file, they are right and this one has drifted.

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
- the playback engine and show clock (`Engine/`)
- packet serialization (`Serialization/`)
- transports and the output service (`Transport/`)
- the software model of the firmware's receiver (`Simulation/`)
- **all audio analysis** (`Audio/`) — loudness, frequency bands, onset
  detection, tempo estimation, the metronome

That last one is the least obvious and the most load-bearing: analysis lives here
rather than in IO precisely so it can be tested against signals whose answers are
known in advance, with no sound card and nothing playing.

### `LightWall.IO`

Hardware and system I/O. Targets `net10.0-windows` because WASAPI is
Windows-specific.

- `Serial/SerialTransport` — the real wall, and the DTR-reset settle window
- `Serial/SerialPortLister` — port enumeration, sorted numerically
- `Audio/SystemAudioCapture` — WASAPI loopback capture

Deliberately thin. It asks Windows for buffers and hands them to Core; the
analysis is not here and must not be moved here.

### `LightWall.Tests`

xUnit tests covering Core and the testable parts of IO. **382 tests.** Windows
targeted only because it references IO.

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

## The Output Pipeline

```
WallShowClock            ticks the engine on its own background thread (~120 Hz)
     |
     +--> MainWindow          draws it (~60 Hz, whatever the monitor does)
     |
     +--> WallOutputService   samples it (30 Hz), builds packets
                |
                +--> LoopbackTransport   virtual wall, always attached
                +--> SerialTransport     the real wall, when a port is connected
```

Both are attached at once through `CompositeTransport`. Connecting a port **adds**
the real wall beside the virtual one rather than replacing it, which is the
project's most useful diagnostic: if the two agree and the hardware does not, the
fault is wiring, firmware or a relay.

The important property is that these are three independent rates. The engine
ticks fast enough to be smooth, the window draws at the monitor's pace, and the
wall is fed at a rate the relays can actually manage. None of them constrains the
others.

### `WallShowClock`

Owns the engine and exclusive rights to touch it. Runs it on a background thread.

Everything else goes through `Modify(Action<WallEngine>)`, which takes a lock
first, or `CopyCurrentFrameTo`, which hands out a copy. Handing out a reference
would let a reader see half of one frame and half of the next, since the tick
thread is rewriting it continuously.

One general `Modify` method rather than a wrapper per operation, because there
would otherwise be a dozen wrappers differing only in one line, and every new
engine feature would need another.

The tick rate is a target, not a promise — Windows timers are only accurate to
around 15 ms. That does not matter: the engine advances by measured elapsed time,
so a lower tick rate means slightly chunkier motion, never slower motion.

### `IWallTransport`

Two implementations, and the app cannot tell them apart. Everything upstream —
rate limiting, packet building, error handling — is written once and works with
either, so time spent testing against the loopback is testing the real code path.

`LoopbackTransport` wraps a `VirtualWallReceiver` and can be told to drop or
corrupt bytes on purpose. That is not a gimmick: a real cable will occasionally
lose a byte, and the question is whether everything downstream recovers. Making a
real cable misbehave on demand is very hard; here it is a property.

### `WallOutputService`

Samples the clock at `OutputRateHz` (default 30) and sends. Two decisions worth
knowing:

**Latest frame wins, there is no queue.** Frames generated between sends are
skipped, not stored. If they were queued, any moment where the wall could not
keep up would leave a backlog, and the wall would start showing the past — with
the lag growing steadily worse. Dropping frames means the wall is at worst one
frame behind, permanently.

**Every frame is sent, even unchanged ones.** It is self-healing (a packet lost
to corruption is replaced a thirtieth of a second later) and it keeps the
firmware watchdog fed without separate heartbeats. The cost is 270 bytes a
second, about 2% of a 115200 baud connection.

Detaching sends a blackout first, so stopping output leaves the wall dark rather
than frozen on an arbitrary frame.

## Rendering Flow

The window no longer advances anything. Its loop is display-only:

1. copy the clock's current frame into the window's own frame
2. compare against what is already on screen
3. restyle only the cells that changed
4. if anything changed, rebuild the packet preview

Driven from `CompositionTarget.Rendering`, which fires once per frame WPF draws.
A `DispatcherTimer` was tried first and only reached about 37 fps, because
Windows timers have roughly 15.6 ms granularity and cannot deliver a 16.7 ms
interval.

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

## Threading Rules

Only two background threads exist, and both follow the same rule: **shared state
is only ever read as a copy, taken under a lock.**

| Thread | Owns | Reads from |
|---|---|---|
| Show clock | `WallEngine` | — |
| Output | the transport | the clock (copies) |
| Interface | the buttons | the clock, the loopback (copies) |

Nothing reaches into anything else's state directly. This is the pattern audio
will use too, when analysis arrives on a WASAPI callback thread and needs to hand
features to the engine.

## Simulating the Firmware

`VirtualWallReceiver` models the Arduino's receiving half: the byte-stream state
machine, sync hunting, checksum validation, resynchronisation, and the watchdog.

It unpacks bits with its own loop rather than calling the serializer, so the
tests compare two independent implementations. Sharing that code would let a
bit-maths bug hide, with both sides agreeing while both were wrong.

Two findings its tests pinned down:

- A stray `0xAA` before a real packet gives `AA AA 55 ...`. A receiver that
  restarts its hunt on the second `0xAA` eats the real sync byte and loses the
  frame. It must stay put and treat each `0xAA` as a possible fresh start.
- A payload can legitimately contain the sync pair. That cannot be prevented,
  only recovered from — the checksum catches the misread and the receiver is back
  in step within a couple of packets.

## Intended Next Architecture Step

A second wall in the interface showing what `VirtualWallReceiver` decoded, next
to what the engine drew. When the two match while sliders are being dragged, the
whole pipeline is proven except the physical layer.

After that: a real `SerialTransport` in `LightWall.IO`, then firmware.
