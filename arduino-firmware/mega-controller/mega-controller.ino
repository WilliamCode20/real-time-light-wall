// =============================================================================
//  Light Wall — Mega Controller Firmware
// =============================================================================
//
//  Receives 5x7 wall frames from the desktop app over USB serial and switches
//  the 35 solid-state relays accordingly.
//
//  WHAT THIS FIRMWARE IS
//
//  Deliberately dumb. It contains no animations, no timing, no show logic and
//  no idea what music is. It receives a picture, validates it, and puts it on
//  the wall. Everything clever happens on the computer.
//
//  That split matters: the desktop app can be rewritten, restarted or crash
//  without this needing to change, and this can be flashed once and forgotten.
//
//  WHERE THIS CAME FROM
//
//  This is a translation of VirtualWallReceiver.cs in the desktop project,
//  which was written specifically to be translated — byte at a time, one small
//  fixed buffer, no dynamic allocation, exactly the shape an Arduino needs.
//
//  That C# version has a test suite covering dropped bytes, corrupted bytes,
//  doubled sync bytes and payloads that happen to contain the sync pair. If you
//  change the logic below, change it there too and run those tests. Getting the
//  two out of step is the most likely way to introduce a bug that only shows up
//  as the wall occasionally misbehaving.
//
//  PACKET FORMAT (must match WallFrameSerializer.cs exactly)
//
//    Byte 0   0xAA        sync 1
//    Byte 1   0x55        sync 2
//    Byte 2   command     1 = frame, 2 = blackout, 3 = heartbeat
//    Byte 3-7 payload     35 bulbs, one bit each
//    Byte 8   checksum    XOR of bytes 2 through 7
//
//  Bulb N sits at bit N, counting row-major from the top-left, packed least
//  significant bit first. Bulb N is allLights[N] below, with no translation.
//
// =============================================================================

// ---------------------------------------------------------------------------
//  Wall geometry and pin map
// ---------------------------------------------------------------------------

const int ROWS = 5;
const int COLS = 7;
const int BULB_COUNT = ROWS * COLS;   // 35

// Which Arduino pin drives each bulb, in row-major order: the whole top row
// first, then the next row down.
//
// Taken from allLights[35] in the original hand-written sketch, which is the
// only authoritative source for this wiring. Note the jump from 13 to 22 —
// pins 14 to 21 are skipped.
//
// The relays in the enclosure are labelled A1 to E7. The letter is the row and
// the number is the column counting from 1, so allLights[0] is relay A1 and
// allLights[34] is relay E7.
const int allLights[BULB_COUNT] = {
//  col1 col2 col3 col4 col5 col6 col7
     2,   3,   4,   5,   6,   7,   8,   // row A
     9,  10,  11,  12,  13,  22,  23,   // row B
    24,  25,  26,  27,  28,  29,  30,   // row C
    31,  32,  33,  34,  35,  36,  37,   // row D
    38,  39,  40,  41,  42,  43,  44    // row E
};

// The relay boards are non-inverting: a HIGH pin turns its bulb on.
//
// Confirmed from the original sketch, which defined exactly this. Worth being
// explicit about, because plenty of relay boards are the opposite and getting
// it backwards produces a perfectly working wall showing a photographic
// negative of what was intended.
#define PIX_ON  HIGH
#define PIX_OFF LOW

// NOTE: pin 13 drives a bulb (row B, column 5). It is also the Mega's built-in
// LED. Do not use it for status blinking — you would be lighting part of the
// wall.

// ---------------------------------------------------------------------------
//  Protocol constants — these MUST match WallFrameSerializer.cs
// ---------------------------------------------------------------------------

const byte SYNC_1 = 0xAA;
const byte SYNC_2 = 0x55;

const byte CMD_FRAME     = 0x01;
const byte CMD_BLACKOUT  = 0x02;
const byte CMD_HEARTBEAT = 0x03;

const int PAYLOAD_LENGTH = 5;
const int BODY_LENGTH    = 7;   // command + 5 payload + checksum

// Must match SerialTransport.DefaultBaudRate on the desktop side.
const long BAUD_RATE = 115200;

// ---------------------------------------------------------------------------
//  Watchdog
// ---------------------------------------------------------------------------
//
//  If the app crashes, the laptop sleeps, or somebody trips over the USB cable,
//  packets simply stop arriving. Without a watchdog the wall would hold its last
//  frame indefinitely — some arbitrary half-lit pattern burning away with
//  nothing driving it.
//
//  For something switching mains voltage, what happens when contact is lost
//  should be a decision rather than an accident. Going dark is the safe choice.
//
//  The app sends about 30 packets a second, so a second of total silence means
//  something is genuinely wrong rather than merely slow.

const unsigned long WATCHDOG_TIMEOUT_MS = 1000;

// ---------------------------------------------------------------------------
//  Receiver state
// ---------------------------------------------------------------------------

// Where we are in the process of finding and reading a packet.
//
// This is a state machine: we are always in exactly one of these, and each
// incoming byte may move us to another. It is the standard way to pull a
// protocol out of a stream, and it is what the C# version does too.
enum ReceiveState {
  WAITING_FOR_SYNC_1,   // hunting for 0xAA; anything else is noise
  WAITING_FOR_SYNC_2,   // saw 0xAA; the very next byte must be 0x55
  COLLECTING_BODY       // both sync bytes seen; reading the 7 body bytes
};

ReceiveState state = WAITING_FOR_SYNC_1;

byte body[BODY_LENGTH];
int  bodyBytesReceived = 0;

unsigned long lastValidPacketMs = 0;
bool hasEverReceived = false;
bool watchdogTripped = false;

// ---------------------------------------------------------------------------
//  Setup
// ---------------------------------------------------------------------------

void setup() {
  for (int i = 0; i < BULB_COUNT; i++) {
    pinMode(allLights[i], OUTPUT);
    digitalWrite(allLights[i], PIX_OFF);
  }

  Serial.begin(BAUD_RATE);

  // Brief startup sign, so it is obvious at a glance that this firmware is
  // running and which bulb the app calls number 0.
  //
  // One bulb rather than the whole wall on purpose: all 35 at once draws close
  // to the microcontroller's total current limit, and there is no reason to go
  // anywhere near that just to say hello.
  digitalWrite(allLights[0], PIX_ON);
  delay(500);
  digitalWrite(allLights[0], PIX_OFF);
}

// ---------------------------------------------------------------------------
//  Main loop
// ---------------------------------------------------------------------------

void loop() {
  // Drain whatever has arrived. Serial data turns up whenever it likes and in
  // whatever sized chunks it likes, so this reads a byte at a time and lets the
  // state machine worry about where packets begin and end.
  while (Serial.available() > 0) {
    receiveByte((byte)Serial.read());
  }

  updateWatchdog();
}

// ---------------------------------------------------------------------------
//  The state machine — the heart of the whole thing
// ---------------------------------------------------------------------------

void receiveByte(byte value) {
  switch (state) {

    case WAITING_FOR_SYNC_1:
      if (value == SYNC_1) {
        state = WAITING_FOR_SYNC_2;
      }
      // Anything else is discarded. This is what recovery looks like: after a
      // disruption we simply throw bytes away until something that could be the
      // start of a packet turns up.
      break;

    case WAITING_FOR_SYNC_2:
      if (value == SYNC_2) {
        state = COLLECTING_BODY;
        bodyBytesReceived = 0;
      }
      else if (value == SYNC_1) {
        // Another 0xAA. STAY HERE rather than starting over.
        //
        // This is small and easy to get wrong, and the C# tests cover it
        // specifically. If a stray 0xAA lands just before a real packet the
        // stream reads "AA AA 55 ...". Dropping back to hunting would consume
        // the real packet's sync byte and lose the whole frame. Treating each
        // 0xAA as a possible fresh start keeps it.
        //
        // The symptom of getting this wrong is occasional dropped frames —
        // miserable to diagnose on hardware.
      }
      else {
        // The 0xAA was a coincidence, most likely a payload byte. Go back to
        // hunting.
        state = WAITING_FOR_SYNC_1;
      }
      break;

    case COLLECTING_BODY:
      body[bodyBytesReceived] = value;
      bodyBytesReceived++;

      if (bodyBytesReceived == BODY_LENGTH) {
        processPacket();
        state = WAITING_FOR_SYNC_1;
      }
      break;
  }
}

// ---------------------------------------------------------------------------
//  A complete body has arrived — check it, and act on it if sound
// ---------------------------------------------------------------------------

void processPacket() {
  // Body layout: [command][payload x 5][checksum]
  byte command  = body[0];
  byte checksum = body[BODY_LENGTH - 1];

  byte expected = command;
  for (int i = 0; i < PAYLOAD_LENGTH; i++) {
    expected ^= body[1 + i];
  }

  if (checksum != expected) {
    // Either bytes were corrupted in transit, or — more likely — we latched
    // onto a coincidental 0xAA 0x55 inside a payload and read rubbish.
    //
    // Either way, discard it and start hunting again. Never apply a frame that
    // failed its checksum: showing nothing for a thirtieth of a second is far
    // better than showing garbage, and another frame is along immediately.
    //
    // This is why the checksum still matters despite there being two sync
    // bytes. The sync pair makes a false start unlikely; the checksum catches
    // the ones that slip through anyway.
    return;
  }

  // Any valid packet counts as contact, so the watchdog is satisfied even by a
  // command that changes nothing on the wall.
  lastValidPacketMs = millis();
  hasEverReceived = true;
  watchdogTripped = false;

  switch (command) {

    case CMD_FRAME:
      applyPayload();
      break;

    case CMD_BLACKOUT:
      allOff();
      break;

    case CMD_HEARTBEAT:
      // Deliberately changes nothing. Its only job is to reset the watchdog
      // above, so a still frame holds on the wall during a stretch when the app
      // is not sending new pictures.
      break;

    default:
      // An unrecognised command from a newer version of the app. Ignore it
      // rather than treating it as an error, so that old firmware keeps working
      // when new commands are added and simply misses features it does not
      // understand.
      break;
  }
}

// ---------------------------------------------------------------------------
//  Unpack 5 bytes into 35 pins
// ---------------------------------------------------------------------------

void applyPayload() {
  for (int i = 0; i < BULB_COUNT; i++) {
    // Bulb i lives at bit i, counting from the least significant bit of the
    // first payload byte.
    //
    //   i / 8  picks the byte
    //   i % 8  picks the bit within it
    //
    // Getting this backwards produces a wall that looks scrambled in a way
    // that strongly resembles a wiring fault but is not.
    byte  payloadByte = body[1 + (i / 8)];
    bool  isOn        = (payloadByte & (1 << (i % 8))) != 0;

    digitalWrite(allLights[i], isOn ? PIX_ON : PIX_OFF);
  }
}

// ---------------------------------------------------------------------------
//  Helpers
// ---------------------------------------------------------------------------

void allOff() {
  for (int i = 0; i < BULB_COUNT; i++) {
    digitalWrite(allLights[i], PIX_OFF);
  }
}

void updateWatchdog() {
  if (watchdogTripped) {
    return;
  }

  // Nothing has ever arrived, so there is no silence to measure yet. Without
  // this check the wall would blank itself the instant it powered on, before
  // the app had any chance to say anything.
  if (!hasEverReceived) {
    return;
  }

  // Subtracting unsigned longs this way is deliberate: it stays correct even
  // when millis() wraps back to zero after about 49 days of running.
  if ((millis() - lastValidPacketMs) > WATCHDOG_TIMEOUT_MS) {
    watchdogTripped = true;
    allOff();
  }
}
