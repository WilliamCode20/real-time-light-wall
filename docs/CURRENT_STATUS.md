# Current Status

## Working Right Now

The full chain works end to end: **music → capture → analysis → engine → packets
→ firmware → real bulbs.** The hardware mapping was verified against the physical
wall on 2026-08-04.

The solution builds clean with 297 passing tests.

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

**Procedural animations (5):** Meteor, Sparkle Storm, EQ Bumper, Beat Flash,
Tempo Pulse

**Diagnostics (1):** Identify Bulb, which lights one bulb at a time so the pin
map can be checked against the relay labels.

All 18 are registered in `EffectCatalog`, and the window builds its buttons from
that list. Adding an effect is a one-entry change.

One gap: `Diagnostics` is a separate list in the catalog, but the window still
renders it inline with the procedural animations rather than under its own
heading.

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
glance whether the wall was really following the sound. Everything the wall does
now is measured.

### Frequency bands

`FourierTransform` (written out in Core rather than taken from NAudio, so the
whole analysis chain stays testable with no audio hardware) splits the sound into
seven bands, one per wall column. Bass on the left, treble on the right, the way
every equaliser display reads.

Band ranges are spaced logarithmically because hearing works in ratios: the gap
from 100 to 200 Hz sounds like the same musical distance as 1000 to 2000 Hz.
Evenly spaced bands would put nearly everything musical into the first column.

**Each band has its own automatic gain.** This is what makes the high columns
usable at all. Bass typically carries a hundred times the energy of treble, so
measured against a shared reference the treble columns would never move. Measured
against its own recent history, a quiet hi-hat is loud *for a hi-hat*.

Verified: a bass tone lights the low columns and leaves the high ones dark, a
treble tone does the reverse, and a quiet treble tone still fills its own column.

**One reasoning error worth recording.** Band strength was first computed by
averaging the frequency bins in each range, on the argument that wide bands would
otherwise dominate. That was wrong twice over: a hi-hat occupying two of two
hundred bins got divided into nothing, so the treble columns read exactly zero;
and the concern about wide bands was unfounded anyway, since per-band gain
normalises whatever scale a band naturally sits at. It now sums the squares and
takes the root, which is the actual energy in that stretch of spectrum and gives
a pure tone the same reading regardless of band width.

### Smoothing

Real music initially looked jittery — individual bulbs flickering on and off for
a frame or two, giving an energy hard to read as connected to the song. Three
separate causes, each fixed:

**Chatter at row boundaries** was the biggest. With five rows, a band sitting
near the halfway point of a row flips between two heights on the tiniest
fluctuation, and seven columns doing it independently reads as static. The cause
is not noisy audio — it is that a boundary is infinitely sharp.

`BarHeightSmoother` adds hysteresis: once a bar settles, it takes more than a
hair's movement to shift it. The same trick a thermostat uses. A side benefit is
that very quiet bands no longer light their first row and twitch.

**Quiet bands shimmering.** The automatic gain divides by a reference that
shrinks when nothing loud happens, so a band with no content — sub-bass on a
track with no deep bass — ended up dividing one tiny number by another and
swinging wildly on inaudible noise. `AudioGainController.NoiseGate` reports zero
below a threshold instead of amplifying.

**A jagged top edge.** Neighbouring columns moving independently gave a sawtooth
rather than the rolling curve an equaliser is expected to have. Bands now borrow
a little from each side. This is honest rather than decorative: adjacent
frequencies in real music genuinely are related, and the band boundaries were
always somewhat arbitrary.

A **Smoothing** slider controls the last two together — release time and
neighbour blending — from raw and twitchy to slow and flowing. The attack is
deliberately never slowed: however smooth the wall should look, a drum hit should
land the moment it happens.

### Beat detection

`OnsetDetector` spots the moment a new sound *starts* — which is what a beat
actually is. A sustained bass note is loud its whole length but begins only once;
detecting loudness would flash for the entire note, detecting onsets flashes once.

It uses **spectral flux**: comparing each band separately and adding up only the
increases. A hi-hat landing over a held bass note raises the high bands while the
low ones are unchanged, so the flux jumps even though the overall level barely
moves. Only increases count — a sound ending is not a sound starting.

The threshold **follows the recent average** rather than being fixed. A beat is
not "louder than some number" but "a bigger jump than this music has been making
lately", which is much closer to what a listener notices, and works across dense
and sparse material alike.

Three separate guards, each ruling out a different false alarm: a minimum flux
(so near-silence does not trigger), a rising-edge requirement (so the readings
after a peak are not each counted), and a minimum gap of 0.12 s (so one drum hit
is not reported three times).

Detection works from the **raw** band strengths, not the smoothed ones. Smoothing
rounds off exactly the sharp rise an onset consists of.

### Tempo

`TempoEstimator` takes the **median** of recent gaps between beats, not the
average — one missed beat produces a doubled gap that would drag an average
upward, and the median simply ignores it.

Tempo is genuinely ambiguous: the same music at 70 and at 140 are both correct
descriptions, and listeners disagree about this constantly. Gaps are folded by
doubling or halving into 70–180 BPM, so a slow track may be reported at double
its written tempo. For driving lights that barely matters.

**Confidence** is reported alongside, as the fraction of recent gaps agreeing
with the estimate. Worth having because a confident wrong answer and an
unconfident one look identical without it. Observed in practice: a phone ringtone
gives a number with 33% confidence, correctly signalling that it has onsets but
no real beat.

**Beat Flash** flashes the whole wall on each detected beat. Deliberately the
crudest possible visual — anything more elaborate would obscure whether a flash
was early, late or missing, which is exactly what needs spotting while tuning.
It flashes on *detection* rather than on prediction from the tempo: a predicted
flash would look convincing whether or not the detection underneath it worked,
hiding the faults it exists to reveal.

### Tempo Pulse — keeping time through the quiet

`BeatClock` is a metronome running at the estimated tempo. Once the tempo is
known it keeps counting whether or not anything is being played, so a breakdown
still pulses in time. Detected beats keep it *aligned* rather than triggering it,
nudging the phase a fraction of the way toward where it should be — snapping
would lurch on every slightly-off detection, nudging shrugs those off while a run
of them pulls it into line.

**The tempo is held through quiet passages.** An earlier version wiped it after
three seconds without beats, which meant exactly the passages where holding the
beat matters most left the wall dead. It now holds for 30 seconds while
*confidence* fades instead — so anything reading these values can tell "120,
measured just now" from "still 120, but unconfirmed for a while".

`BeatPhase` (0 to 1 through each beat) is exposed for effects that want to sweep
or fade across a beat rather than merely blink on it.

**Two beat modes, deliberately both.** Beat Flash fires on beats actually heard —
honest, right for percussive material, but goes quiet when the music does. Tempo
Pulse predicts — carries through gaps, but can drift confidently if the tempo
estimate is wrong.

### Tuning beat detection by ear

Two settings decide what counts as a beat, and both can only really be judged by
listening: how big a jump has to be (`OnsetDetector.Sensitivity`, 1.4) and the
shortest allowed gap between beats (`MinimumSecondsBetweenBeats`, 0.12).

They are now sliders in the window — **Beat size** and **Beat gap** — rather than
numbers in the code. That matters more than it sounds. Tuning by ear needs the
change to happen while the music is still playing; with a rebuild in the loop you
cannot compare a setting against the one you just left, and the defaults survive
by default.

Beside them is a **trigger meter**. The bar is how close the sound is right now
to counting as a beat, and the red line is the point it has to reach. Missing a
hit by a hair and missing it by miles look identical from the wall alone, and
they call for opposite responses — one means nudge the slider, the other means
the slider is the wrong thing to be touching.

A **lamp** beside the meter lights on each detected beat. It is separate from the
bar deliberately, for two reasons. Reaching the line is necessary but not
sufficient — a beat also has to be on the way up and far enough after the last
one — so a bar sitting above the line with no beats is the normal look of a
sustained note rather than a fault. And the lamp works from the time since the
last beat rather than a momentary value, so unlike the bar it cannot fall down
the gap between two screen redraws.

**One mistake worth recording.** The meter first stored readings exactly as they
arrived, on the reasoning that the bar gets clamped when it is drawn anyway, so
an oversized value could not hurt. It could. A hit landing after a quiet moment
is measured against a very low threshold and reads not 2 or 3 but sometimes 20,
and draining 20 units at 6 a second takes over three seconds — by which point
several more beats have topped it up. The meter pinned at full the instant music
started and stayed there. It was caught by playing something and watching, not by
any test, and it is exactly the failure that mattered most: a meter that looks
plausible while being wrong is worse than no meter, because it gets trusted.
Readings are now capped at the top of the scale before being remembered.

Verified against a synthetic 120 BPM track: the bar spikes past the line on each
hit and drains back to near zero before the next, the lamp fires on exactly those
frames, and the tempo readout settles at 118 BPM with 100% confidence.

### Latency budget

Roughly 60–90 ms worst case from sound to bulb:

| Stage | Cost |
|---|---|
| Windows audio buffer | ~10 ms |
| FFT window (1024 samples) | ~21 ms |
| Attack smoothing | 5–10 ms |
| Engine tick at 120 Hz | ~8 ms |
| Output rate limit at 30 Hz | ~33 ms |
| Serial | ~1 ms |
| Zero-cross relay | ~8 ms |

The largest controllable cost is the output rate limit, not anything audio-side.
Raising it to 60 Hz would halve that and is within what the original show
demonstrated, at the cost of more relay switching. The FFT window is the audio
trade: halving it halves that delay but leaves the bass resolved by barely one
bin. The relay's ~8 ms is physics and cannot be recovered.

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

297 tests covering the wall model, the exact byte layout of the protocol,
round-trip packing, effect repeatability, engine behaviour, the receiver's
stream handling under deliberately injected faults, the output pipeline end to
end, and the whole audio chain — loudness, automatic gain, frequency bands,
smoothing, onset detection and tempo estimation.

The audio tests are worth singling out. They feed in signals whose answers are
known in advance — a 100 Hz tone, a synthetic drum track at a stated tempo — so
they check against the right answer rather than against a judgement call. None
of them needs a sound card or anything playing.

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

### Serial wiring in the UI

The window has a port dropdown, Refresh, Connect and Disconnect. Connecting
**adds** the real wall alongside the virtual one rather than replacing it, which
is the project's most useful diagnostic: if both walls agree and the hardware
disagrees, the fault is wiring, firmware or a relay; if the virtual wall is
already wrong, the fault is upstream and no cable is involved.

### Arduino firmware

Written, deployed and verified against the physical wall. It is a translation of
`VirtualWallReceiver` — the same byte-at-a-time state machine, sync hunting,
checksum validation and watchdog — which is why it worked with so little
debugging on the board itself.

**Hardware Check** in the window lights one bulb at a time so the pin map can be
checked against the relay labels. That is how the mapping was confirmed on
2026-08-04, and it found nothing wrong: bulbs light top-left to bottom-right in
the expected order.

## Not Yet Implemented

Nothing in the original plan is outstanding. What remains is improvement rather
than missing foundation, and is listed in `NEXT_STEPS.md`. In short:

- **Tuning by ear.** The beat sliders now exist but the defaults have not been
  dialled in against real music. Same for the Smoothing and Sensitivity defaults.
- **Output rate.** 30 packets a second is the safe number and the largest single
  contributor to audio-to-light delay. 60 is within proven territory; the trade
  is relay wear.
- **Beat-driven effects.** `AudioFeatures.BeatPhase` runs from 0 to 1 across each
  beat and nothing uses it yet. Bar tracking would open up more.
- **Scene control for a DJ.** The real product goal, and where
  `EffectParameters` finally needs to become a per-effect system rather than one
  shared object.

## Current Development State

The project has a working visual engine, a tested protocol, a real audio analysis
chain, firmware running on the board, and a clean separation between logic and
interface. Everything in `LightWall.Core` — which is all the analysis and all the
effects — is testable with no window, no sound card and nothing playing.

The work from here is tuning and features rather than plumbing.
