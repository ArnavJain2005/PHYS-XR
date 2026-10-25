# PHYS-XR live hardware acceptance checklist (ESP32 USB-serial station)

The ordered procedure the project team runs with the physical station (ESP32 + VL53L0X + HC-SR04 +
MPU6050) connected over USB. Every step in this file is **physical**: none of it has been run. Automated
software evidence is in [`HARDWARE_SERIAL.md`](HARDWARE_SERIAL.md) §6 and
[`TechnicalSpec_MilestoneStatus.md`](TechnicalSpec_MilestoneStatus.md). Background on pins, protocol and
calibration: [`Firmware/README.md`](../Firmware/README.md).

**How to record.** Fill in the Result column with PASS / FAIL and a short observation (a value, a
screenshot name). Steps marked **STOP** must pass before you continue: fix the cause, then repeat the step.
Note the date, the tester, the build commit and the COM port at the top of your copy.

| Run | Date | Tester(s) | Build commit | COM port | Firmware `fw=` / `boot=` |
|---|---|---|---|---|---|
| | | | | | |

## Overview: bring-up order

None of these steps has been performed yet. Each must pass before the next; steps marked **STOP** in the checklist block everything after them.

| # | Step | Checklist | Evidence to record |
|---|---|---|---|
| 1 | Verify wiring and electrical safety with USB **unplugged**: ECHO divider, SCL pin, SDA, common ground, 5 V to the HC-SR04 | A1–A7 | Meter readings, photos |
| 2 | Flash `physxr_station` | B1–B3 | Compile and upload output |
| 3 | Confirm the handshake: `#HELLO … dev=phys-xr-01 … scl=NN`, IMU calibration done | C1–C5 | Serial log; GPIO 26 ≤ 3.3 V measured |
| 4 | Validate each sensor on its own, without Unity | D1–D8 | Harness monitor log |
| 5 | Calibrate proximity and disturbance thresholds | D9, [`Firmware/README.md`](../Firmware/README.md) §5 | Calibrated values table |
| 6 | Confirm Unity accepts live lines (`FRESH`, `rejected 0`) | F1–F3, E1–E4 | HUD/F1 screenshot, telemetry |
| 7 | Verify observable ghost reactions (R3, R4, R5, R1; never ATTACK) | F4–F17 | Video + `DECISION … cause_evt` |
| 8 | Full run, soak, analyzer summary, sign-off | F18–F21, G | `.jsonl`, analyzer CSV |

> **Status (26 Oct 2026).** The project team has accepted the physical hardware system: the VL53L0X time-of-flight and MPU6050 sensors are calibrated and functional, and the full physical integration was demonstrated on Meta Quest 2 and Meta Quest 3 (team-confirmed). The station runs `physxr_station` firmware 1.0.0 (`#HELLO proto=1;fw=1.0.0;dev=phys-xr-01`, I²C on SCL 23), and 15 live runs from 22-24 Oct are logged in [`Evidence/`](../Evidence/README.md). The text below is kept as the record of the 21 Oct bring-up.

**Known before starting (21 Oct 2026).** The station's board enumerates as **COM6** (Silicon Labs CP210x) on
the development PC; COM3/5/7/8 there are Bluetooth. It still runs an old bench sketch, which printed
`LASER Distance: 42–52 mm`, `MPU Accel Z ≈ 1.03 g` and **`SONAR No echo` on every cycle**. User-reported
wiring: TRIG 25, ECHO 26 with a divider "reported present", SCL 23, ToF "VL53L0X V2"; SDA 21 and common ground
not confirmed.

**Desk review before acceptance (21 Oct 2026; no hardware touched, nothing below is a physical result).**
- *Software re-check on `d583f02`:* harness 24 suites / 314 passed (serial suite 29/29), firmware host test
  47/47, and `arduino-cli compile --warnings all` against the real Pololu VL53L0X 1.3.1 with 0 warnings
  (305,548 B flash, 24,628 B RAM, `VL53L0X.cpp` linked).
- *`physxr_station` ultrasonic code:* reviewed, no fault found. TRIG 25 is an output held LOW, ECHO 26 an
  input with a CHANGE interrupt, 10 µs trigger every 66 ms, 25 ms echo timeout, 2–400 cm accepted. A missing
  echo marks the sample invalid; nothing is substituted.
- *The `SONAR No echo` source is unknown.* The sketch now on the board (it prints `LASER`/`SONAR`/`MPU`) was
  not found on this PC, so its TRIG/ECHO pins cannot be checked, and its `No echo` does **not** by itself
  show a wiring fault on 25/26. The only bench sketch found, the I²C scanner
  (`OneDrive/Desktop/PHYS-XR/Demo.cpp/Demo.cpp.ino`), uses `Wire.begin(21, 22)`: SDA 21, **SCL 22**.
- *Likely physical causes, in order (to be measured, A1–A6, C4):* HC-SR04 not on 5 V; missing common
  ground; TRIG/ECHO swapped or on other pins; divider reversed (a 10 kΩ/20 kΩ divider wired the wrong way
  round gives ≈ 1.7 V, below the ESP32's ≈ 2.5 V input-high threshold, so the echo is never seen); an
  obstruction a few cm in front of the sensors (the ToF read 42–52 mm at the same time, inside the
  HC-SR04's practical blind zone).

**PC set-up.** Close the Unity Editor and any large programs during player runs: earlier self-test early
exits coincided with the PC running out of commit memory. Close the Arduino Serial Monitor before starting
the game or the harness monitor: only one program can hold the COM port.

## A. Physical checks before power (cannot be done in software)

| # | Do | Expect | Result |
|---|---|---|---|
| A1 **STOP** | **USB unplugged.** Measure the HC-SR04 ECHO path with a multimeter (resistance mode). ECHO is a **5 V** output; it must reach GPIO 26 through a divider (e.g. 10 kΩ from the HC-SR04 ECHO pin to GPIO 26, 20 kΩ from GPIO 26 to GND) or a level shifter. Photograph it. | ECHO pin → GPIO 26 ≈ **10 kΩ** (the top resistor, never ≈ 0 Ω); GPIO 26 → GND ≈ **20 kΩ**. In-circuit readings can come out lower because of parallel paths through the modules; if unsure, read the resistors' colour codes or lift one leg. ≈ 0 Ω from ECHO to GPIO 26 means **no protection: stop**. Values swapped (20 k on top, 10 k to GND) means the divider is reversed. Later, powered (C4): GPIO 26 ≤ **3.3 V**, about 3.3 V while echoing with 10 k/20 k. | |
| A2 **STOP** | **USB unplugged.** Continuity test from the SCL pin of the VL53L0X breakout and of the MPU6050 breakout to the ESP32 header. **Unresolved: GPIO 22 or 23** (latest code says 23; schematic poster and the I²C scanner sketch say 22). | Both modules' SCL beep to the **same** ESP32 pin, and only to it (no continuity to the other candidate). Record the pin: ____. The firmware tries 23 then 22; C2 must report the same. | |
| A3 | **USB unplugged.** Continuity: SDA of both I²C modules → ESP32 GPIO 21; GND of ESP32, VL53L0X, HC-SR04 and MPU6050 all connected; no continuity between SDA and SCL or between either and GND/3V3/5V. | SDA → GPIO 21 on both modules. **Common ground** across all four boards. No shorts. | |
| A4 | Check supplies (continuity unplugged, then voltage once A1–A3 pass and USB is connected). | HC-SR04 VCC wired to the ESP32's **5V/VIN** pin and reading ≈ 4.7–5.1 V on USB (it does not range on 3.3 V). VL53L0X and MPU6050 breakouts on VIN or 3V3 as their boards require. | |
| A5 | Check TRIG/ECHO. | TRIG → GPIO 25, ECHO → GPIO 26, not swapped. (The poster shows 5/18; if the station is really wired 5/18, change `PIN_TRIG`/`PIN_ECHO` in the sketch, do not re-wire.) | |
| A6 | Clear the ToF's field of view. | Nothing within ~1.2 m in front of the VL53L0X (the old sketch read 42–52 mm, i.e. something ~5 cm away). Note the mounting geometry. | |
| A7 | Photograph the whole wiring and mounting. | Photos attached to the report. | |

## B. Flash the firmware

| # | Do | Expect | Result |
|---|---|---|---|
| B1 | Install the **Pololu "VL53L0X"** library in the Arduino IDE (Library Manager). This is a download; it is not installed in the IDE sketchbook yet. | Version 1.3.1 installed (the version the firmware was compiled against). | |
| B2 | Compile `Firmware/physxr_station` for **ESP32 Dev Module** (or `arduino-cli compile --fqbn esp32:esp32:esp32 Firmware/physxr_station`). | 0 errors, 0 warnings; about 305 KB flash, 24.6 KB RAM. | |
| B3 | Upload to the station's port (`arduino-cli upload --fqbn esp32:esp32:esp32 -p COM6 Firmware/physxr_station`, or the IDE's Upload). | Upload succeeds. | |

## C. Boot and identity (Arduino Serial Monitor, 115200 baud)

Keep the station **still** for 2 s after every reset (IMU calibration).

| # | Do | Expect | Result |
|---|---|---|---|
| C1 **STOP** | Press the ESP32 reset button. | `#HELLO proto=1;fw=1.0.0;dev=phys-xr-01;boot=...;baud=115200;sda=21;scl=NN;trig=25;echo=26;mpu=0x68;tof=0x29`. No `#ERR`. | |
| C2 **STOP** | Read `scl=` in `#HELLO`. | Matches the wire traced in A2. `#ERR sensor=i2c;msg=no_device_...` = SDA/SCL/power/ground fault. | |
| C3 | Watch `#CAL`. | `#CAL state=done;...;noise=<0.03` within ~2 s; `#DIAG ... imu_cal=1` (armed). | |
| C4 **STOP** | With a target 30–100 cm in front, measure GPIO 26 with a meter or scope. | Never above 3.3 V. `#DIAG` shows `us_ok=1` and a plausible `us_cm`. If `us_ok=0`: see Firmware/README §6 (5 V supply, TRIG/ECHO swapped, divider orientation, ground). | |
| C5 | Close the Serial Monitor. | Port free for the next step. | |

## D. Each sensor, without Unity

Run `dotnet run -c Release --project Tools/TestHarness -- --serial-monitor COM6 300` (use the station's
port). It prints `LINK`, `STA` (`#HELLO`, `#CAL`, `#ERR`), `STATE presence/confidence`, `EVENT n ACCEPTED/
DUPLICATE`, `BOOT new boot`, and a summary every second with `ToF`, `sonar` and `IMU` readings. `#DIAG` lines
are summarised, not printed. Add `--raw` to see unrecognised lines.

| # | Sensor | Do | Expect | Result |
|---|---|---|---|---|
| D1 | Link | Start the monitor. | `LINK CONNECTED COM6`, then `(station)` in the summary; `#HELLO` printed; `rejected 0`, `lost 0`. | |
| D2 | ToF | Empty zone for 60 s. | `ToF` reads "nothing in range" or a far distance; `STATE NONE/HIGH` steady, no false NEAR. | |
| D3 | ToF | Move a hand from 120 cm to 10 cm and back, slowly. | `ToF` distance follows; class NONE → NEAR (≤ 80 cm) → VERY_NEAR (≤ 30 cm) → back, with no flicker at the boundaries. Record the distances. | |
| D4 | Ultrasonic | Repeat D3. | `sonar` distance follows the hand within a few cm of the ToF; valid throughout. | |
| D5 | Fusion | Repeat D3 with both sensors seeing the hand. | `STATE NEAR/HIGH` and `VERY_NEAR/HIGH` (HIGH = both agree). | |
| D6 | Fusion | Cover only the ToF, then only the ultrasonic, with the hand at NEAR. | Confidence drops to LOW; presence never goes higher than the uncovered sensor's class. | |
| D7 | IMU | Leave the station untouched for 5 minutes. | **No** `EVENT` lines; IMU `|A|` ≈ 1.00 g and steady; stage ARMED. | |
| D8 | IMU | Tap/shake the station 20 times, at least 2 s apart. | Exactly **20** `EVENT n ACCEPTED`, each followed by 2 `DUPLICATE` (the event repeats on 3 lines). | |
| D9 | Calibration | If D2–D8 need it, adjust the thresholds (Firmware/README §5), re-flash, repeat D2–D8. | Values recorded in Firmware/README §5's table. | |

## E. USB link robustness (monitor still running)

| # | Do | Expect | Result |
|---|---|---|---|
| E1 | Press the ESP32 reset button. | `BOOT new boot <id>`; state lines resume within ~3 s; the next tap is accepted after resync (two in-order lines). | |
| E2 | Unplug the USB cable for 10 s, then replug. | `LINK FAULTED/DISCONNECTED ...` then `LINK CONNECTED` again, without restarting the monitor; `reconnects` +1. | |
| E3 | Open the Arduino Serial Monitor on the port while the harness monitor holds it. | The IDE cannot open the port (in use). Close the IDE monitor. | |
| E4 | Quit the harness monitor. | `Port released.`; the Arduino Serial Monitor can open the port immediately. | |

## F. In the game (Windows player, Editor closed)

Use the newest player build. In the menu, **Sensors → `Physical - USB serial`**, Serial port `COM6` (or
`AUTO`). Press **F1** for the developer overlay: its `decision` line shows the action, the rule and the
reason. Telemetry is in `%USERPROFILE%\AppData\LocalLow\DefaultCompany\PHYS_XR\telemetry`.

**Connection and display**

| # | Do | Expect | Result |
|---|---|---|---|
| F1 | Select `Physical - USB serial`, port `COM6`. | Menu panel: `USB COM6 CONNECTED`, the `#HELLO` identity and pins, live ToF/sonar/IMU readings that change with your hand. | |
| F2 | Cycle the port to a wrong one (e.g. a Bluetooth port), then back to COM6. | Wrong port: `FAULTED` or `CONNECTED (no PHYS-XR data yet)` with a reason, never `FRESH`. Back on COM6: recovers. | |
| F3 | Begin. | HUD chip `SENSORS  USB COM6 CONNECTED  -  FRESH`; F1 overlay `accepted` rising, `rejected 0`. | |

**Sensor-to-ghost responses** (before the final encounter unless stated; the ghost must be manifested,
e.g. after the first sighting)

| # | Physical action | Expect (F1 overlay `decision` line) | Telemetry | Result |
|---|---|---|---|---|
| F4 | Person/hand at NEAR, both sensors agree, then tap the station once. | **INVESTIGATE** `[INNER]`, reason "INVESTIGATE: qualified physical disturbance detected" (R3); The Hollow goes to the anchor nearest the player and lingers. | `PHYS_EVENT result=ACCEPTED`, `DECISION ... cause_evt=<id>` | |
| F5 | Hand at NEAR with one sensor covered. | **WARN**, reason "WARN: physical condition detected with low confidence" (R4); The Hollow stops and faces the player; a light flickers. | `PKT_RX confidence=LOW` | |
| F6 | Nobody in range; tap the station. | **WARN**, reason "WARN: disturbance detected without proximity confirmation" (R5). | `PHYS_EVENT ACCEPTED` | |
| F7 | Player inside the ghost's threat zone, a person VERY_NEAR the station (both sensors agree). | **PROTECT** `[S]`, reason "PROTECT: player at risk + high-confidence VERY_NEAR physical presence" (R1); lights steady. | `PKT_RX presence=VERY_NEAR` | |
| F8 | During the final encounter, keep a person VERY_NEAR and tap repeatedly. | Physical input never produces **ATTACK**; attacks still come only from the encounter (telegraphed WARN, then wind-up). PROTECT may prevent an attack. | No `DECISION action=ATTACK` with a physical cause | |

**F1 scenario: pause, disturbance, resume** (fix `ec4af1e`, see [`F1_PauseEventFix.md`](F1_PauseEventFix.md))

| # | Do | Expect | Result |
|---|---|---|---|
| F9 | In play, ghost manifested, hand at NEAR (both sensors). Press **Esc** to pause. | Pause menu; game clock stopped. | |
| F10 | While paused, tap the station once. Wait at least 10 s. | Nothing visible. Telemetry: `PHYS_EVENT evt=<n> result=ACCEPTED` then `PHYS_EVENT_DROPPED evt=<n> ... reason=NOT_PLAYING run_state=Paused`. | |
| F11 | Resume and watch for 5 s. | **No** INVESTIGATE and no WARN caused by the paused tap; no `DECISION` with `cause_evt=<n>`. | |
| F12 | Tap again in play. | **INVESTIGATE** as in F4, `cause_evt=<n+1>`. | |

**Link loss during play, and conditions**

| # | Do | Expect | Result |
|---|---|---|---|
| F13 | Unplug the USB cable during play. | Chip shows the fault; source STALE → UNAVAILABLE within ~3 s; the ghost keeps behaving on virtual rules only; no exception. Telemetry `SERIAL_LINK`, `SOURCE_STATUS to=STALE/UNAVAILABLE`. | |
| F14 | Replug. | `CONNECTED`, then `FRESH` again without restarting the game; events accepted again after resync. | |
| F15 | Press the ESP32 reset button during play. | `ESP32_REBOOT` in telemetry; state resumes; nothing from the old boot is accepted. | |
| F16 | Quit to the menu, choose `Virtual only`, play with the station connected and tap it. | Lines received and logged, events `MASKED`; the ghost does not react to the station. | |
| F17 | Choose `Simulated sensors`. | Chip says `SIMULATED`; the COM port is released (the Arduino Serial Monitor can open it); keys 1–9 drive the simulated station. | |
| F18 | Full run with the station connected: fuse, power, encounter, lantern, death and checkpoint, restart, ending. | All work as in virtual-only play. | |
| F19 | Quit the game. | The Arduino IDE can open COM6 immediately (port released). | |
| F20 | Run `python Tools/analyze_telemetry.py <file>` on the run's `.jsonl`. | Parses; `packets_rejected` ≈ 0; `inappropriate_attacks` 0; `SERIAL_LINK`, `STATION_LOG`, `SENSOR_DIAG` (every 5 s), `PKT_RX`, `PHYS_EVENT`, `DECISION` present. | |
| F21 | 30-minute soak with the station connected (any condition with physical input). | No exceptions in the player log; packet loss recorded. | |

## G. Sign-off

| Item | Value / result |
|---|---|
| SCL pin confirmed (A2 = C2) | |
| ECHO divider measured, max voltage at GPIO 26 | |
| Calibrated thresholds recorded (Firmware/README §5) | |
| Steps failed and how they were resolved | |
| Accepted by (names, date) | The project team, 26 Oct 2026 (owner-confirmed). The individual step results above were not recorded in this file. |
