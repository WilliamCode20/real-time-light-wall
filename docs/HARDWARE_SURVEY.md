# Hardware Survey

A checklist to fill in at the wall. Answer what you can, mark anything you are
unsure about rather than guessing — a confident wrong answer is worse than "not
sure", because it sends debugging in the wrong direction.

**Do this with the mains side switched off.** Nothing here needs the load side
live.

## Do NOT hand-trace the pin map

The obvious thing to document is which Arduino pin drives which bulb. Skip it.

Tracing 35 wires through a breadboard is slow and error-prone, and the errors
stay invisible until the wall misbehaves. The planned bulb-identification mode
will discover the true mapping in a few minutes, reliably, by lighting one bulb
at a time.

Spend the trip on the things software cannot discover instead.

---

## 1. Orientation (highest value)

Get this wrong and every animation runs backwards or upside down.

- [ ] Photo of the wall **from the viewer's side**, with the top-left bulb marked
      (sticky note, tape, anything)
- [ ] Is the Arduino mounted behind the wall? If so, left and right are mirrored
      from where you stand while testing.
- [ ] Is there a definite "top" — is the wall hung one way round, or could it be
      rotated?

Answer:

```
Top-left bulb from the front is ...
Arduino sits (behind / beside / in front of) the wall
```

## 2. Power for the SSR control side

The current-budget question. 35 relay inputs at roughly 7.5 mA each is about
260 mA, which is over an Arduino Mega's total I/O budget — but the installation
works, so something here explains why.

- [ ] Where does the SSR control voltage come from?
      Arduino 5V pin / VIN / USB only / separate DC supply / other
- [ ] If a separate supply: what does its label say (voltage, amps)?
- [ ] How is the Arduino itself powered? USB, barrel jack, or both?
- [ ] Photo of the power connections

Answer:

```
SSR control voltage source:
Arduino power source:
Separate supply rating (if any):
```

## 3. What sits between the Arduino pins and the SSRs

- [ ] Photo of the breadboard from **directly overhead**, in good light
- [ ] Are there any components on the board — resistors, transistors, chips,
      an opto-isolator board — or just wires?
- [ ] Does each pin go to exactly one SSR, or is anything shared?

Answer:

```
Components between pin and SSR:
One pin per SSR? (yes / no):
```

## 4. Grounds

If the Arduino and the SSR control side run from different supplies and their
grounds are not joined, the relays behave erratically in a way that looks
exactly like a software timing bug. Worth ruling out now.

- [ ] Is Arduino GND connected to the SSR control-side ground / negative?
- [ ] Photo of wherever the grounds meet

Answer:

```
Common ground present? (yes / no / not sure):
```

## 5. One channel traced end to end

Rather than all 35, draw **one** bulb's complete path. A hand sketch
photographed is perfectly fine — no need for any software.

```
Arduino pin __  ->  breadboard  ->  SSR input +
                                    SSR input -   -> goes to ______
                    SSR output -> bulb -> ______
```

- [ ] Sketch photographed
- [ ] Confirm the other 34 are wired the same way (yes / no / exceptions:)

## 6. The SSR modules

- [ ] Clear close-up photo of one module's label

This confirms whether they are zero-cross switching, which is what the 30 Hz
output ceiling in HARDWARE_NOTES.md is based on. If they turn out to be
random-fire instead, faster updates may be possible.

Already noted from earlier: SSR-25 DA, 25 A, 24–380 VAC, 3–32 VDC.

## 7. The bulbs

- [ ] Any marking on a bulb — wattage, brand, "dimmable" or not
- [ ] Do they visibly fade out when switched off, or cut instantly?

The fade question matters: LED bulbs with capacitors in their drivers hold charge
briefly after the relay opens, which is the likely explanation for the "ghosting"
question in HARDWARE_NOTES.md. Worth watching one bulb closely as it switches.

Answer:

```
Bulb markings:
Fades or cuts instantly:
```

## 8. Anything that looks wrong

- [ ] Loose terminals, scorching, exposed conductors, wires under strain

Flag these to whoever built the wall rather than working around them in
software.

---

## When you are back

Drop the photos into `docs/hardware-photos/` and fill in the answers above.
Roughly-labelled photos are fine — I can read them directly, and I would rather
have ten quick honest shots than one perfect diagram.
