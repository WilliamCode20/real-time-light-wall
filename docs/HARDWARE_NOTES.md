# Hardware Notes

## Known Physical Wall Specs

Current known wall characteristics:

- 5 rows x 7 columns
- 35 total bulbs
- each bulb is individually addressable
- current operation target is ON/OFF only
- current development machine is Windows
- eventual app should react to system audio

## Controller

The existing wall uses:

- Arduino Mega 2560 R3

## Relay Information

Known relay/module markings observed on the hardware:

- SSR-25 DA
- 25A
- 24–380VAC
- 3–32VDC

This strongly suggests solid-state relay modules with:

- low-voltage DC control side
- higher-voltage AC load side

The project should continue treating the Arduino/computer side and the mains/load side as separate concerns.

## Existing Pin Mapping from Old Arduino Code

The old sketch is now in the repository at `docs/OLD_ARDUINO_CODE/`. It defines:

Row 0: 2, 3, 4, 5, 6, 7, 8
Row 1: 9, 10, 11, 12, 13, 22, 23
Row 2: 24, 25, 26, 27, 28, 29, 30
Row 3: 31, 32, 33, 34, 35, 36, 37
Row 4: 38, 39, 40, 41, 42, 43, 44

The old code also defines a flattened allLights[35] array in row-major order.

Convenient consequence: that ordering matches the desktop app's bit numbering
exactly. Bulb number N in a packet is `allLights[N]` in the firmware, with no
translation needed.

## Facts Confirmed from the Old Sketch

Read directly out of the working code, so these are established rather than
assumed:

- **Active HIGH.** The sketch defines `PIX_ON HIGH` / `PIX_OFF LOW`, so the SSR
  control is non-inverting. Worth knowing, since many relay boards are the
  opposite.
- **Serial was never used.** No `Serial.begin`, no serial calls anywhere. The
  port is entirely free for the new protocol.
- **No PWM.** No `analogWrite` anywhere; strictly digital ON/OFF throughout.
  Consistent with zero-cross solid-state relays, which cannot dim.
- **A0 is used as a noise source** for `randomSeed(analogRead(0))`. Worth
  remembering before repurposing that pin.
- **Structure is fully blocking.** Busy-wait loops throughout and a `while(true)`
  at the end of the show. New firmware will be non-blocking and serial-driven, so
  the old code is useful as a reference for the wall's capabilities, not as a
  starting point.

## Switching Speed — Evidence

The old show gives real measured evidence for how fast this hardware can be
driven, which is more useful than theory. At 130 BPM one beat is about 461 ms,
and the effects break that down as follows:

- fastest observed: **15 ms on, 15 ms off** (`chorus_lightningChaos`)
- common range: 20–40 ms per change
- typical effect frame time: 30–80 ms

The show runs correctly on the real installation, so ~15 ms dwell is proven
workable in practice.

Software conclusion: roughly **30 updates per second is a comfortable ceiling**
for the physical wall, and 60 is about at the proven edge. The simulator redraws
at 60 fps, but the serial layer should send at its own slower, rate-limited pace
rather than forwarding every drawn frame.

Theoretical backing: zero-cross SSRs can only change state at mains zero
crossings, which is 120 times a second on 60 Hz. A command shorter than one half
cycle (8.3 ms) may not fire at all, or may fire inconsistently across bulbs. The
measured 15 ms floor sits at about two half cycles, which fits.

## Existing Old Control Model

The old Arduino sketch proves that the wall already works as a functioning installation.

It includes:

- pin setup for all 35 outputs
- helper methods like:
- allOn()
- allOff()
- rowOn()
- colOn()
- many named visual effects
- beat/timeline-based sequencing for one hard-coded song

This confirms that:

- the wall is controllable
- the spatial layout is already understood
- reusable pattern logic is a good fit for this installation

## Known Wiring

- bulbs are LEDs
- Arduino pins run to a breadboard, which connects to the SSR inputs
- no known intermediate driver stage
- the SSR datasheet figure found online is roughly 7.5 mA per relay input, which
  across 35 relays would be about 260 mA
- the original installation runs all 35 outputs without trouble, which is the
  strongest evidence available that the current arrangement is fine

## Important Open Questions

Still not fully confirmed:

- exact bulb power characteristics
- exact per-relay input current on this specific hardware (the 7.5 mA figure is
  from a web search, not measured)
- whether any ghosting/leakage occurs with the current LED + SSR combination —
  LED driver capacitance can hold charge briefly after the relay opens
- behaviour under sustained rapid updates over long periods, which the old show
  never had to do since it ran for one song

## Current Software Assumption

For software planning purposes, the current working assumption is:

- the Arduino will eventually receive already-decided wall frames from the desktop app
- the desktop app will be the main "brain"
- the Arduino will mainly act as an output device
