# Next Steps

## Where the project stands

The full chain works: **music → capture → analysis → engine → packets → firmware
→ real bulbs.** The hardware mapping is verified against the physical wall. 294
tests pass.

Everything below is improvement rather than missing foundation.

## Immediate: tuning, not building

These need someone listening to real music, which is the one thing no test can
substitute for.

### 1. Onset sensitivity

`OnsetDetector.Sensitivity` is 1.4 and `MinimumSecondsBetweenBeats` is 0.12.
Both are reasoned defaults that nobody has dialled in by ear.

Use **Beat Flash** to judge:

- flashing too often → raise Sensitivity
- missing obvious hits → lower it
- double-flashing on one hit → raise the minimum gap

**Tempo Pulse** is the cross-check: if it locks on well while Beat Flash looks
wrong, the problem is sensitivity rather than the tempo estimate.

### 2. Audio smoothing and sensitivity defaults

The Smoothing slider (0.5) and Sensitivity slider (1.0) both default to guesses.
Whatever values feel right in practice should become the defaults.

### 3. Output rate

30 packets/second is the safe number and the largest single contributor to
audio-to-light latency (~33 ms worst case). The original show demonstrated ~15 ms
dwell times, so 60 Hz is within proven territory and would halve that. Worth
deciding deliberately once the delay has been felt on the real wall — the
trade-off is relay wear.

## Next features, roughly in order

### 4. More beat-driven effects

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
- **`EffectCatalog.Diagnostics`** exists as a separate list but the window still
  renders it inline with the procedural animations.

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
