# Next Steps

## Current Priority

The next major priority is to move from simulator-only output to real transport/output plumbing.

That means the next development phase should focus on serial transport before beginning full audio integration.

## Recommended Immediate Next Steps

### 1. Add serial communication on the desktop side

Build a serial service that can:

- enumerate COM ports
- connect/disconnect to the Arduino
- send the current 8-byte frame packet
- report connection state

Suggested project location:

- `LightWall.IO`

### 2. Add a simple serial test path in the app UI

The app should eventually expose a minimal hardware test flow:

- choose COM port
- connect
- send current frame
- optionally enable live-send mode during animation playback

This should be added conservatively, not as a giant UI overhaul.

### 3. Implement Arduino packet receive/apply logic

Use the current packet design to:

- wait for start byte
- validate command
- validate checksum
- unpack the 5-byte payload
- apply each bit to the mapped wall pins

### 4. Validate end-to-end hardware output

Once serial exists, test:

- static frames
- simple animations
- mapping correctness
- update stability
- timing behavior

## After Serial Transport Works

### 5. Add audio capture only

Do not jump straight to full reactive logic.

First add:

- system audio capture on Windows
- basic visualization / debugging
- maybe level meters and simple bands

### 6. Add audio feature extraction

After audio capture works, add:

- overall level
- bass / mid / treble energy
- smoothing
- onset/transient detection
- eventual beat confidence / BPM estimation

### 7. Map audio features to visual parameters

Only after the above layers work should audio begin driving:

- animation speed
- density
- pattern selection
- scene changes
- accent behaviors

## Near-Term Guardrails

Avoid doing all of these at once:

- serial
- audio
- UI overhaul
- major refactor

Preferred pattern:

- one focused layer at a time
- keep the simulator working
- keep commits small
- keep architecture understandable

## Good First Prompt for a New Claude Code Session

A useful first Claude Code prompt would be:

"Read the docs in the docs/ folder first, then inspect the codebase and summarize the current architecture, implemented features, and next best step. Do not edit anything yet."

## Notes for Future Agent Sessions

Any new agent should preserve these truths:

- simulator remains important
- `WallFrame` remains the source of truth
- serialization format should stay consistent unless intentionally revised
- desktop app is the main intelligence layer
- Arduino is primarily an output target
