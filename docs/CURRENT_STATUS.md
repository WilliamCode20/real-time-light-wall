# Current Status

## Working Right Now

The desktop WPF simulator currently works and includes:

### Core wall model

- `WallFrame` stores the 5x7 ON/OFF wall state
- supports cell, row, column, all-on/all-off operations
- supports copying and translated frame generation

### Static patterns

Current static pattern support includes:

- Clear
- Fill
- Randomize
- Row 3
- Column 4
- Checkerboard
- Border
- Cross
- Sparkle

### Pre-set animations

Current frame-list animations include:

- Row Sweep
- Border Pulse
- Spiral In/Out

### Procedural animations

Current procedural animations include:

- Meteor
- Sparkle Storm
- EQ Bumper

### Animation controls

Current controls include:

- Speed
- Center X
- Center Y
- Meteor Tail Length

### UI improvements already implemented

The simulator UI currently includes labeled sections:

- Static Patterns
- Pre-Set Animations
- Procedural Animations
- Animation Controls

### Serialization layer

A serializer now exists that converts a `WallFrame` into:

- a 5-byte packed wall payload
- an 8-byte packet with:
  - start byte
  - command byte
  - payload
  - checksum

A packet preview is displayed in the simulator UI for debugging.

## Not Yet Implemented

These layers are not yet built:

### Serial communication from desktop app

Not yet implemented:

- COM port selection
- serial connection service
- packet transmission to Arduino from the app

### Arduino firmware integration with app protocol

The protocol design exists conceptually, but desktop-to-Arduino live communication has not yet been wired up and tested end-to-end.

### Audio system

Not yet implemented:

- Windows system audio capture
- waveform monitoring
- bass/mid/treble analysis
- onset detection
- BPM estimation
- music-to-animation mapping

## Current Development State

The project is still in the simulator-first phase, but it has moved beyond a toy prototype.

The app now has:

- a real visual engine
- parameterized controls
- reusable pattern/animation logic
- a first transport-oriented serialization layer

This is the stage immediately before serial transport and later audio integration.
