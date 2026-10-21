# PHYS-XR sensing station firmware

ESP32 + VL53L0X (ToF) + HC-SR04 (ultrasonic) + MPU6050, sending the semantic
**PhysicalState contract v1** to Unity over **USB serial at 115200 baud**.

| File | What it is |
|---|---|
| `physxr_station/physxr_station.ino` | The sketch: drivers, cooperative scheduler, serial output |
| `physxr_station/physxr_semantics.h` | Portable semantic core (proximity classes, fusion, disturbance detector, line format); no Arduino dependency |
| `tests/semantics_test.cpp` | Host test of the semantic core (runs on a PC, no ESP32) |

Status: **written and compile-checked, not yet run on the physical station.**
See "What has and has not been verified" at the end.

## 1. Pins and bus

| Signal | ESP32 | Evidence | Status |
|---|---|---|---|
| HC-SR04 TRIG | GPIO 25 | Combined progress report (Sep 2026): working HC-SR04 bench prototype on TRIG 25 / ECHO 26; your instructions | Used |
| HC-SR04 ECHO | GPIO 26 | Same | Used. **ECHO is a 5 V signal on an HC-SR04: it must go through a divider (e.g. 10 kΩ / 20 kΩ) or level shifter before GPIO 26.** Not verified on the build. |
| I²C SDA | GPIO 21 | Your instructions; schematic poster; I²C scanner sketch (`OneDrive/Desktop/PHYS-XR/Demo.cpp`) | Used |
| I²C SCL | **GPIO 23 or GPIO 22 — unresolved** | Your latest code: 23. Schematic poster and the I²C scanner sketch: 22. | The firmware tries 23, then 22, keeps the one where a device answers, and reports it in `#HELLO ... scl=NN`. Record the reported value and update this table. |
| MPU6050 | I²C 0x68 (0x69 if AD0 high) | MPU6050 datasheet; poster | Probed at both |
| VL53L0X | I²C 0x29 | VL53L0X default address; poster | Probed |
| I²C clock | 100 kHz | Hardware Spec v1 §4.3 (bring-up speed) | Used |

The schematic poster (25 May 2025) also shows TRIG = GPIO 5 and ECHO = GPIO 18. That conflicts with the
working bench prototype (25/26); this firmware follows the bench prototype and your instructions.
If the station is actually wired 5/18, change `PIN_TRIG`/`PIN_ECHO` to match the wiring — do not re-wire to
match the code.

Probing SCL 23 and 22 is safe on this station because nothing else is connected to either pin (per the
poster and the bench prototype). If something else is ever wired to GPIO 22 or 23, remove that pin from
`SCL_CANDIDATES`.

## 2. Build and flash

1. Arduino IDE 2.x with the **esp32 by Espressif** core (3.x; compile-checked against 3.3.11).
2. Library Manager: install **"VL53L0X" by Pololu** (the sketch uses its `setBus`, `startContinuous`,
   `readReg`, `readRangeContinuousMillimeters`, `timeoutOccurred`, `last_status`).
3. Open `Firmware/physxr_station/physxr_station.ino`, board **ESP32 Dev Module**, the station's COM port.
4. Upload. **Close the Serial Monitor before starting Unity** — only one program can hold the COM port.

Command line equivalent (arduino-cli ships with the IDE):

```bash
arduino-cli lib install "VL53L0X"
```

```bash
arduino-cli compile --fqbn esp32:esp32:esp32 Firmware/physxr_station
```

```bash
arduino-cli upload --fqbn esp32:esp32:esp32 -p COM5 Firmware/physxr_station
```

## 3. Serial protocol (what Unity receives)

UTF-8 text, one message per line, `\n` terminated, at most 512 bytes. 115200 8N1, no flow control.

**State line** (contract v1, Technical Spec section E; validated by Unity's `PacketValidator`):

```
v=1;dev=phys-xr-01;boot=2864130047;seq=1234;ts=987654;presence=NEAR;confidence=HIGH;disturbance=false;eventType=;eventId=0;tEvent=0
v=1;dev=phys-xr-01;boot=2864130047;seq=1235;ts=987702;presence=VERY_NEAR;confidence=LOW;disturbance=true;eventType=DISTURBANCE;eventId=17;tEvent=987690
```

| Key | Meaning |
|---|---|
| `v` | Protocol version, always `1`. Unity's serial link **requires** it. |
| `dev` | Device ID `phys-xr-01` (Unity rejects other IDs as `FOREIGN_DEVICE`). |
| `boot` | Random non-zero 32-bit ID per power-up (`esp_random()`); a change tells Unity the ESP32 reset. |
| `seq` | +1 per state line, restarting at 1 after a reset. |
| `ts` | `millis()` since boot (never 0). Used for ordering and latency only, never compared with Unity's clock. |
| `presence` | `NONE` / `NEAR` / `VERY_NEAR`, fused from ToF + ultrasonic. |
| `confidence` | `HIGH` when both distance sensors are valid and agree, else `LOW`. |
| `disturbance` | `true` on the lines that carry a disturbance event. |
| `eventType`, `eventId`, `tEvent` | A qualified disturbance: ID +1 per event (from 1 each boot), `tEvent` = `millis()` when it qualified. Repeated on 3 consecutive lines; Unity accepts it once. |

Sent every 100 ms (10 Hz) and immediately when presence, confidence or an event changes.
No raw distance or acceleration appears in a state line.

**Diagnostic lines** start with `#`. Unity shows them in the menu and the F1 overlay and logs them to
telemetry, but **never** feeds them into PhysicalState:

```
#HELLO proto=1;fw=1.0.0;dev=phys-xr-01;boot=..;baud=115200;sda=21;scl=23;trig=25;echo=26;mpu=0x68;tof=0x29
#DIAG ts=..;tof_ok=1;tof_far=0;tof_cm=41.9;tof_age=12;tof_cls=NEAR;us_ok=1;us_cm=43.0;us_age=40;us_cls=NEAR;imu_ok=1;imu_g=1.002;imu_d=0.0040;imu_on=0.0800;imu_cal=1;evt=3;seq=812;loop_us=950
#CAL state=start;msg=keep_station_still   /   #CAL state=done;base=1.0021;noise=0.0031;on=0.0800;off=0.0400;rejects=0
#ERR sensor=tof;msg=read_failed
```

`#HELLO` at boot and every 5 s; `#DIAG` 4 times a second. `imu_cal`: 0 calibrating, 1 armed, 2 latched, 3 fault.

## 4. Sensor processing

**Ultrasonic (HC-SR04, secondary proximity evidence).** 10 µs trigger every 66 ms (15 Hz); echo timed by a
pin-change interrupt so the loop never waits for it. `cm = echo_us × 0.0343 / 2`. Valid range 2–400 cm
(datasheet). No echo within 25 ms (~4.3 m) = invalid sample (Hardware Spec §5). Median of the last 3 valid
samples, then per-sensor class with hysteresis. Invalid if no valid sample for 300 ms or 10 consecutive failures.

**ToF (VL53L0X, primary proximity).** Continuous ranging every 40 ms, read only when the data-ready flag is
set. Device range status 11 = valid distance. A reading of ≥ 8190 mm, or a valid one beyond 1200 mm, means
"nothing in range" and is a valid NONE (not an invalid sample). Any other status, an I²C error or a timeout
is invalid. Median of 3, class with hysteresis. Invalid after 250 ms without a valid sample; re-initialised
every 2 s after 10 consecutive failures.

**Fusion (Hardware Spec §7.1 table).** Both valid and equal → that class, HIGH. Both valid, different →
the less aggressive class, LOW. One valid → its class, LOW. Neither → NONE, LOW. Raw distances are never
averaged. An invalid or stale sensor can never produce presence.

**MPU6050 (local disturbance).** Accelerometer only (±2 g, DLPF 44 Hz, 100 Hz), gyro unused (Hardware Spec
§6). On power-up the station must stay still for 2 s: the stationary |A| baseline and noise are measured;
if the noise exceeds 0.03 g the station moved and calibration repeats; a baseline outside 0.7–1.3 g is a
sensor fault (no events). Then `D = | |A| − baseline |`, filtered by a 3-sample median (so a one-sample
spike is removed rather than smeared), and an event fires when D stays above ON for 3 consecutive samples
(30 ms). ON = max(0.08 g, 6 × baseline noise); OFF = ON / 2. After an event the detector stays latched
until D < OFF **and** 1.5 s have passed. Using |A| makes the result independent of how the board is
mounted, so axis mapping does not matter. A sensor failure marks the IMU unhealthy and never produces an event.

## 5. Calibration (required before the thresholds are trusted)

All thresholds above are **uncalibrated starting values** (Hardware Spec §§5, 6.2, 20 leave them to
calibration on the mounted station). They are constants at the top of `physxr_station.ino`
(`PROXIMITY`, `DISTURBANCE`). Procedure:

1. Mount the station as it will be used. Run the monitor (no Unity needed):
   `dotnet run -c Release --project Tools/TestHarness -- --serial-monitor AUTO 120`
2. Proximity: stand/hold a hand at the distances you want to mean NEAR and VERY_NEAR; note `tof_cm` and
   `us_cm` in the `ToF`/`sonar` lines. Set the enter thresholds there and the exit thresholds 10–15 % further.
   Check the empty zone reads `NONE/HIGH` for a minute (no false NEAR).
3. Disturbance: after `#CAL state=done`, leave the station untouched for 5 minutes — expect **zero** `EVENT`
   lines. Then perform the intended movement 20 times — expect 20 events, one each. Raise
   `onFloorG` if untouched events occur; lower it (or `qualifySamples`) if real movements are missed.
4. Record the final values, the date, and the mounting geometry in the table below and in your report.

| Parameter | Starting value | Calibrated value | Date / geometry |
|---|---|---|---|
| VERY_NEAR enter / exit | 30 / 35 cm | | |
| NEAR enter / exit | 80 / 90 cm | | |
| Disturbance ON floor | 0.08 g (or 6 × noise) | | |
| Qualify samples | 3 (30 ms) | | |
| Re-arm interval | 1.5 s | | |

## 6. Troubleshooting

| Symptom (Unity menu / monitor) | Likely cause |
|---|---|
| `FAULTED ... in use by another program` | Arduino Serial Monitor (or another Unity/monitor) has the port. Close it. |
| `FAULTED COMx not found` / `no USB COM ports, only Bluetooth` | ESP32 not plugged in, charge-only cable, or USB driver (CP210x / CH340) missing. |
| `CONNECTED (no PHYS-XR data yet)` for > 3 s | Wrong baud, or a different sketch is flashed. |
| `CONNECTED (old raw-distance sketch...)` | The board still runs the old `Distance = ...` sketch. Flash `physxr_station`. |
| `#ERR sensor=i2c;msg=no_device_...` | SDA/SCL wiring, power or ground to the MPU6050/VL53L0X. |
| `#ERR sensor=tof/imu ...` | That module is unpowered, miswired or failed; the other sensors keep working. |
| `imu_cal=0` forever | The station is moving (or vibrating) during calibration. |
| `imu_cal=3` | Baseline outside 0.7–1.3 g: wrong range setting or a faulty IMU. |
| Ultrasonic always INVALID | ECHO not reaching GPIO 26 (divider wiring), or no target within 4 m. |
| Status STALE/UNAVAILABLE in game while CONNECTED | Lines are arriving but being rejected: see the `reject` line in the F1 overlay. |

## 7. What has and has not been verified

| Check | Result |
|---|---|
| Semantic core host test (`g++ ... semantics_test.cpp`) | **47/47 checks pass** |
| `arduino-cli compile --fqbn esp32:esp32:esp32` (core 3.3.11) | **Compiles, 0 warnings** (22 % flash, 7 % RAM) — against a compile-only stub of the Pololu VL53L0X header, because the library was not installed on the build PC. Compile again with the real library before flashing. |
| Golden state lines accepted by Unity's validator | **Pass** (`SerialTransportTests` SER-04) |
| Flashed to an ESP32, sensors read, live link to Unity | **Not done** — no ESP32 was connected while this was written. Use `Docs/HARDWARE_SERIAL.md` section 7. |
| ECHO divider, SCL pin, module addresses, thresholds | **Not verified** on the physical station |
