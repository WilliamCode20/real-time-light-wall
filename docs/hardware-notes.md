# Hardware Notes

## Current known specs
- Wall size: 5 rows x 7 columns = 35 bulbs
- Arduino: Mega 2560 R3
- Bulbs: LEDs
- Control style: on/off only
- Relay modules: SSR-25 DA solid state modules
- Platform target: Windows first
- Audio goal: system audio reactivity

## Old Arduino code observations
- Old sketch defines ROWS = 5 and COLS = 7
- Uses a 2D `lights[ROWS][COLS]` pin mapping
- Uses an `allLights[35]` flat array
- Includes helper functions like:
  - allOn()
  - allOff()
  - rowOn()
  - colOn()

## Old pin mapping from the existing sketch
Row 0: 2, 3, 4, 5, 6, 7, 8
Row 1: 9, 10, 11, 12, 13, 22, 23
Row 2: 24, 25, 26, 27, 28, 29, 30
Row 3: 31, 32, 33, 34, 35, 36, 37
Row 4: 38, 39, 40, 41, 42, 43, 44

## Questions still open
- Confirm exact bulb voltage / AC path
- Confirm exact Arduino-to-relay wiring
- Confirm relay input current requirements
- Confirm desired update rate