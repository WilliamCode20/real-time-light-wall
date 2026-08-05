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

### Serial transport

`SerialTransport` in `LightWall.IO` implements the same `IWallTransport`
interface as the loopback, so everything upstream is unchanged — it was the only
new code needed to drive real hardware.

It handles the reset that happens on opening a port: the Arduino watches the DTR
line, which is wired to its reset pin, so connecting reboots the board and the
bootloader swallows everything for the first couple of seconds. Rather than
blocking `Connect` (which would freeze the window), it records when the port
opened and discards packets until the board is ready. `IsWaitingForBoardReset`
and `PacketsDroppedDuringReset` report that state, so the pause is visible rather
than looking like a broken connection.

Writes have a 250 ms timeout, so a port whose far end stopped listening cannot
hang the output thread indefinitely. Failures are recorded in `LastError` and
rethrown for the output service to absorb, which means output resumes by itself
if a cable is plugged back in.

`SerialPortLister` enumerates available ports, sorted numerically so COM9 comes
before COM10.

### Audio capture

`SystemAudioCapture` in `LightWall.IO` uses WASAPI loopback to listen to what the
computer is **playing**, not to a microphone. That means it hears the music
exactly as mixed, with no room echo and no people talking, whatever program is
producing the sound.

The maths lives in Core so it can be tested without a sound card:

- `AudioSampleMath` — RMS and peak from a sample buffer, plus decibel mapping
- `AudioLevelTracker` — fast attack, slow release smoothing
- `AudioFeatures` — an immutable snapshot, swapped in atomically so the audio
  thread never shares mutable state with anything

Two details that matter more than they look:

**Decibel mapping.** Hearing is logarithmic, so ordinary music sits at an RMS of
roughly 0.05 to 0.2. Driving anything from that directly would keep it pinned
near the bottom of its range. Mapping through decibels with a -60 dB floor
spreads real music across the whole range. Verified in practice: a ringtone reads
about 0.6 on the meter rather than about 0.05.

**Fast attack, slow release.** Rising almost instantly and falling gently is what
turns a drum hit into a visible pulse instead of a one-frame flicker, while
smoothing away the constant jitter between waves. Simple averaging would blunt
the attack, which is the part worth showing.

**Silence needs detecting explicitly.** Windows sends no buffers at all when
nothing is playing, rather than sending zeros — so without a timeout the level
would freeze wherever the music left it.

### Audio driving the wall

`AudioFeatures` reaches effects through `EffectContext`, the same way elapsed
time does. `WallShowClock` reads the latest snapshot on each tick and hands it to
the engine; the engine passes it through. No locking on the audio side, because
snapshots are immutable and swapped whole.

**EQ Bumper listens.** With capture running, bar heights follow the measured
loudness: louder music means taller bars, and stopping the music lets the wall
fall dark. With nothing listening it shows a single lit row — running and
waiting, without inventing motion that might be mistaken for a response to sound.

Those two cases are told apart by `EffectContext.IsAudioActive` rather than by
the level being zero, because "nobody is listening" and "listening to silence"
deserve different answers.

**No sine wave anywhere.** An earlier version used a travelling sine wave to vary
the height across columns. It was actively misleading — peaks rolled across the
wall that had nothing to do with the music, making it impossible to tell at a
glance whether the wall was really following the sound. An effect that invents
movement makes the real movement harder to trust.

**All seven columns therefore move together**, because overall loudness is the
only number describing the music so far. That is the honest picture of what is
currently measured. Frequency bands are what will make columns differ for
measured reasons.

### Automatic volume adjustment

`AudioGainController` measures loudness against the loudest moment of the last
few seconds rather than against absolute full scale. A reference level jumps up
instantly to any new peak and drifts slowly down when nothing loud happens.

The effect is that the system volume setting cancels out: the same music at half
volume drives the wall to the same heights as at full. There is a test proving
exactly that.

The limitation worth knowing: it cannot distinguish quiet music from loud music
played quietly, and left alone during a soft passage it would keep winding the
gain up. `MinimumReference` is the floor below which it refuses to amplify, so
real silence stays dark instead of turning room hiss into a light show.

A response curve (`Contrast`) then pushes quiet and loud further apart so the
bars use the whole height of the wall, and a Sensitivity slider gives manual
control on top for taste.

Note that a completely steady tone will correctly pin the bars at full height —
it genuinely is constantly at its own recent maximum. Music with transients
swings; a test signal without them does not.

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

### Serial wiring in the UI

`SerialTransport` and `SerialPortLister` are written and tested, but there is no
way to select a port from the window yet — the app still attaches the loopback at
startup. Adding a port dropdown and a connect button is the next step, and is the
only thing standing between the app and real hardware.

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
