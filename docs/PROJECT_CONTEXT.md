# Project Context

## Project Name

Real-Time Music-Reactive Light Wall

## Project Goal

Build a Windows desktop application that can control a 5x7 light wall in real time.

The long-term goal is for the app to:

- capture audio playing on the computer
- analyze the music in real time
- generate visually interesting lighting behavior
- send wall-state updates to an Arduino Mega 2560
- drive a physical 35-bulb wall made of individually addressable lights

## Product Direction

This is not meant to be just a novelty equalizer. The desired direction is a controllable, studio-grade lighting instrument that can:

- react to any music source
- feel intentional and aesthetically pleasing
- be usable by non-programmers such as DJs, venue operators, or clients
- eventually be included with future light wall installations

The wall should feel more like an architectural lighting feature than a gimmick.

## Current Development Approach

The project was built in stages, and all of the planned ones are done.

- **Simulator** — wall-state modeling, a time-driven effect engine, live
  animation controls, a tested 9-byte packet format
- **Serial transport and firmware** — frames leave the app and drive the real
  wall; the mapping was verified against it on 2026-08-04
- **Audio** — WASAPI loopback capture, seven frequency bands, onset detection,
  tempo estimation and a metronome locked to it

There are now **25 effects**, ten of which react to the music.

The current stage is refinement rather than construction: tuning beat detection
against real music, and then DJ-facing scene control — which is the point at
which `EffectParameters` will need to stop being one shared object. See
`NEXT_STEPS.md`.

## Distribution

The finished app is meant to be sent to a DJ or venue operator as a single file
they download and run, with no installation steps. It publishes as a
self-contained single .exe with the .NET runtime bundled inside, so the recipient
does not need to install anything first.

The simulator is not temporary. It is intended to remain part of the tool as:

- a development surface
- a debugging tool
- a future preview mode

## Platform / Stack

The project is currently being developed as a Windows-first desktop app using:

- C#
- .NET
- WPF

This choice was made because the app needs:

- a real desktop UI
- structured code
- access to Windows system audio
- serial communication with Arduino hardware

The last two were the reason for choosing Windows-first, and were still ahead
when the decision was taken. Both have since been built — WASAPI loopback capture
and a serial transport driving the real wall — so the choice can be judged rather
than merely justified, and it held up.

## Long-Term Architecture

The architecture below was the plan, and is now what the system does:

Computer app:

- captures audio
- analyzes audio
- chooses / shapes animations
- produces wall frames
- sends frame packets over serial

Arduino:

- receives packets
- validates packets
- unpacks 35-bit wall state
- sets output pins accordingly

## Guiding Principles

- Keep the simulator as a permanent development tool
- Keep wall-state truth separate from UI rendering
- Prefer reusable pattern/animation logic over one-off effects
- Build safely in layers rather than jumping straight to full audio reactivity
- Keep hardware output and visual simulation conceptually aligned
