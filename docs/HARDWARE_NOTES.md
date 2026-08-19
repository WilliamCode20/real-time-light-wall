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

## Relay Labels — the mapping is documented on the hardware

Every SSR in the enclosure carries a sticker: **A1–A7, B1–B7, C1–C7, D1–D7,
E1–E7**. Thirty-five of them, which is the whole wall.

The letter is the row and the number is the column counting from 1. That matches
the original sketch's own convention exactly:

- `rowAEOff()` touches `lights[0]` and `lights[4]` → A = 0, E = 4
- `rowBDOff()` touches `lights[1]` and `lights[3]` → B = 1, D = 3
- `rowCOff()` touches `lights[2]` → C = 2
- `col4On()` touches `lights[r][3]` → column 4 = index 3

So the builder and the sketch author agreed with each other, which is much
stronger footing than either source alone. Encoded in
`LightWall.Core/Models/WallHardwareMap.cs`.

Examples: A1 = bulb 0 = pin 2. C4 = bulb 17 = pin 27. E7 = bulb 34 = pin 44.

**Still unverified:** none of this proves relay A1 physically switches the
top-left bulb. It proves the labelling is self-consistent, not that the wiring
matches it. Only the Identify Bulb mode settles that.

Note that the relays are **not** arranged in label order in the enclosure — the
middle rail mixes C6, D2 and D3, and the bottom rail runs A6 to A1 right to left.
Physical position in the box means nothing; only the stickers do.

## Control Circuit — measured

Confirmed from photographs and the owner's survey:

```
Arduino digital pin → 120 Ω resistor → SSR terminal 3 (+)
SSR terminal 4 (−)  → common rail → single wire → Arduino GND
```

- **35 resistors, 120 Ω each**, one per channel, on the breadboard
- **No driver stage.** The relays are switched directly by the digital pins.
- **Nothing connected to 5V or 3.3V.** The pins themselves are the supply.
- **One shared ground wire** back to a single Arduino GND pin
- Arduino powered from the **barrel jack**; the **USB port is unused and free**

### Current draw — needs one confirmation

Measured roughly **6 mA per channel** when energised. That is consistent with the
120 Ω resistor:

```
5 V − (6 mA × 120 Ω) = 4.28 V across the relay input
```

which is a sensible drop for an opto-input SSR.

**35 × 6 mA = 210 mA with the whole wall lit.** The ATmega2560's absolute maximum
for total current through its VCC/GND pins is **200 mA**, so an all-on frame sits
fractionally over the chip's stress rating.

This is not new and nothing has failed — the original show included full-wall
flashes and ran fine. Absolute maximum is a stress rating rather than a cliff,
and brief excursions are evidently survivable.

What *is* new is duration. The old show ran one song, roughly four minutes, with
all-on lasting a beat at a time. This app could hold `Fill` for hours during a
set, in a hot enclosure. Worth avoiding effects that park all 35 on for long
stretches.

**To confirm the 6 mA figure:** measure DC **volts** across one 120 Ω resistor
with that bulb lit, then divide by 120. About 0.72 V confirms 6 mA. This is a
parallel measurement, so nothing needs disconnecting — unlike a current reading,
which requires breaking the circuit and putting the meter in series.

If this ever needs fixing on a future installation, the standard answer is a
driver stage — five ULN2803A chips at eight channels each, after which the
Arduino sources almost nothing.

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

## How the Software Actually Uses This

This section used to describe an assumption. It is now how the system works, and
it was built exactly this way:

- the Arduino receives already-decided wall frames from the desktop app
- the desktop app is the "brain" — capture, analysis, effects, frame generation
- the Arduino is purely an output device: receive, validate, set pins

The firmware in `arduino-firmware/mega-controller/` contains no animation, timing
or show logic at all. That split is what lets the app be rewritten, restarted or
crashed without the board needing reflashing.
