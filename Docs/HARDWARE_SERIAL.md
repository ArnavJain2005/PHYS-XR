# ESP32 USB-serial integration — hardware guide and acceptance checklist

How the physical station (ESP32 + VL53L0X + HC-SR04 + MPU6050) drives The Hollow, how to set it up in
Unity, and what has been verified. Firmware details (pins, protocol, calibration): [`Firmware/README.md`](../Firmware/README.md).

**Status (21 Oct 2026, evening): software complete and re-verified; not yet run with this firmware on the
physical station.** The firmware compiles against the real Pololu VL53L0X 1.3.1 (repeated: 0 warnings,
`VL53L0X.cpp` linked). The station's board is now connected as **COM6** (Silicon Labs CP210x) but still runs
an old bench sketch; `physxr_station` has not been flashed. A read-only look and a USB-serial player
self-test against it are recorded in section 6. Every "Pass" below is an automated software test unless it
says otherwise. The ordered hardware procedure is [`HARDWARE_ACCEPTANCE.md`](HARDWARE_ACCEPTANCE.md).

> **Physical verification required before powering the station with this firmware**
>
> | Item | Status | Why it matters |
> |---|---|---|
> | **HC-SR04 ECHO → GPIO 26 voltage protection** (divider or level shifter; ECHO is 5 V) | Reported present, **not measured** | Without it the 5 V echo can damage GPIO 26. Measure ≤ 3.3 V (checklist A1, C4). |
> | **I²C SCL: GPIO 22 or 23** | **Unresolved** (latest code 23; poster and I²C scanner 22) | The firmware tries 23 then 22 and reports `scl=` in `#HELLO`; the wire must be traced and recorded (A2, C2). |
> | **Ultrasonic not ranging** | The old sketch printed `SONAR No echo` on every cycle (21 Oct) | If it persists after flashing, confidence stays LOW and R1 PROTECT / R3 INVESTIGATE never fire. Check 5 V supply, TRIG/ECHO not swapped, divider orientation, ground (A4, A5, C4). |
> | **SDA 21 and common ground** | Not confirmed | No I²C devices without them (A3). |
> | **ToF field of view** | The old sketch read 42–52 mm with nothing intended in front | Would hold presence at VERY_NEAR; clear the field of view (A6). |

## 1. Architecture

USB serial is a transport, not a second input system. It produces exactly what the UDP receiver and the
simulator produce — one contract line with its Unity-side arrival time — and everything after that is the
existing, unchanged pipeline:

```
ESP32 firmware (Firmware/physxr_station)
  sensors -> validity/filter -> per-sensor class -> fusion + disturbance detector -> contract v1 line
        | USB cable, COM port, 115200 8N1, '\n'-framed
Win32SerialPorts (kernel32)            Communication/SerialPortIO.cs
SerialLineReceiver (reader thread)     Communication/SerialLineReceiver.cs
  framing, routing, arrival stamp T2, bounded queue, reconnect
    '#' lines -> StationDiagnostics (display + telemetry only, never PhysicalState)
    contract lines -> IDatagramSource
SensorFeed.Pump (main thread)          <- same class UDP and the simulator use
PacketValidator (RequireVersion on serial) -> PhysicalCommunicationPipeline
  (order, boot, transit age, freshness, resync, event dedup, blackout, masking)
PhysicalStateManager / accepted PhysicalEvents
WorldStateManager (physical neutral unless FRESH; 3 s disturbance pulse per accepted event)
EncounterDecisionLayer -> RuleBasedDecisionProvider R1-R8 -> GhostAction
GhostRuntime -> GhostExecutionState -> GhostMotionController / HollowVisual -> telemetry
```

Why serial rather than UDP: the project's physical link is USB serial (final report §3.3; the working
bench prototype used COM13 at 115200). The Technical Spec's contract v1 is kept unchanged; only the
framing differs. UDP remains available (and `Tools/esp32_sim.py` still targets it).

Why kernel32 rather than `System.IO.Ports`: the project builds against .NET Standard 2.1, which does not
include `System.IO.Ports`; switching the API level would change `ProjectSettings.asset`. The team's
earlier `ESP32Receiver` used kernel32 for the same reason. Consequence: USB serial works on the Windows
desktop build only (on Quest it reports `FAULTED: only supported on the Windows desktop build`).

### Threading and lifecycle

- One reader thread per link; `Start()` is idempotent and never starts a second reader; `Stop()` ends the
  thread and closes the port within ~0.1 s (read timeout) and waits for it.
- The reader never touches Unity objects. Lines go on a bounded queue (256; oldest dropped and counted);
  `SensorService.Update` (execution order −200) drains it on the main thread.
- Opening the port leaves DTR/RTS de-asserted, so it does not hold the ESP32 in reset or bootloader mode.
- Errors never escape: unplug, driver errors and missing ports become `FAULTED`/`DISCONNECTED` with a
  reason, and the thread retries every 1 s, backing off to 4 s. A port that goes silent is checked for
  existence every 1.5 s, so an unplug is noticed even when the driver reports nothing.
- An ESP32 reset is recognised from the new `boot` ID: sequence tracking restarts and events wait for
  two in-order lines (resync), as for UDP.
- The port is released on leaving Play Mode, quitting, returning to a non-serial menu mode, or switching
  port/baud.

### Connection states (menu, HUD, overlay, telemetry `SERIAL_LINK`)

| State | Meaning |
|---|---|
| `DISCONNECTED` | No port open: stopped, the port vanished, or AUTO found no station |
| `CONNECTING` | Opening a port, or probing ports in AUTO mode |
| `CONNECTED` | Port open. The label adds `(no PHYS-XR data yet)` until a contract line or `#HELLO` arrives |
| `FAULTED` | The last attempt failed (port in use, missing, device error, unsupported platform); retrying |

"CONNECTED" only means the port is open. Whether the game uses the data is the separate source status:
`FRESH` (accepted line ≤ 0.5 s ago), `STALE` (≤ 3 s), `UNAVAILABLE`. Only `FRESH` lets physical state reach R1–R8.

## 2. Sensor-by-sensor path to the ghost

Thresholds marked * are uncalibrated starting values (Firmware/README.md §5).

### Ultrasonic HC-SR04 (TRIG 25, ECHO 26) — secondary proximity evidence

| Step | What happens |
|---|---|
| Raw reading | Echo pulse width (µs) timed by interrupt, 15 Hz; `cm = µs × 0.0343 / 2` |
| Validity | 2–400 cm valid; no echo in 25 ms = invalid; no valid sample for 300 ms = invalid |
| Semantic | Median of 3 → NONE / NEAR (≤ 80*, exit > 90 cm) / VERY_NEAR (≤ 30*, exit > 35 cm) → fused with ToF |
| Role | Never an independent gameplay input: it only confirms or contradicts the ToF (agreement → HIGH confidence) |

### VL53L0X ToF (I²C 0x29) — primary proximity

| Step | What happens |
|---|---|
| Raw reading | mm, 25 Hz continuous, read when data-ready |
| Validity | range status 11 → valid; ≥ 8190 mm or > 1200 mm → valid "nothing in range" (NONE); other status / I²C error / timeout → invalid |
| Semantic | Median of 3 → same classes and hysteresis as the ultrasonic |

### Fusion → presence/confidence (Hardware Spec §7.1)

| ToF | Ultrasonic | presence | confidence |
|---|---|---|---|
| X | X (same) | X | HIGH |
| X | Y (different) | lower of X, Y | LOW |
| valid X | invalid | X | LOW |
| invalid | valid Y | Y | LOW |
| invalid | invalid | NONE | LOW |

### MPU6050 (I²C 0x68/0x69) — local disturbance

| Step | What happens |
|---|---|
| Raw reading | ax, ay, az in g (±2 g, 100 Hz), gyro unused |
| Calibration | 2 s still at power-up: baseline |A| and noise; repeated if the station moved; fault if outside 0.7–1.3 g |
| Semantic | D = | |A| − baseline |, 3-sample median; > ON* (max 0.08 g, 6 × noise) for 3 samples → **one** event; re-arm after D < ON/2 and 1.5 s |
| Wire | `eventId` +1, `tEvent`, on 3 consecutive lines (`disturbance=true`) |

### From the state line to visible behaviour

The `DECISION` record's rule ID is the encounter layer's (`INNER` = R1–R8 result passed through, `S` =
safety PROTECT/RETREAT); the R-rule is identified by its reason text, which is also shown in the F1 overlay.

| Physical action | Semantic state / event | Unity gate | Rule | GhostAction | What you see / hear | Telemetry proving it |
|---|---|---|---|---|---|---|
| Person at NEAR distance, both sensors agree, then a knock/shake of the station | `NEAR/HIGH` + DISTURBANCE event | FRESH, event accepted once (dedup), 3 s pulse | **R3** (not in threat zone) | **INVESTIGATE** | The Hollow goes to the anchor nearest the player and lingers | `PKT_RX`, `PHYS_EVENT result=ACCEPTED`, `DECISION` rule `INNER`, reason `INVESTIGATE: qualified physical disturbance detected`, `BEHAVIOUR` |
| Person close (VERY_NEAR, both agree) while the player is in the ghost's threat zone | `VERY_NEAR/HIGH` | FRESH | **R1** | **PROTECT** | The Hollow guards the objective anchor; the game holds the lights steady | `PKT_RX presence=VERY_NEAR`, `DECISION` rule `S`, reason `PROTECT: player at risk + high-confidence VERY_NEAR physical presence`, `BEHAVIOUR` |
| Something NEAR but the two sensors disagree / one invalid | `NEAR/LOW` | FRESH | **R4** | **WARN** | The Hollow stops and faces the player; a light flickers | `PKT_RX confidence=LOW`, `DECISION` reason `WARN: physical condition detected with low confidence` |
| Shake with nobody in range | `NONE` + event | FRESH, event accepted | **R5** | **WARN** | As above | `PHYS_EVENT ACCEPTED`, `DECISION` reason `WARN: disturbance detected without proximity confirmation` |
| Cable pulled / ESP32 off | no lines | STALE → UNAVAILABLE; physical neutral | R6–R8 only | FOLLOW (virtual rules only) | Ghost behaves as in virtual-only mode | `SERIAL_LINK DISCONNECTED/FAULTED`, `SOURCE_STATUS to=STALE/UNAVAILABLE` |

Arbitration is unchanged: the encounter layer (E0–E5, warning beat, attack timing, lantern interruption,
cooldowns, restart blackout, terminal gate) sits after R1–R8. ATTACK is only produced by E1/E3/E5, which
depend on encounter state, never on a sensor; SER-18h floods VERY_NEAR/HIGH + events with the player at
1.2 m and verifies no ATTACK. In the **Virtual only** condition the same lines are received and logged but
masked (events `MASKED`, physical state neutral).

## 3. Unity configuration

**Menu → Sensors** cycles: `Virtual only` → `Simulated sensors` → `Physical - USB serial` → `Physical - UDP 47800`.

With **Physical - USB serial** selected:
- a **Serial port** button cycles `AUTO` and every COM port present (re-scanned on each click);
- the link opens immediately and the right-hand panel shows link state, counters, and the station's live
  readings (`#HELLO` identity and pins, ToF/sonar distance + class + age, IMU |A|, D, threshold, stage);
- the choice is saved (PlayerPrefs) for the next launch.

`AUTO` opens each non-Bluetooth COM port and keeps the first that sends PHYS-XR lines within 3 s. Naming
the port avoids probing other devices.

In game: the HUD sensor chip shows the mode and link; **F1** shows the developer overlay with the full
diagnostics block. **Simulated sensors** is always labelled `SIMULATED`, reads no hardware, and is never
used as a fallback: if the serial link is down in Physical mode the game shows the fault and runs without
physical input.

Command line (Windows player), e.g. a kiosk shortcut:

```bash
PHYS-XR.exe -physxr-transport serial -physxr-serial-port COM6 -physxr-serial-baud 115200
```

Finding the port on Windows: Device Manager → **Ports (COM & LPT)** → plug the ESP32 in and see which entry
appears (typically "Silicon Labs CP210x USB to UART Bridge (COMn)" or "USB-SERIAL CH340 (COMn)"). On the
development PC the station is **COM6** (CP210x); COM3, COM5, COM7 and COM8 there are Bluetooth. No entry →
install the CP210x/CH340 driver or try a data-capable cable. Entries named "Standard Serial over Bluetooth
link" are not the ESP32. The Arduino IDE's port menu shows the same name.

## 4. Startup procedure

1. Flash `Firmware/physxr_station` (Firmware/README.md §2). Close the Arduino Serial Monitor.
2. Place the station where it will be used and plug it into the PC. Keep it still for 2 s (IMU calibration).
3. Optional, without Unity: `dotnet run -c Release --project Tools/TestHarness -- --serial-monitor AUTO 30`
   — expect `LINK CONNECTED`, `#HELLO ...`, `STATE NONE/HIGH`, readings updating; then close it.
4. Start the game → Sensors: `Physical - USB serial` → port `AUTO` (or the COM port).
5. Wait for `USB COMx CONNECTED` and readings that change when you move a hand in front of the sensors.
6. Begin. The HUD chip should read `SENSORS USB COMx CONNECTED - FRESH`.

## 5. Reconnection and troubleshooting

- Unplug/replug, ESP32 reset button, or power loss: the game keeps running with physical input off
  (STALE → UNAVAILABLE), the link reconnects by itself, the new boot is logged (`ESP32_REBOOT`), state is
  used at once and events after two in-order lines. No restart of Unity is needed.
- Port in use: close the Arduino Serial Monitor / the TestHarness monitor / another game instance.
- Everything else: Firmware/README.md §6 and the `reject` line of the F1 overlay.

## 6. Tests and results

| What | Command | Result (21 Oct 2026) |
|---|---|---|
| Firmware semantic core (host) | `g++ -std=c++17 -I Firmware/physxr_station Firmware/tests/semantics_test.cpp -o semantics_test && ./semantics_test` | **47/47** |
| Firmware build for ESP32 | `arduino-cli compile --fqbn esp32:esp32:esp32 Firmware/physxr_station` | **Compiles, 0 warnings** with the real Pololu VL53L0X 1.3.1 and esp32 core 3.3.11: 305,548 B flash (23 %), 24,628 B RAM (7 %) |
| Serial transport suite | `dotnet run -c Release --project Tools/TestHarness -- --suite SerialTransport` | **29/29** after `02cece8` (raw mode, SER-19..22) and the `dev=` requirement on the serial link (SER-05). Earlier: 25/25 in 15 sequential and 12 parallel runs; SER-09 30/30 idle and 31/31 under load after the race fix in `5efd34f` |
| Full harness (regression) | `dotnet run -c Release --project Tools/TestHarness` (after `dotnet build -c Release --no-incremental Tools/TestHarness`) | **24 suites, 314 passed, 0 failed** (21 Oct 2026, evening; includes F1 and the raw mode) |
| Firmware build, repeated | `arduino-cli compile --fqbn esp32:esp32:esp32 --warnings all Firmware/physxr_station`, with `ARDUINO_DIRECTORIES_USER` pointing at the scratch install of Pololu 1.3.1 | **0 warnings**, 305,548 B flash, 24,628 B RAM; the build folder shows the real `VL53L0X.cpp` compiled and linked. Host test 47/47 |
| Unity compile | Editor import (`Logs/Editor.log`) and a scratch build of `Assembly-CSharp` against the 6000.5.8f1 DLLs | **0 errors, 0 warnings** |
| Real kernel32 backend | SER-17 + `--serial-monitor AUTO/COM13` on this PC | Missing port → `FAULTED ... not found`; Bluetooth-only → `no USB COM ports, only Bluetooth`; port released |
| Windows player build of `a6d976a` | `Unity -batchmode -quit -projectPath . -executeMethod PHYSXR.EditorTools.M2SceneSetup.BuildWindows -buildPath Builds/PHYSXR_Windows/PHYS-XR.exe` | **Succeeded**, 0 errors (486 warnings, all from the Sentis package's shaders) |
| `-physxr-m2-selftest` (headless player) | see README | **23/23** |
| `-physxr-selftest` (VIRTUAL_ONLY / SIMULATED) | see README | **34/34** each |
| `-physxr-selftest -physxr-condition PHYSICAL_PLUS_VIRTUAL -physxr-transport serial -physxr-serial-port AUTO`, no ESP32 | — | **34/34**; link `DISCONNECTED: no USB COM ports, only Bluetooth ...`, 0 packets, 0 physical events, 0 of 33 decisions citing physical input, port released on exit |
| Same run on a player of `8bf28a6` (includes the `0241e15` telemetry fix), 3 consecutive runs, telemetry file of each run inspected | — | **3/3**: 34/34 each; record 0 of each `.jsonl` (ts 18–19 ms, frame 0) is the single initial `SOURCE_STATUS`: `from=NONE to=UNAVAILABLE condition=PHYSICAL_PLUS_VIRTUAL transport=USB_SERIAL serial=AUTO @ 115200`, before `SERIAL_LINK` and every encounter record; 0 `PKT_RX`, 0 `PHYS_EVENT`, 0 decisions citing physical input; "USB serial link released" logged on exit. The pre-fix player (`a6d976a`) wrote 0 `SOURCE_STATUS` records in the same test |
| Read-only look at the connected board | `--serial-monitor COM6 15 115200 --raw` | Board on COM6 runs an **old bench sketch**: `LASER Distance: 42–52 mm`, `MPU Accel Z ≈ 1.03 g`, `SONAR No echo` every cycle. Unity side: 0 lines accepted, source UNAVAILABLE, old sketch reported, port released |
| Player build of `02cece8` + the `dev=` change (batch, `Builds/M2_Windows`) | `M2SceneSetup.BuildWindows` | **Succeeded**, 0 errors. M2 23/23, default 34/34, SIMULATED 36/36 |
| `-physxr-selftest -physxr-condition PHYSICAL_PLUS_VIRTUAL -physxr-transport serial -physxr-serial-port COM6`, old sketch attached | — | **34/34**. `SOURCE_STATUS ... transport=USB_SERIAL serial=COM6` first; `SERIAL_LEGACY_FIRMWARE` once; 0 `PKT_RX`, 0 `PHYS_EVENT`, 0 of 34 decisions citing physical input; port released |
| Live hardware with `physxr_station` | — | **Not run** (firmware not flashed). See [`HARDWARE_ACCEPTANCE.md`](HARDWARE_ACCEPTANCE.md) |

`SerialTransportTests` covers: framing (partial lines, several per read, CRLF, empty, oversized), line
routing (contract / `#` diagnostic / old raw sketch / boot noise), the validator on the firmware's exact
output and on bad input (missing/unsupported version, numeric or lower-case enums, missing fields, NaN,
Infinity, negative or fractional numbers, bad event IDs, foreign device, invalid UTF-8, > 512 bytes),
diagnostics parsing and ages, missing port, port in use, device error and reconnect, silent removal and
replug, 20× start/stop with no leaked thread or handle, AUTO probing (Bluetooth skipped, non-station
port closed), unsupported platform, bounded queue, the real kernel32 backend, and end to end through
R1–R8: INVESTIGATE with one pulse, PROTECT only on HIGH confidence, WARN on LOW, diagnostics/raw lines
never changing state, link loss → neutral, reboot → resync, **serial and simulator giving identical
decisions frame by frame**, and no ATTACK from sensor data.

## 7. Hardware acceptance checklist

The ordered, recordable procedure is [`HARDWARE_ACCEPTANCE.md`](HARDWARE_ACCEPTANCE.md): A physical checks
before power, B flashing, C boot and identity, D each sensor without Unity, E USB link robustness, F in the
game (sensor-to-ghost responses, the F1 pause/disturbance/resume scenario, link loss, conditions, soak),
G sign-off. It supersedes the 27-step list that was here.

## 8. Open items and assumptions

- **SCL pin**: 23 (your latest code) vs 22 (poster, I²C scanner). The firmware probes both and reports the
  one that works; the wiring itself must be checked (A1, B4).
- **TRIG/ECHO**: 25/26 from the bench prototype; the poster says 5/18. Confirm against the wiring (A1).
- **ECHO level shifting** is required by the HC-SR04's 5 V output and was not verified on the build.
- **Thresholds** (proximity 30/80 cm, disturbance 0.08 g / 3 samples / 1.5 s) are starting values, not calibrated.
- **VL53L0X library**: compiled against the real Pololu 1.3.1 (installed in a scratch folder for the check).
  Install it in the Arduino IDE (Library Manager, "VL53L0X" by Pololu) before flashing (B3). That the
  station's ToF breakout is a VL53L0X comes from the schematic poster; it is not physically confirmed.
- **USB adapter behaviour on open**: DTR/RTS are left de-asserted so opening should not reset the board;
  if a board resets anyway, the boot-ID handling covers it. Not observed on hardware.
- **Telemetry**: fixed in `0241e15` and verified in a player (see section 6). Builds before it lose the
  first `SOURCE_STATUS` record (condition, transport, port).
- **Port release with a real port**: the player and the harness monitor both logged "released" after
  holding the real COM6 (21 Oct). That the Arduino IDE can reopen it immediately is checklist F19.
- **Serial contract**: on the USB-serial link Unity requires `v=1` **and** `dev=` on every state line; a line
  missing either is rejected (`VERSION_UNSUPPORTED` / `FOREIGN_DEVICE`). UDP and the legacy format keep both
  optional.
- **Memory**: the intermittent self-test early exits most likely come from the PC running out of commit
  memory while the Editor, its build workers and several players ran at once (one occurrence coincided with
  a Windows low-virtual-memory event; with the Editor closed, all later runs passed). Not proven. Run
  hardware tests with the Editor closed or with memory to spare.
