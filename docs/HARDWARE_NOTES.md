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

The old hard-coded Arduino sketch defines:

Row 0: 2, 3, 4, 5, 6, 7, 8
Row 1: 9, 10, 11, 12, 13, 22, 23
Row 2: 24, 25, 26, 27, 28, 29, 30
Row 3: 31, 32, 33, 34, 35, 36, 37
Row 4: 38, 39, 40, 41, 42, 43, 44

The old code also defines a flattened allLights[35] array in row-major order.

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

## Important Open Questions

Still not fully confirmed:

- exact bulb power characteristics
- exact Arduino-to-relay wiring details
- whether there are any intermediate driver stages between Arduino and relay inputs
- real-world switching behavior under sustained rapid updates
- whether any ghosting/leakage occurs with the current LED + SSR combination

## Current Software Assumption

For software planning purposes, the current working assumption is:

- the Arduino will eventually receive already-decided wall frames from the desktop app
- the desktop app will be the main "brain"
- the Arduino will mainly act as an output device
