# Mega Controller Firmware

Receives 5x7 wall frames from the desktop app over USB serial and switches the
35 solid-state relays.

Deliberately dumb: no animations, no timing, no show logic. It receives a
picture, validates it, and puts it on the wall. Everything clever happens on the
computer.

`mega-controller.ino` is a translation of `VirtualWallReceiver.cs` in the desktop
project, which was written specifically to be translated — byte at a time, one
small fixed buffer, no dynamic allocation. **If you change the logic here, change
it there too and run the tests**, which cover dropped bytes, corrupted bytes,
doubled sync bytes, and payloads containing the sync pair.

## Before you flash: back up what is already on the board

Uploading this **erases whatever sketch is currently on the Arduino.** If the
wall has behaviour you have not got the source for — a startup sequence, a newer
version of the show than the copy in `docs/OLD_ARDUINO_CODE/` — take a backup
first.

### What you can and cannot recover

You can read back the **compiled machine code** as a `.hex` file. You cannot
recover the original C++ source: the compiler discarded the variable names,
comments and structure long ago, and there is no way to get them back.

That still solves the real problem. A `.hex` backup can be flashed straight back
onto the board and it will behave *exactly* as it does today, byte for byte.
Think of it as a photograph of the board's memory: perfect for restoring, useless
for reading.

### Doing the backup

`avrdude` is the tool, and it ships with the Arduino IDE. Find it at roughly one
of these, depending on which IDE version is installed:

```
IDE 2.x   C:\Users\<you>\AppData\Local\Arduino15\packages\arduino\tools\avrdude\<version>\bin\avrdude.exe
IDE 1.x   C:\Program Files (x86)\Arduino\hardware\tools\avr\bin\avrdude.exe
```

With the board plugged in and nothing else using the port — **close the IDE's
Serial Monitor** — read the flash:

```
avrdude -c wiring -p atmega2560 -P COM3 -b 115200 -U flash:r:wall-backup.hex:i
```

Replace `COM3` with the actual port. If avrdude complains it cannot find its
configuration, add `-C <path>\avrdude.conf`, which sits beside the executable or
one folder up in `etc`.

Worth also grabbing the EEPROM, in case the old sketch stored anything there:

```
avrdude -c wiring -p atmega2560 -P COM3 -b 115200 -U eeprom:r:wall-eeprom.hex:i
```

Keep both files somewhere outside this repository — they are binaries, and the
repo is for source.

### Restoring it later

```
avrdude -c wiring -p atmega2560 -P COM3 -b 115200 -U flash:w:wall-backup.hex:i
```

The board goes back to exactly what it was doing before.

### About the startup sequence

Most likely there is nothing extra to find. The old sketch's `loop()` runs its
timeline from beat 0 the moment the board powers up, so "the sequence it does
when switched on and left alone" is simply the show starting from its intro. The
source for that is already in `docs/OLD_ARDUINO_CODE/`.

But the board might be carrying a later revision than that file. Taking the
backup costs two minutes and settles the question either way.

## Uploading

1. Open `mega-controller.ino` in the Arduino IDE
2. Tools → Board → **Arduino Mega or Mega 2560**
3. Tools → Processor → **ATmega2560 (Mega 2560)**
4. Tools → Port → whichever COM port the board is on
5. Upload

## Checking it worked

On startup the firmware lights **the top-left bulb for half a second**, then goes
dark and waits for the app.

One bulb rather than a full flash on purpose: all 35 at once draws close to the
microcontroller's total current limit, and there is no reason to go near that
just to say hello. It also tells you immediately which bulb the app calls
number 0.

If that bulb does not light, the firmware is not running — check the upload
succeeded and that the board is powered.

## Protocol

Defined in `LightWall.Core/Serialization/WallFrameSerializer.cs`, which carries
the full specification. Summary:

| Byte | Meaning |
|---|---|
| 0 | `0xAA` sync 1 |
| 1 | `0x55` sync 2 |
| 2 | command: 1 = frame, 2 = blackout, 3 = heartbeat |
| 3–7 | payload, 35 bulbs at one bit each |
| 8 | checksum, XOR of bytes 2 to 7 |

115200 baud. Bulb N is at bit N, row-major from the top-left, packed least
significant bit first, and maps to `allLights[N]` with no translation.

Two things the firmware must keep doing:

- **On a second `0xAA` while waiting for `0x55`, stay put.** Restarting the hunt
  would eat the real sync byte of an `AA AA 55 ...` sequence and silently drop a
  frame.
- **Never apply a frame that failed its checksum.** A lone `0xAA` is an ordinary
  bulb pattern and turns up in payloads regularly, so a false packet start is
  always possible; the checksum is what catches it.

## Watchdog

If no valid packet arrives for **1 second**, the wall blanks itself.

The app sends about 30 packets a second, so a full second of silence means
something is genuinely wrong — a crashed app, a sleeping laptop, a pulled cable.
For something switching mains voltage, what happens when contact is lost should
be a decision rather than an accident.

## Pin note

**Pin 13 drives a bulb** (row B, column 5) as well as being the Mega's built-in
LED. Do not use it for status blinking; you would be lighting part of the wall.
