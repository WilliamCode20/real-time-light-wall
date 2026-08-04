# Architecture

## High-Level Structure

The desktop app is currently organized around a small number of core concepts:

- `WallFrame` = one 5x7 wall state
- pattern classes = static arrangements of a wall frame
- animation classes = either frame-list sequences or procedural frame generators
- `MainWindow.xaml` = UI layout
- `MainWindow.xaml.cs` = current coordinator for UI, timer, rendering, and controls
- serializer = converts a wall frame into compact transport data

## Current Projects

The Visual Studio solution contains multiple projects:

### `LightWall.App`

This is the WPF application project.

It currently handles:

- the visible simulator window
- building the wall grid UI
- user interaction
- timer-driven animation playback
- parameter controls
- packet preview display

### `LightWall.Core`

This contains reusable non-UI logic.

It currently includes:

- wall-state modeling
- pattern generation
- animation generation
- serialization logic

### `LightWall.IO`

This project exists for future hardware/system I/O work but has not yet been meaningfully implemented.

Intended future responsibilities:

- serial communication
- audio input services
- device enumeration

## Core Model: `WallFrame`

`WallFrame` is the current truth model for wall state.

Responsibilities:

- store the ON/OFF state of all 35 cells
- support cell/row/column operations
- support clearing/filling/randomizing
- support copying from another frame
- support translated copies for center-offset controls

Important principle:
the UI does not own the wall truth; `WallFrame` does.

## Patterns vs Animations

There is an intentional distinction between static patterns and animations.

### Patterns

Patterns generate one static wall arrangement.

Examples:

- checkerboard
- border
- cross
- sparkle

### Frame-list animations

These generate an ordered list of `WallFrame` objects and are played back by the timer.

Examples:

- row sweep
- border pulse
- spiral

### Procedural animations

These generate a new frame from rules based on a step number and current parameters.

Examples:

- meteor
- sparkle storm
- EQ bumper

## UI Coordination

`MainWindow.xaml.cs` currently acts as the coordinator for the prototype.

Responsibilities include:

- building the 5x7 button grid
- storing the active wall frame
- rendering the wall to the simulator UI
- handling button clicks
- handling animation timer ticks
- reading control slider values
- applying translated frames
- updating the packet preview

This is acceptable for the prototype stage, but later some of this should likely move into more focused services or view models.

## Rendering Flow

Current visual flow is:

1. create or modify a `WallFrame`
2. apply translation offsets if needed
3. copy the resulting frame into the active wall state
4. render the active wall state into the simulator UI
5. serialize the active wall state for packet preview

## Serialization Flow

Current packet design:

### Wall data

- 35 wall cells
- 1 bit per cell
- row-major order
- packed into 5 bytes

### Packet structure

- Byte 0 = start byte (`0xAA`)
- Byte 1 = command byte (`0x01` for frame update)
- Byte 2-6 = packed wall payload
- Byte 7 = checksum

## Intended Next Architecture Step

The next likely architecture layer is a real transport/output path:

- `WallFrame`
- serializer
- serial service
- Arduino receiver

That will turn the current simulator-first app into a true controller.
