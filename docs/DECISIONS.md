# Decisions

## Confirmed Decisions

### 1. Platform choice

The project is Windows-first.

Reason:

- development machine is Windows
- future system audio capture is easier to approach on Windows
- WPF/.NET is a strong fit for this stage

### 2. Language and UI stack

The project uses:

- C#
- .NET
- WPF

Reason:

- appropriate for desktop tooling
- better fit than Electron for this Windows-first hardware-control use case
- strong enough for future audio + serial work

### 3. Simulator-first development

The simulator is a permanent part of the project, not just a temporary toy.

Reason:

- enables development without always using hardware
- helps debug patterns and animation logic
- helps validate serialization output
- should remain useful even after hardware output is live

### 4. Desktop app is the main brain

The computer app is intended to handle:

- animation logic
- future audio analysis
- future music feature extraction
- wall frame generation

The Arduino is intended to handle:

- packet reception
- packet validation
- output pin updates

### 5. Wall truth lives in `WallFrame`

The project uses `WallFrame` as the source of truth for a single wall state.

Reason:

- keeps UI separate from wall logic
- supports simulation and future hardware output from the same model
- makes serialization cleaner

### 6. Keep patterns and animations reusable

Static patterns and animations are intentionally separated from UI event handlers.

Reason:

- better code organization
- easier reuse
- future audio system will need reusable behaviors to drive

### 7. Frame translation should affect data, not just rendering

Center X and Center Y controls are implemented by translating wall-frame data rather than only shifting visuals during rendering.

Reason:

- keeps the simulator honest
- future hardware output should match what the simulator shows
- avoids UI-only hacks

### 8. Serialization format

Current chosen frame protocol:

- row-major wall mapping
- 35 cells packed into 5 bytes
- full packet = 8 bytes:
  - start byte
  - command byte
  - 5 payload bytes
  - checksum

Reason:

- compact
- easy to debug
- easy for Arduino to unpack
- future-friendly for additional command types

### 9. Build safety / workflow

The preferred workflow is:

- make small changes
- build often
- review behavior
- commit frequently

Reason:

- beginner-safe
- helps avoid AI-generated chaos
- keeps the project understandable

## Things Intentionally Deferred

The following are intentionally not yet implemented:

- live serial transport
- audio capture
- beat detection
- feature extraction
- music-to-animation mapping

These are deferred because the simulator, frame model, and serialization layers needed to exist first.
