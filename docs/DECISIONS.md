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

- row-major wall mapping, bulb `N` = `row * 7 + column`
- bits packed least-significant-bit first
- 35 cells packed into 5 bytes
- full packet = 9 bytes:
  - sync byte 1 (`0xAA`)
  - sync byte 2 (`0x55`)
  - command byte
  - 5 payload bytes
  - checksum (XOR of command and payload)

Reason:

- compact
- easy to debug
- easy for Arduino to unpack
- fixed length keeps the firmware's receive logic simple
- future-friendly for additional command types

Revised from an earlier 8-byte design that used a single `0xAA` start byte. The
problem is that `0xAA` is an ordinary bulb pattern that turns up in payloads
regularly — Sparkle Storm produces one every couple of seconds. A receiver that
had lost its place could latch onto a payload byte and stay misaligned. Two sync
bytes make that far less likely for the cost of one byte. The checksum still
matters, because it reduces the risk rather than eliminating it.

### 9. Effects are a single interface, driven by time

Every visual — static pattern, frame sequence, or procedural animation —
implements `IWallEffect` and is asked what the wall looks like at a moment in
time, rather than at a step number.

Reason:

- three different shapes meant anything working with "whatever is playing" needed
  three special cases; that is manageable at 15 effects and a roadblock at 40
- animation pace becomes independent of redraw rate
- the simulator and the physical wall can run at different rates and still agree,
  which is necessary because relays cannot switch as fast as a screen refreshes
- beats happen at points in time, so music sync is only possible against time

A consequence worth preserving: effects must be repeatable, so the same time
always gives the same frame. Random effects derive their generator from the step
number rather than sharing one. Without that they flicker, because the screen
redraws far more often than they change.

### 10. Wall state lives in `WallEngine`, not in the window

Playback, wall state, speed and offsets moved out of `MainWindow` into a class in
`LightWall.Core`.

Reason:

- three things need to know what the wall should look like — the simulator, the
  serial layer, and the tests — and only one of them is a window
- logic inside a window can only ever be used by that window
- it made the engine testable, which is where most of the 80 tests came from

### 11. Effects are registered in a catalog

`EffectCatalog` is the single list of available effects. The window builds its
buttons from it.

Reason:

- adding an effect is a one-entry change instead of edits in three files
- a DJ picking scenes from a menu needs a list of scenes to pick from
- a future audio system choosing effects needs the same list

### 12. The app will ship as a self-contained single .exe

Publishing uses a profile that bundles the .NET runtime into one file.

Reason:

- the app is meant to be sent to a DJ or venue operator who just runs it
- the alternative is talking a non-technical user through installing the .NET
  Desktop Runtime first
- the cost is file size (roughly 70 MB compressed), which is a good trade

The settings live in a publish profile rather than the `.csproj` so that ordinary
debug builds do not also copy the entire runtime into their output folder.

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
