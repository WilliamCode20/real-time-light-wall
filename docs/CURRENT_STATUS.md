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

**Procedural animations (12):** Meteor, Sparkle Storm, EQ Bumper, Beat Flash,
Tempo Pulse, Starburst, Breathing, Wiggle Breathing, EQ Breathing, Checkerboard
Switch, Fill Horizontal, Fill Vertical

**Diagnostics (1):** Identify Bulb, which lights one bulb at a time so the pin
map can be checked against the relay labels.

All 25 are registered in `EffectCatalog`, and the window builds its buttons from
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

Two columns: controls on the left, the two walls and the readouts on the right at
a fixed 7:5 aspect. The original single-column layout pushed the wall off the
bottom of the screen once the controls grew.

- effect buttons generated from the catalog, with descriptions as tooltips
- the active effect's button is highlighted
- status line shows what is playing and what it does
- live frame-rate readout
- redraws via `CompositionTarget.Rendering` at ~60 fps
- only changed cells are restyled; brushes are created once and frozen

#### Three tabs, not one long stack

The controls were a single scrolling column, categorised but unbroken, which made
finding anything a matter of scrolling past everything else. They are now:

| Tab | Holds |
|---|---|
| **Patterns & Animations** | Static patterns, pre-set animations, and the procedural ones that ignore the music |
| **Audio Reactivity** | Every effect that listens, plus the whole audio capture panel |
| **Connections & Testing** | Arduino connection, hardware check, virtual wall faults, packet preview |

**Which tab an effect lands on comes from the effect**, via
`IWallEffect.ReactsToAudio`, not from a list kept in the window. A list would need
editing every time an effect was added and would be wrong the first time somebody
forgot — the same reasoning that put the buttons in the catalogue to begin with.

`Diagnostics` deliberately gets no generated button. Identify Bulb is reached
through the Hardware Check panel, which gives it a readout naming the bulb four
ways plus Previous and Next — controls a plain button in a row of show effects
could not. That also closes the old gap where a bring-up tool sat among the show
effects.

#### Controls that appear only when they apply

Speed and the centre offsets are applied by the engine to whatever is playing, so
they are always on screen. The rest are not: the meteor tail means nothing unless
Meteor is running, and the fill pacing means nothing unless a Fill and Clear is.

Each effect declares which it reads through `IWallEffect.Controls`, and the window
shows exactly those. A slider on screen is a promise that dragging it does
something, and leaving all of them visible invites the reasonable conclusion that
they ought to work.

Worth noting what falls out of that: **Beat Flash and Tempo Pulse show no beat
source control**, because each is deliberately pinned to one source. Not offering
a switch is more honest than offering one that would be ignored.

The shared strip sits below the tabs rather than inside them, so that one set of
sliders serves both animation tabs — two copies would drift apart the moment one
was dragged. It hides itself on Connections & Testing, where there is nothing for
it to adjust.

#### Column widths

The right column used to be "whatever is left over" against a fixed-width left
column, which on a wide screen meant two thirds of the window — most of it empty
space either side of walls that had stopped growing. Two stacked walls plus their
labels and readouts run out of *height* long before width, so past a point the
extra width does nothing.

It is now capped at 600, measured rather than guessed: the walls settle at about
505 wide on a maximised window here, so the cap leaves them room to breathe
without the gulf beside them. On a narrow window the cap never binds and the
column simply takes its half.

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

The threshold **moves with the music** rather than being fixed. A beat is not
"louder than some number" but "a bigger jump than this music has been making
lately", which is much closer to what a listener notices.

Specifically it is the **middle recent flux reading plus a share of how much
readings normally vary** — not the average times a multiplier, which is what it
used to be.

That change was made because the sensitivity slider needed moving for nearly
every song, anywhere from about 1 to 2.5, which is no use to somebody running a
set. The cause is that an average is moved by the *shape* of the distribution and
not just its level, in two directions at once. On sparse material the occasional
huge flux spike drags the average above where ordinary readings sit, so the bar
ends up out of reach of the very hits it is meant to catch. On dense, compressed
material the readings bunch together so the average sits up among the peaks and
almost nothing clears a multiple of it.

Measuring the middle reading is immune to a handful of large values, and adding a
share of the spread rather than multiplying the level is what makes one setting
mean the same thing on both kinds of track. Measured across three synthetic
tracks at identical tempo and peak loudness but very different dynamics:

| Setting | Sparse | Moderate | Dense |
|---|---|---|---|
| 1.0 | 120 BPM, 100% | 119 BPM, 32% | 143 BPM, 29% |
| 3.0 | 120 BPM, 100% | 119 BPM, 46% | 119 BPM, 42% |
| **5.0** | **120 BPM, 100%** | **120 BPM, 94%** | **120 BPM, 88%** |

That bottom row is the whole point — one setting reading all three correctly.
Under the old threshold no such value existed. There is a test pinning it.

`Sensitivity` is therefore now a **count of deviations, not a multiplier**, and
defaults to 5. The old values around 1.4–1.7 mean nothing under it. Synthetic
material only, so it is a well-founded starting point rather than a settled
answer — real music has structure that noise does not, and it still wants
dialling in by ear.

Three separate guards, each ruling out a different false alarm: a minimum flux
(so near-silence does not trigger), a rising-edge requirement (so the readings
after a peak are not each counted), and a minimum gap of 0.20 s (so one drum hit
is not reported three times).

The gap was 0.12 to begin with, which is about what covering the width of one
hit needs. Raised after listening: the extra is doing a second job, ignoring some
of the sounds that are real but not on the beat. 0.20 still allows up to 300
beats a minute, comfortably faster than the fastest tempo ever reported, so no
real beat is suppressed by it.

Detection works from the **raw** band strengths, not the smoothed ones. Smoothing
rounds off exactly the sharp rise an onset consists of.

### Tempo

`TempoEstimator` tries **every tempo** in the reportable range and asks of each
one: how much of what was just heard would make sense at this speed? The test is
that the distance between any two sounds should be a whole number of beats. Every
pair of recent sounds votes for the tempos it fits, and the tempo with the most
votes wins.

A vote counts for less the more beats apart the pair is, because two sounds one
beat apart are far stronger evidence than two sounds five beats apart — at five
beats, almost any tempo can find some multiple that nearly fits. That weighting
is also what settles the choice between a tempo and double it: both explain the
same sounds, but the slower one explains them at lower multiples.

Tempo is genuinely ambiguous: the same music at 70 and at 140 are both correct
descriptions, and listeners disagree about this constantly. Only tempos inside
70–180 BPM are ever tried, so a slow track may be reported at double its written
tempo. For driving lights that barely matters.

**Confidence** is the share of recent sounds that actually land on the beat. A
clean four-to-the-floor track gives something near 1; a chorus with a syncopated
synth over the same beat gives perhaps 0.5, which is honest — half the sounds
genuinely are not on the beat, and the tempo underneath is still right. Worth
having because a confident wrong answer and an unconfident one look identical
without it.

#### Trust — how hard a settled tempo is to shift

Once settled, the estimate **resists being moved**, and how hard depends on what
it has earned. A rival has to beat it by a margin and keep winning for a while;
both requirements scale with **trust**, a 0–1 measure that grows while beats keep
confirming the tempo and erodes while they do not.

This replaced a fixed margin and a fixed three-second hold, which defended a
tempo adopted four seconds ago exactly as hard as one that had held confidently
for a whole verse. That is why a break could shove the estimate around so easily:
the incumbent was re-judged purely on recent evidence with no memory of how well
established it was.

**Trust erodes as well as builds, and that is the load-bearing part.** Trust that
only accumulated would be a trap — a four-minute song would build a position
nothing could dislodge and the next track would never get a look in. Because it
decays while the evidence is against it, *how long a switch takes is set by the
decay rate, not by how long the previous tempo ran*. Measured:

| 120 BPM held for | Trust at the change | 150 BPM adopted after |
|---|---|---|
| 3 s | 0.05 | 4.9 s |
| 12 s | 0.50 | 7.9 s |
| 30 s | 1.00 | 10.1 s |
| 60 s | 1.00 | 9.6 s |
| 120 s | 1.00 | 9.2 s |

Trust saturates, so half a minute and two minutes give way at the same pace. A
freshly adopted tempo folds in about five seconds, a settled one in about ten —
trust roughly doubles the resistance without ever making it permanent.

**Silence counts against trust too.** It looks harsh on a quiet break and costs
nothing there: a quiet passage produces no challenger, so there is nobody to hand
over to however low trust falls, and the tempo itself is held for 30 s regardless.
What it buys is track changes, which usually carry a gap. Measured with a three
second gap, a new track is picked up in 7.0 s rather than ~10.

**The honest limit.** Resistance to breaks and speed of track changes are the
same number pulling in opposite directions — about ten seconds of each. That is
enough for a short break at a genuinely unrelated tempo and not for a long one.
The way past it is not to tune the trade but to stop breaks producing a
challenger at all, which is what the next section does.

#### A section changing feel is not a section changing speed

Breaks routinely change the *feel* without changing the speed. Measured on a
settled 120 BPM, playing 20 seconds of break at various spacings:

| Break | Before | After |
|---|---|---|
| half-time (1.0 s) | 120.0 ✓ | 120.0 ✓ |
| double-time (0.25 s) | 120.0 ✓ | 120.0 ✓ |
| triplet (0.333 s) | **180.0** ✗ | 120.0 ✓ |
| dotted (0.75 s) | **80.0** ✗ | 120.0 ✓ |
| sparse pad (2.0 s) | **0.0** ✗ | 119.5 ✓ |
| unrelated (0.41 s) | 146.3 | 146.3 — *correctly still a challenger* |

Two findings worth recording, because the first contradicted what this was
started to fix.

**Doubling and halving were already handled.** Only tempos from 70 to 180 are
ever tried, and the scoring prefers whichever explains the sounds at lower
multiples, so half-time and double-time breaks both held a rock-steady 120
before anything was written. The problem was **three-to-two** relationships —
triplet and dotted feels — which had never been considered. All four ratios are
now listed, doubling included, because relying on the range to keep covering that
is luck rather than design.

**A sparse break was wiping the tempo outright.** `Recalculate` answered "too few
sounds to work from" by setting the tempo to zero, which defeated the
hold-through-quiet design from the other direction: the hold in `Update` keeps a
tempo alive for 30 s, but the wipe ran first and it never got a say. Zero means
Tempo Pulse stops dead in exactly the passage it exists to carry. It now leaves a
settled tempo alone and only reports nothing when nothing was ever established.

**The absorption is gated on trust**, and deliberately. It is right mid-track,
where a three-to-two reading means a section changed feel. It is wrong at the
*start* of one, where the estimator may have picked the wrong reading first and
needs to be free to correct itself, and wrong across a track change where the
next song genuinely might be half or one-and-a-half times the last. Trust already
separates those cases.

**It stays selective.** A break at 146 BPM against a settled 120 is not related
by any simple ratio and is still treated as a real challenger — there is a test
guarding that, because absorbing related tempos must not slide into absorbing
everything.

#### The trap this created, and how it was got out of

Reported from listening: *"it got the wrong BPM and just held onto it, even as it
kept detecting beats."* That was this feature, done naively, and it is worth
recording because both obvious fixes are wrong in opposite directions.

**Attempt one — absorb related readings outright.** If the estimator latched onto
180 for music that is really 120, every later reading of 120 was folded back onto
180 and swallowed as agreement. The wrong answer defended itself permanently and
grew *more* trusted the longer it was wrong.

**Attempt two — let a related reading correct the settled one as soon as it
explains the music better.** The break tests caught this within a minute: during a
triplet section the 180 reading genuinely *does* explain the sounds better, so it
corrected straight to 180 — exactly what absorbing was meant to prevent.

**What actually separates the two cases is not which fits better. It is how long
it lasts.** A section that changed feel ends; a wrong multiple does not. So a
related reading is a challenger like any other, just one that has to hold on
three times as long — about 24 seconds at full trust. No realistic break outlasts
that, and a genuinely wrong multiple still puts itself right inside half a minute
without anybody intervening. A test walks the estimator into the wrong multiple
deliberately and checks it finds its way back.

**Trust also has to be earned, not just waited for.** It used to grow whenever
nothing was actively beating the settled tempo, so a mediocre answer still crept
to full trust given a long enough song and then defended itself as though it had
been right all along. Agreement now only counts towards trust when confidence is
at least 0.5; below that a tempo keeps what it has but stops climbing.

### Beat size that looks after itself

The **Auto** tick-box beside Beat size lets the detector keep its own sensitivity
in a workable range, which is what a person running a set actually needs — the
setting that suits one track is often wrong for the next.

**It does not try to find the "right" sensitivity**, because nothing at this level
can measure that; the detector cannot tell a beat from a well-timed guitar chord.
It aims at finding a *plausible number of things*. Music runs 70–180 BPM and
carries off-beat sounds too, so outside roughly 1 to 3.5 detections a second the
knob wants moving in a known direction. That asymmetry — unable to judge
correctness, able to judge plausibility — is what makes this safe to automate when
hunting the true tempo would not be.

Deliberately slow and bounded: it judges over four seconds at a time, moves in
small steps, and never leaves 1.5–12. Tightening is slightly brisker than
loosening, because over-detection reads as noise and wants dealing with promptly
while under-detection reads as restraint.

**Two traps found by testing.** Silence reads as "finding nothing", which would
walk the setting down to its minimum and leave the next track triggering on
everything — so it only judges when there is real audio. And the first upper bound
was set at 5.0 per second, which `MinimumSecondsBetweenBeats` makes *unreachable*:
a detector triggering on absolutely everything sits exactly at that cap and was
read as healthy. A dense track started far too loose stayed there and reported 77
BPM for a 120 BPM signal.

Off by default. Automatic behaviour that quietly disagrees with a slider somebody
has just set is worse than none.

#### The version before this, and how it failed

Worth recording, because it was convincing and wrong.

The first version measured the gap between each sound and the one before it,
doubled or halved each gap until it landed in range, and took the middle value.
It worked well on clean material and fell over on a busy chorus.

**Fault one: doubling a slightly-wrong gap gives a confidently wrong answer, not
a slightly wrong one.** A gap of 0.30 s is too short to be a beat, so it was
doubled to 0.60 and reported as 100 BPM. A gap of 0.20 became 0.40 and reported
150. Sounds landing a little off the grid — which is most of what a busy
arrangement adds — did not blur the answer, they scattered it.

Measured on a simulated 120 BPM track with one extra sound per beat: landing
exactly on the eighth note gave 120 BPM at 100% confidence, but moving that same
sound 50 ms later gave 100 BPM at 52%, and moving it to 0.40 s after the beat
gave **150 BPM at 100% confidence** — completely wrong and maximally sure.

**Fault two, the deeper one: only neighbouring sounds were ever compared.** Once
every beat had a companion sound between it and the next, the real half-second
spacing never appeared as a gap at all. The correct answer was not being
outvoted; it was not on the ballot.

**A wrong turn on the way to the fix.** The obvious repair was to keep the median
but widen it to include pairs further apart. That did fix the tempo — 120 BPM in
every case — but dropped confidence to around 50% even when exactly right,
because many of those wider gaps are legitimately two or three beats long. It
would have fixed the number and broken the thing that tells you whether to trust
the number. Confidence had to be rethought too, not carried over.

**And one thing that turned out not to matter.** The resist-being-moved rule was
added expecting it to be what kept a chorus from dragging the estimate around. It
is not. Running the messy-chorus test with the hold set to zero still gives
120.5 BPM against a true 120 — the scoring does that work on its own. The hold is
a second line of defence for cases the scoring cannot settle, and is proven to
function by turning it *up* rather than off: at sixty seconds a real change from
120 to 150 is correctly refused. That is what the test checks, since testing it
the obvious way would prove nothing.

Verified end to end through the running app on a 120 BPM track with a synth
0.30 s into every beat — the exact case that used to report 100 BPM. It now reads
119 BPM at 53% confidence.

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
listening: how big a jump has to be (`OnsetDetector.Sensitivity`, 1.7) and the
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

### Starburst — the first effect driven by both beat and frequency

A dark wall with one small explosion going off on each detected beat, in a
different place every time.

One burst: a single bulb lights, the four around it join it to make a plus, the
middle drops out, then the plus goes too. Bigger ones do the same thing with more
rings, so the ring travels outward like a ripple.

**Two separate readings from the music**, kept apart so each can be seen working
on its own:

- The **low end sets the size**. A heavy kick throws a ripple most of the way
  across the wall; a light one makes a small plus.
- **Whichever end is leading sets which way the star points.** Bass-led beats
  throw a star pointing north, south, east and west, whose smallest form is the
  four-bulb plus. Beats led by the top end — a hi-hat, a bright stab — throw the
  same star turned to point at the corners, whose smallest form is a four-bulb X.
  Small bursts of the two kinds therefore cannot be mistaken for one another.

**How a star is made out of rings.** Every bulb sits on one of eight arms — four
straight, four diagonal — and anything in the gaps between arms is never lit,
which is what makes the sides fall inward instead of running straight from point
to point. One set of arms then leads and the other trails by a single step, so by
the time the leading points have reached three steps out the trailing ones are
only at two.

**One rule that is load-bearing rather than taste:** a trailing arm never uses its
innermost bulb. The travelling ring is wide enough to touch two rings at once —
that overlap is what makes a burst read as a ripple — but the innermost trailing
bulb is a diagonal neighbour of the middle while the leading ones are its straight
neighbours. Light both while the middle has gone dark and the result is all eight
bulbs around a dark one: a hollow 3×3 square, which is not a star by any reading.

**Two shapes that were tried and dropped.** A solid diamond expanded correctly and
looked dull, because the edge between any two points is a perfectly straight
diagonal line — it reads as a growing lozenge. Putting all eight arms at the same
distance was worse: one step out, eight equidistant arms *are* the eight bulbs
surrounding the middle, so small bursts and the last frame of large ones both
ended on that hollow 3×3. That was not a drawing bug so much as the shape being
geometrically impossible at that size, which is why the fix was to change the
shape rather than special-case the radius. It then reappeared once more from the
ring overlap described above, which is how that invariant came to be written down.

Both readings use band levels measured against their own recent history, which is
what makes "the bass is bumping" mean something even in a quiet passage.

**Bursts can be centred anywhere including the very edge**, and one in a corner
simply shows the quarter of itself that fits. That is worth a note because
`WallFrame.SetCell` throws on a coordinate off the wall rather than ignoring it —
so the effect walks the 35 real bulbs and asks each whether it belongs to the
current ring, rather than generating ring coordinates and trying to set them.
Thirty-five sums a frame is nothing, and it makes an out-of-bounds coordinate
impossible rather than merely unlikely.

**It holds state, which nearly every other effect must not.** A burst is an event
rather than a position: where it appeared and when it started were decided when a
beat arrived, possibly several redraws ago, and cannot be recovered from the
current time. The rule is still honoured where it matters — a new burst only
starts when the beat *count* changes, and where it lands comes from the beat
number rather than a shared generator, so asking about the same moment twice
gives the same picture both times.

**A mistake caught by printing the frames out.** The first version stretched every
burst to fill the whole gap between beats, on the reasoning that the wall should
stay busy. A one-ring burst then had to cover two steps rather than four, so it
crawled — the plus appeared and sat there unchanged for three quarters of the
beat, reading as a blinking plus rather than anything bursting. Big and small hits
also rippled at visibly different speeds, which made a small one look like a
different effect rather than a smaller version of the same one. Every burst now
ripples at the same speed and a small one simply finishes sooner, which is also
what leaves a longer dark gap before the next beat.

### Breathing — a surface that rises and falls

A line lifts off the bottom row, bows up into a rounded arch and sinks back
again, like a chest rising and falling. Beats push it up; between them it lets
back out.

It is a **surface, not a filled shape** — exactly one bulb per column, with
nothing lit underneath. The first version filled each column from the bottom row
upward and the difference is larger than it sounds: as a solid mass it read as a
block growing rather than as something breathing. The eye follows the moving
edge, and filling in behind it buries that edge in a wall of light instead of
leaving it as the thing being watched.

At full stretch and at rest:

```
..###..          .......
.#...#.          .......
#.....#          .......
.......          .......
.......          #######
```

**The arch is a circle, flattened to fit.** Height falls away from the middle the
way it does around the top of a circle: barely at all near the centre, then
faster towards the edges. The circle is shaped as though it were slightly wider
than the wall, which is what lifts the outer columns — an arc drawn exactly the
wall's width reaches zero at the last column, so the ends of the line would stay
pinned to the floor.

That replaced a straight taper, each column simply one row lower than its
neighbour nearer the middle. It was easy to reason about and it looked like a
pyramid: a sharp point in the middle, dead straight sides, and the outer columns
barely lifting. Nothing about it suggested anything being inflated. The rounded
version differs in exactly the two ways that matter — a short flat span across
the top instead of a point, and ends that lift two rows instead of one.

#### Beats push it up, and never pull it down

The behaviour this effect was rewritten for, and the reason it now holds state.

The first version worked the height out purely from how long ago the last beat
was. That had a real virtue: it needed no memory at all and was a pure function of
the moment it was asked about, which is what nearly every effect here is supposed
to be.

It looked wrong with real music, for a reason only visible while listening. Beats
regularly arrive *before* the previous breath has finished sinking. Because the
height came from "time since the last beat", a new beat meant a time of zero,
which meant the floor — so the line snapped all the way down and started again,
cutting off the tail of the movement. A run of quick beats made it slam up and
down repeatedly instead of hovering near the top and breathing there, which is
what a chest actually does when someone is breathing hard.

The height is now something the effect carries forward and beats nudge upward,
rather than something recalculated from scratch. Holding state is still safe
under the repeatability rule: nothing moves unless time has actually advanced, so
a second frame at the same moment steps by nothing, and a beat only registers when
the beat *count* changes, so one beat cannot be counted twice.

**A consequence worth knowing.** The Speed slider now does affect how briskly the
breath rises and falls, because the movement is stepped by elapsed effect time.
The beats still arrive from the music, so at anything other than 100% the two
drift apart. The first version was immune to this because it read the time since
the last beat directly. Worth knowing rather than worth fixing — the slider is a
legitimate way to make the breath lazier or more urgent than the music, and the
alternative was keeping a design that snapped to the floor on every beat.

Gentlest effect in the catalogue for power — exactly seven bulbs at any instant,
whatever the music does.

### Three breathers sharing one envelope

The rise and fall lives in `BreathEnvelope`, shared by all three breathing
effects. They differ only in what shape they draw at a given fullness, which is a
few lines each; the timing underneath is the part with all the care in it, so it
exists once rather than three times.

**Wiggle Breathing** is Breathing that never settles into the same shape twice.
Instead of climbing towards a tidy arch, each breath picks its own wandering
profile — higher on the left perhaps, dipping through the middle, rising again
towards the right.

The profile is a **random walk across the columns**: the leftmost starts somewhere
low and each column steps up or down a little from its neighbour. A walk rather
than an independent roll per column, and the difference matters — rolling each
column on its own gives a row of unrelated spikes, which reads as noise rather
than as a shape. Stepping from the previous column keeps neighbours related, so
the line wanders as though drawn by hand.

The shape is worked out from the beat number rather than drawn fresh each frame.
Drawing fresh would re-roll it sixty times a second and the line would dissolve
into static. Tying it to the beat also keeps the effect repeatable, the same way
Starburst places its bursts.

**EQ Breathing** is seven filled bars jumping to new heights on each beat. Two
things differ from the other two: the bars are filled from the bottom row up
rather than drawn as a moving edge, which is what makes it read as an equaliser;
and each column rolls its height independently, since unrelated neighbours are
exactly what an equaliser looks like.

**It is not EQ Bumper**, which matters because the names are close. EQ Bumper is
honest measurement — each column follows its own slice of the real spectrum, and
silent treble leaves the right-hand columns down. EQ Breathing invents its
heights and follows only the beat, so it is decorative rather than diagnostic and
must never be used to judge whether the audio analysis is working.

It is also the heaviest of the three for power: around twenty of the thirty-five
bulbs on an average beat, and briefly the whole wall on the rare beat where every
bar rolls high. That is a flash rather than a hold, well within what the original
show did routinely.

### Checkerboard Switch — the whole wall changing at once

There are exactly two ways to chequer a wall: every bulb is lit in one of them or
lit in the other, never both and never neither. This shows one, and every beat
swaps to the other.

The wall is therefore **never dark**. Roughly half the bulbs are lit at all times,
and a beat does not turn anything off so much as hand the lighting over to the
other half. That is what gives it its snap — every single bulb changes at once, so
the beat is impossible to miss from across a room.

**It needs no memory**, and is the opposite extreme from the breathing effects. It
never has to remember which board it is showing because the beat number already
says: even beats get one, odd beats get the other. Counting beats is something the
audio side does anyway, so the switching falls out of arithmetic rather than out of
anything held here. Add a bulb's row and column together — even belongs to one
board, odd to the other, which is exactly a chessboard since stepping one place in
any direction flips even to odd. Adding the beat number flips the whole wall.

That makes it a pure function of the moment it is asked about: nothing to reset,
nothing that can get out of step, and drawing the same moment twice gives the same
picture without any care being taken.

It is the one audio effect that **does not** drop to a single lit row while
waiting. Being never dark is the point of it, so with nothing listening it holds a
board still instead. Holding still is signal enough that nothing is being heard.

**Power is different in kind here** and worth stating plainly. This holds around
eighteen of the thirty-five bulbs lit *continuously* rather than touching a high
number for an instant — roughly a hundred milliamps against the two hundred
available. Comfortable, but a sustained load rather than a flash, which is the
distinction the caution in this project is really about.

### Fill and Clear — horizontal and vertical

The wall fills from the middle outward, then empties the same way, one step per
beat. Two versions from one class: horizontal bars spreading up and down from the
middle row, or vertical bars spreading left and right from the middle column.

The emptying spreads outward exactly as the filling did, so what grows is a
**hole in the middle** rather than a wall draining inward from its edges. That
detail is most of the character of the effect, and there is a test pinning it.

#### Two ways to pace it

The same sequence of pictures, timed two ways. **Fill and clear** in Animation
Controls picks between them, and they look quite different.

**One step per beat** is slow and deliberate — every beat moves the wall on by one
picture. Counting outward from the middle, a wall five tall has three positions
and one seven wide has four; filling uses each once and emptying uses each again:

| Version | Steps out | Beats per cycle | At 120 BPM |
|---|---|---|---|
| Horizontal | 3 | 6 | 3 seconds |
| Vertical | 4 | 8 | 4 seconds |

Verified in the running app, counting lit rows on each beat:

```
1  ->  3  ->  5  ->  4  ->  2  ->  0  ->  (repeats)
```

**A whole sweep per beat** is punchier. One beat runs the entire fill in a quick
run of pictures and holds the wall full; the next runs the entire clear and holds
it dark. Two beats for a complete cycle whatever the wall's size. The beat is the
moment a sweep is *launched* rather than the moment the wall moves — the same
relationship a Starburst has with the beat that threw it. Verified live:

```
0 -> 1 -> 3 -> 5 (held) -> 4 -> 2 -> 0 (held)
```

**Memory is needed for one pacing and not the other.** Stepping needs none: the
beat number alone says which picture to draw, since dividing it by the number of
pictures leaves the position as the remainder — the same approach as Checkerboard
Switch. Sweeping does need it, because a sweep is an *event* whose start time was
decided when a beat arrived and cannot be recovered from the current time.

**A mistake worth recording.** The sweep first took its direction from whether the
beat number was odd or even, reasoning that arithmetic on the count could not
drift where a remembered flag might. Two faults, both found by watching it play.
The wall rests showing a single middle bar, so a first beat landing on an odd
number ran a *clear* — and clearing assumes the wall is full, so instead of
emptying it inverted, jumping from one lit bar to every bar but that one. Which
you got depended on nothing more than how many beats the track had played before
the effect was selected. And the drift the parity was guarding against turned out
to be the worse behaviour anyway: if two beats arrive between frames the count
jumps by two, the parity is unchanged, and the same direction runs twice with the
second doing nothing visible. Alternating simply carries on. The first sweep now
always fills.

Power: stepping, the whole wall is lit for one beat in six or eight — a flash.
Sweeping, it is lit for most of every second beat, since the wall holds full while
waiting for the beat that will clear it. Both are comfortable; sweeping is the
heavier and is closer to a hold.

### Choosing where the beat comes from

The project has two answers to "when is the next beat", and they are genuinely
different things rather than two implementations of one thing. **Bursts on** in
Animation Controls picks between them:

- **Detected beats** — beats actually heard. Honest: when the drums stop, so does
  the wall. Inherits every miss and every false alarm.
- **Tempo** — a metronome at the detected tempo. Keeps perfect time and carries
  straight through a passage with nothing to detect, but is a prediction, and does
  nothing at all until a tempo has been worked out.

`EffectParameters.BeatSource` holds the choice and effects read it through
`EffectContext.BeatCount`, so no effect implements the choice itself and adding a
beat-driven effect gets both modes for free. It sits with the cross-cutting
controls rather than beside the beat detection sliders because it is not any one
effect's business — and because two effects on screen should never disagree about
when the beat was.

**Beat Flash and Tempo Pulse deliberately ignore it.** Their job is to show the
difference between the two, so letting either be switched would remove the only
honest reference for judging whether detection is working. There is a test
holding that in place.

Verified end to end the decisive way: with **Tempo** selected, a tempo locked, and
then the music stopped outright, bursts carry on firing. Under **Detected beats**
the wall would go dark. Nothing else distinguishes the two while music is playing,
since both fire at roughly the same moments.

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
