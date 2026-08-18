# CLAUDE.md

Guidance for Claude Code when working in this repository.

## What this project is

A Windows desktop app driving a physical 5x7 light wall (35 individually switched
bulbs) in real time, reacting to music playing on the computer. The app is the
brain; an Arduino Mega 2560 is the output device.

The end goal is something a DJ or venue operator could be handed and use — not a
developer tool.

**The full chain works today**: music → capture → analysis → engine → packets →
firmware → real bulbs. The hardware mapping has been verified against the
physical wall.

## Working style for this repo

The repository owner is new to C#, .NET and WPF. That shapes how to work here:

- **Comments are part of the deliverable.** This codebase deliberately carries
  much heavier comments than a typical professional project. Match the existing
  density. Explain *why* a thing is done, not just what it does, and write for
  someone who has not met the language feature before.
- **One layer at a time.** Do not combine a refactor, a new subsystem and a UI
  overhaul in one change. Build, run, and commit between layers.
- **Prefer clarity over cleverness.** If a shorter expression needs a paragraph
  of explanation, write the longer obvious version instead.
- Explain reasoning in replies rather than only stating conclusions.
- **Record reasoning that turned out wrong.** Several comments explain a
  plausible-sounding approach that failed and why. Those are deliberate — keep
  them, and add to them.
- Commits are granular, one layer each, with a full explanation in the body.

## Commands

```bash
dotnet build "desktop-app/LightWallController/LightWallController.slnx"
```

```bash
dotnet test "desktop-app/LightWallController/LightWall.Tests/LightWall.Tests.csproj"
```

```bash
dotnet run --project "desktop-app/LightWallController/LightWall.App"
```

Single self-contained .exe for a non-developer:

```bash
dotnet publish "desktop-app/LightWallController/LightWall.App" -p:PublishProfile=SelfContained
```

## Project layout

| Project | Target | Purpose |
|---|---|---|
| `LightWall.Core` | `net10.0` | Wall model, effects, engine, clock, transport, packet format, virtual wall, **all audio analysis**. No UI, no platform dependencies. |
| `LightWall.App` | `net10.0-windows` | WPF simulator window. The only project that knows about WPF. |
| `LightWall.IO` | `net10.0-windows` | Real hardware and system I/O: `SerialTransport`, `SystemAudioCapture`. Windows-specific because WASAPI is. |
| `LightWall.Tests` | `net10.0-windows` | xUnit tests. **294 of them.** Windows-targeted only because it references IO. |

Shared build settings live in
`desktop-app/LightWallController/Directory.Build.props`.

## Architectural rules

These are load-bearing. Breaking them causes real problems later.

1. **`LightWall.Core` never references WPF or any platform library.** It targets
   plain `net10.0` specifically so this is impossible by accident. Logic that
   would still make sense with no screen attached belongs in Core.

2. **`WallEngine` is the single authority on what the wall shows.** The window
   draws what the engine says; it decides nothing itself.

   `WallShowClock` owns the engine and runs it on a background thread. Nothing
   else touches the engine directly — go through `clock.Modify(...)` to change it
   and `clock.CopyCurrentFrameTo(...)` to read it. Do not add locking inside
   `WallEngine`; it is deliberately a simple single-threaded class.

3. **Effects are driven by time, not frame count.** `EffectContext.TimeSeconds`
   is the input. This decouples animation pace from redraw rate and lets the
   screen and the hardware run at different rates.

4. **Effects should be repeatable.** The same time value should produce the same
   frame. Random effects use `EffectContext.CreateRandomForStep(step)` rather
   than a shared generator — otherwise they flicker, because the screen redraws
   far more often than they change. Tests enforce this across the whole catalog.

   *One deliberate exception:* `EqBumperEffect` holds a `BarHeightSmoother` and
   so depends on history. Audio-reactive effects were never pure functions of
   time anyway. What the rule really protects — the same question asked twice
   giving the same answer — still holds, because the smoother settles in one step.

5. **Effects must clear the frame they are given.** The engine reuses one frame
   object rather than allocating per frame, so it arrives holding the previous
   contents. There is a test enforcing this.

6. **Adding an effect means adding one entry to `EffectCatalog`.** The window
   builds its buttons from the catalog. Do not hard-code effect buttons in XAML.

   Two declarations on the effect itself decide how the window treats it, and
   both default to the quiet answer so most effects say nothing:

   - `ReactsToAudio` puts it on the **Audio Reactivity** tab rather than
     **Patterns & Animations**.
   - `Controls` lists the effect-specific sliders it reads, and only those are
     shown while it plays. Speed and the centre offsets are the engine's and are
     always visible.

   Never decide either of these from a list of effect names in the window. That
   list would need editing alongside the catalog and would be wrong the first
   time somebody forgot — the fault this rule exists to prevent.

7. **Shared state crosses threads only as a copy, taken under a lock.** Three
   background threads exist — the show clock, the output service, and the audio
   callback. None reaches into anything else's state. Audio goes further and
   takes no lock at all: `AudioFeatures` snapshots are immutable and swapped by
   reference, so a reader sees one complete moment or the previous one.

8. **Output is rate-limited and never queued.** The wall is fed at 30 packets a
   second regardless of engine tick rate; frames generated in between are skipped
   rather than stored. Queueing would make the wall lag further behind reality
   over time; dropping keeps it at worst one frame late.

   This is also the single largest controllable contributor to audio-to-light
   latency (~33 ms worst case). Raising it to 60 Hz is within what the original
   show demonstrated. `WallOutputService.OutputRateHz` is already a settable
   property, so it is a one-line change.

   **What the trade actually is.** An earlier version of this note said "relay
   wear", which is wrong and worth correcting: the relays are *solid state*, so
   there are no contacts to pit, and the bulbs are LEDs, which do not care about
   switching cycles. Bandwidth is not the limit either — 9-byte packets at 60 Hz
   is 540 bytes/second against the ~11,500 that 115200 baud carries, under 5%.

   The real limiter is the **zero-cross behaviour**. A zero-cross SSR can only
   change state as the mains waveform passes zero, every 8.3 ms on 60 Hz mains.
   At a 33 ms update interval that slop is a quarter of the period; at 16.7 ms it
   is half. So 60 Hz does not buy a clean halving — expect to feel perhaps 10–12
   ms of the 16 ms on paper. Past ~120 Hz it buys nothing at all, since the
   hardware cannot switch faster than the crossings.

9. **All audio analysis lives in Core, not IO.** `AudioAnalyser` is the front
   door. Everything it does is arithmetic, so it is testable against signals whose
   answers are known in advance — feed in a 100 Hz tone, check the bass band
   lights and the treble does not. `LightWall.IO` only asks Windows for buffers
   and hands them over. Do not move analysis to IO for convenience; it becomes
   untestable the moment it needs a sound card.

10. **Effects read audio through `EffectContext`, never from a device.** Adding
    an effect touches no audio code; adding a measurement touches no effect code.

## The output pipeline

```
WallShowClock  --> MainWindow          (draws, ~60 Hz)
               --> WallOutputService   (samples 30 Hz, builds packets)
                        --> CompositeTransport
                                 --> LoopbackTransport  (virtual wall, always on)
                                 --> SerialTransport    (real wall, when connected)
```

Connecting a serial port **adds** the real wall alongside the virtual one rather
than replacing it. That is the project's most useful diagnostic: if both walls
agree and the hardware disagrees, the fault is wiring, firmware or a relay; if
the virtual wall is already wrong, the fault is upstream and no cable is involved.

`VirtualWallReceiver` is a software model of the firmware's receiving half — both
what makes hardware-free development possible and the reference the C++ firmware
was translated from. Written byte-at-a-time with a fixed buffer and no allocation,
deliberately in the shape an Arduino needs.

`LoopbackTransport` can drop and corrupt bytes on purpose via
`ByteDropProbability` and `ByteCorruptionProbability`, exposed as UI sliders. Use
it when changing anything in the receive path.

## The audio pipeline

```
SystemAudioCapture (IO, WASAPI loopback)
        --> AudioAnalyser (Core) — the single front door
                --> AudioSampleMath      RMS, peak, decibel mapping
                --> AudioLevelTracker    fast attack / slow release
                --> AudioGainController  volume independence, noise gate
                --> SpectrumAnalyser     FFT into 7 bands, one per column
                --> OnsetDetector        spectral flux, moving threshold
                --> TempoEstimator       scores every candidate tempo
                --> BeatClock            metronome locked to that tempo
        --> AudioFeatures (immutable snapshot)
                --> WallShowClock --> WallEngine --> EffectContext --> effects
```

Non-obvious decisions worth not undoing:

- **Decibel mapping.** Hearing is logarithmic; music sits at ~0.05–0.2 RMS.
  Driving anything from that directly pins it near the bottom of its range.
- **Fast attack, slow release** rather than averaging. Averaging blunts the drum
  hit as much as the jitter, and the hit is the part worth showing.
- **Per-band automatic gain.** Bass carries ~100× the energy of treble; against a
  shared reference the treble columns would never move.
- **Band strength sums squares, not averages.** Averaging divided a hi-hat
  occupying 2 of 200 bins into nothing and the treble columns read exactly zero.
- **Silence must be detected explicitly.** Windows sends *no buffers* when nothing
  plays, rather than sending zeros.
- **Beat detection uses raw band strengths**, not smoothed ones — smoothing
  rounds off exactly the sharp rise an onset consists of.
- **The onset threshold is typical-plus-spread, not average-times-a-multiplier.**
  An average is moved by the *shape* of the flux distribution and not just its
  level, in both directions: on sparse material the occasional huge reading drags
  the bar out of reach of ordinary hits, and on dense material it sits up among
  the peaks so nothing clears a multiple of it. That is why the sensitivity
  slider used to need moving for nearly every song. Measuring the middle reading
  and adding a share of how much readings normally vary makes one setting mean
  the same thing on both kinds of track. `Sensitivity` is therefore a count of
  deviations (about 5), *not* a multiplier — the old values around 1.4–1.7 mean
  nothing under it.
- **Tempo is held through quiet passages** (30 s) while *confidence* fades. An
  earlier version wiped it after 3 s, which erased the estimate during exactly
  the breakdowns where holding the beat matters most.
- **Tempo is found by scoring whole candidate tempos**, not by folding individual
  gaps into range and taking a median. Folding a slightly-off gap produces a
  confidently *wrong* tempo rather than a slightly wrong one — an earlier version
  reported 150 BPM at 100% confidence on a 120 BPM track with a syncopated layer.
  Every pair of recent onsets is compared, not just neighbours, because once
  every beat has a companion sound the true spacing stops appearing as a gap at
  all.
- **Confidence means "what share of recent sounds land on the beat"**, not
  "what fraction of gaps agree". The old meaning stopped applying once pairs
  several beats apart were being considered.

## Serial protocol

Defined in `LightWall.Core/Serialization/WallFrameSerializer.cs`, which carries
the full specification. Summary:

- Fixed **9-byte** packets: `AA 55 <command> <5 payload bytes> <checksum>`
- Checksum is XOR of the command byte and the five payload bytes
- 35 bulbs, one bit each, **row-major**, packed **least-significant-bit first**
- Bulb `N` = `row * 7 + column`, matching `allLights[N]` in the original sketch

Two things to be careful about:

- **Two sync bytes, not one.** `0xAA` alone is an ordinary bulb pattern and
  appears in payloads regularly. A receiver must never treat a lone `0xAA` as
  proof a packet is starting, and must still validate the checksum. Also: on a
  second `0xAA` while waiting for `0x55`, **stay put** — restarting the hunt eats
  the real sync byte of an `AA AA 55 ...` sequence and silently drops a frame.
- **Command numbers are permanent.** Once firmware is deployed, existing values
  in `PacketCommand` must never be reassigned.

## Hardware facts

**All verified against the physical wall on 2026-08-04.** Bulbs light top-left to
bottom-right in the expected order; the mapping is settled.

- Pin map, row-major (`WallHardwareMap`):
  ```
  Row A:  2,  3,  4,  5,  6,  7,  8
  Row B:  9, 10, 11, 12, 13, 22, 23
  Row C: 24, 25, 26, 27, 28, 29, 30
  Row D: 31, 32, 33, 34, 35, 36, 37
  Row E: 38, 39, 40, 41, 42, 43, 44
  ```
- **Relays are labelled A1–E7** in the enclosure: letter = row, number = column
  from 1. Matches the original sketch's own convention. Relay `C4` = bulb 17 =
  pin 27. Note they are *not* arranged in label order in the box.
- **Active HIGH** — SSR control is non-inverting
- **No driver stage.** Relays are switched straight from the digital pins through
  270 Ω resistors, with a shared ground back to one Arduino GND pin. Nothing is
  connected to 5V or 3.3V.
- **~5.5 mA per channel, ~192 mA with all 35 lit** — just under the ATmega2560's
  200 mA absolute maximum. Fine in bursts, as the original show proved. **Avoid
  effects that hold all 35 on for minutes**, and mention the margin if one is
  proposed. Do not raise it as an alarm; the installation has worked this way for
  years.
- Arduino powered from the **barrel jack**; the **USB port is free** for serial
- Bulbs are LEDs and cut near-instantly (a few ms of fade)
- **Pin 13 drives a bulb** as well as being the built-in LED — never use it for
  status blinking

**Switching speed.** The old show used dwell times down to ~15 ms and ran
correctly, so ~30 updates/second is comfortable and 60 is about at the proven
edge.

**Port-open reset.** Opening a serial port toggles DTR, which reboots the Mega.
Its bootloader swallows everything for ~2 seconds. `SerialTransport` handles this
by discarding packets during a settle window rather than blocking `Connect`.

## Things deliberately not built yet

Do not add these speculatively:

- **Beat prediction in Beat Flash or Tempo Pulse.** Those two exist to show the
  difference between what was heard and what was predicted, so each is pinned to
  its own source and neither follows the `BeatSource` setting. A predicted flash
  looks convincing whether or not detection works, which would hide the faults
  Beat Flash exists to reveal.

  Other beat-driven effects *do* choose, via `EffectParameters.BeatSource`, read
  through `EffectContext.BeatCount`. Effects should never reach for
  `Audio.BeatCount` or `Audio.PulseCount` directly — that hard-codes an answer
  the user is supposed to give, and lets two effects on screen disagree about
  when the beat was.
- **Per-effect parameter systems.** `EffectParameters` is one shared object.
  `MeteorTailLength` and `IdentifyBulbIndex` belong to a single effect each;
  `BeatSource` is deliberately cross-cutting and would stay shared even after a
  split. Revisit when several effects have their own controls.
- **Brightness or dimming.** The relays are zero-cross SSRs and strictly ON/OFF.
  If it ever became desirable, `SetCell(row, column, bool)` can stay as an
  overload so existing effects keep working.
- **Scene/preset saving, MIDI, DMX, network control.** None are requested.
