# Next Steps

## Where the project stands

The full chain works: **music → capture → analysis → engine → packets → firmware
→ real bulbs.** The hardware mapping is verified against the physical wall. 294
tests pass.

Everything below is improvement rather than missing foundation.

## Immediate: tuning, not building

These need someone listening to real music, which is the one thing no test can
substitute for.

### 1. Onset sensitivity — done, but worth revisiting

Both defaults have now been dialled in by ear rather than reasoned:

- `MinimumSecondsBetweenBeats` is **0.20**, raised from a starting 0.12.
- `OnsetDetector.Sensitivity` is **1.7**, raised from a starting 1.4 because the
  slider was being pushed up on most material.

Neither is settled forever — they were judged on a fairly narrow slice of music,
and a room with different material may want different numbers. Both are sliders
in the window, **Beat size** and **Beat gap**, with a trigger meter and a beat
lamp beside them, so re-judging them is a one-sitting job rather than one rebuild
at a time.

How to read it:

- flashing too often → raise Beat size
- missing obvious hits → lower it
- double-flashing on one hit → raise Beat gap
- bar not reaching the red line on hits you can clearly hear → Beat size is the
  right knob
- bar comfortably past the line but no lamp → it is one of the timing guards,
  not the size, so Beat gap is the one to look at

Whatever values feel right should become the defaults in `OnsetDetector`.

**Beat Flash** shows it on the wall, and **Tempo Pulse** is the cross-check: if
it locks on well while Beat Flash looks wrong, the problem is sensitivity rather
than the tempo estimate.

### 2. Audio smoothing and sensitivity defaults

The Smoothing slider (0.5) and Sensitivity slider (1.0) both default to guesses.
Whatever values feel right in practice should become the defaults.

### 3. Output rate

30 packets/second is the safe number and the largest single contributor to
audio-to-light latency (~33 ms worst case). The original show demonstrated ~15 ms
dwell times, so 60 Hz is within proven territory.
`WallOutputService.OutputRateHz` is already a settable property, so it is a
one-line change.

**The trade is smaller than this file used to claim.** It said "relay wear",
which is wrong: the relays are solid state, so there are no contacts to wear, and
the bulbs are LEDs, which do not care about switching cycles. Bandwidth is not
the limit either — under 5% of the serial link at 60 Hz.

What does limit it is the zero-cross SSRs, which can only switch as the mains
crosses zero, every 8.3 ms. That slop is a quarter of a 33 ms interval but half
of a 16.7 ms one, so 60 Hz buys perhaps 10–12 ms of the 16 ms it promises on
paper, and past ~120 Hz nothing at all.

Still worth doing once the delay has been felt on the real wall. There is no
meaningful risk to it; the question is only whether the gain is noticeable.

## Next features, roughly in order

### 4. More beat-driven effects

**Starburst** is the first of these and the pattern to copy: bursts fire on the
beat, the low end sets their size and whichever end is leading sets which way the
star points.

**Breathing** is the second, and shows a different shape of the same idea: a
surface rather than a filled shape, and a height it carries forward that beats
push upward rather than restart. Between them the two cover most of what a
beat-driven effect has to decide.

One lesson from it worth carrying forward: **a beat should rarely reset an
animation to its start.** Beats arrive before the previous movement has finished
far more often than they do not, so anything that restarts on a beat spends most
of its life cut off part way through. Pushing a value up and letting it fall
looks right in both the sparse and the busy case.

**Wiggle Breathing** and **EQ Breathing** are the same envelope with different
shapes on top, and are the cheapest kind of new effect to add: `BreathEnvelope`
already handles the beat, the timing and the speed slider, so each is only a
question of what to draw at a given fullness. Two ideas they demonstrate that
transfer:

- Anything randomised per beat should be worked out from the **beat number**, not
  rolled fresh each frame. Rolling per frame re-randomises sixty times a second
  and turns a shape into static.
- Neighbouring columns should be **related** for anything meant to read as a line
  and **independent** for anything meant to read as bars. That single choice is
  most of the difference between Wiggle Breathing and EQ Breathing.

Two things to copy from Starburst rather than reinvent:

- Read `EffectContext.BeatCount`, not `Audio.BeatCount` or `Audio.PulseCount`.
  That is what makes an effect follow the user's **Bursts on** choice between
  detected beats and the tempo metronome, and it costs nothing to get right.
- Use a count rather than a time for anything that happens once per beat. A time
  has to be caught inside a window, and the engine reads audio on its own
  schedule — a short window can be stepped clean over and a long one can be seen
  twice.

Still unused: `AudioFeatures.BeatPhase`, for effects that sweep or fade *across* a
beat rather than firing on it.

`AudioFeatures.BeatPhase` (0→1 across each beat) is exposed and unused. Effects
that sweep, fade or travel *across* a beat rather than blinking on it — a meteor
crossing the wall exactly once per bar, a pattern that changes on the downbeat.

Bar tracking (counting beats into groups of four) would open up more, and is a
small addition on top of `BeatClock`.

### 5. Scene control for a DJ

The real product goal. Which effects respond to which bands, how strongly, what
can be adjusted live, and how a non-programmer picks between them.

This is where `EffectParameters` finally needs to become a per-effect system
rather than one shared object.

### 6. Smaller known gaps

- **Output counters never reset**, so they mix clean and faulty periods and
  cannot be read as a delivery rate. A "reset counters" button beside the fault
  sliders would fix it.
- **No UI control for detaching output** — settable in code only.
- **The Center X/Y offsets clip rather than wrap.** A wrap mode might be worth
  offering.
- **The Speed slider does nothing to the breathing effects' pace relative to the
  music.** It scales their movement but not the beats driving them, so away from
  100% the two drift apart. That is a usable control rather than a fault, but it
  is not obvious from the slider.

Closed since this list was written: `EffectCatalog.Diagnostics` used to be
rendered inline with the procedural animations, putting a hardware bring-up tool
among the show effects. Identify Bulb now lives on the Connections & Testing tab,
reached through the Hardware Check panel.

## Hardware follow-ups

- **Confirm the 5.5 mA figure** by measuring volts across one 270 Ω resistor with
  that bulb lit (~0.72 V confirms it). This is a parallel measurement, so nothing
  needs disconnecting.
- **A driver stage** (five ULN2803A chips, eight channels each) would take the
  Arduino out of the current budget entirely. Only worth it for a future
  installation, not this one.

## Guardrails

- One focused layer at a time
- Keep the simulator working
- Run `dotnet test` before committing
- Keep commits granular with full explanations

## Notes for future sessions

Read `CLAUDE.md` first — build commands, protocol spec, hardware facts and the
ten architectural rules.

Preserve these:

- the simulator stays important; it is not scaffolding
- `WallFrame` is the source of truth for a wall state, `WallEngine` for what is
  displayed, `WallShowClock` for who may touch the engine
- effects stay time-driven; audio reaches them only through `EffectContext`
- all audio analysis stays in Core so it stays testable
- `PacketCommand` values are permanent now firmware is deployed
- comments are part of the deliverable, including the ones recording reasoning
  that turned out wrong
