# ESP32 USB-serial integration — hardware guide and acceptance checklist

How the physical station (ESP32 + VL53L0X + HC-SR04 + MPU6050) drives The Hollow, how to set it up in
Unity, and what has been verified. Firmware details (pins, protocol, calibration): [`Firmware/README.md`](../Firmware/README.md).

**Status (21 Oct 2026): software complete and tested; not yet run against the physical station.**
No ESP32 was connected to the development PC (its only COM ports were Bluetooth). Every "Pass" below is
an automated software test unless it says otherwise. Section 7 is the ordered checklist to run on the hardware.

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
PHYS-XR.exe -physxr-transport serial -physxr-serial-port COM5 -physxr-serial-baud 115200
```

Finding the port on Windows: Device Manager → **Ports (COM & LPT)** → plug the ESP32 in and see which entry
appears (typically "Silicon Labs CP210x USB to UART Bridge (COM5)" or "USB-SERIAL CH340 (COM5)"). No entry →
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
| Firmware build for ESP32 | `arduino-cli compile --fqbn esp32:esp32:esp32 Firmware/physxr_station` | **Compiles, 0 warnings** (against a compile-only VL53L0X stub; library not installed on the PC) |
| Serial transport suite | `dotnet run -c Release --project Tools/TestHarness -- --suite SerialTransport` | **25/25** in 15 sequential runs and 12 runs three-at-a-time (after fixing a test-only race in SER-09/SER-10) |
| Full harness (regression) | `dotnet run -c Release --project Tools/TestHarness` | **24 suites, 305 passed, 0 failed** (includes the gameplay bug-fix commit `1b7a475`); the serial work alone on its base: 23 suites, 291 passed |
| Unity compile | Editor import (`Logs/Editor.log`) and a scratch build of `Assembly-CSharp` against the 6000.5.8f1 DLLs | **0 errors, 0 warnings** |
| Real kernel32 backend | SER-17 + `--serial-monitor AUTO/COM13` on this PC | Missing port → `FAULTED ... not found`; Bluetooth-only → `no USB COM ports, only Bluetooth`; port released |
| Windows player build + `-physxr-selftest` / `-physxr-m2-selftest` | — | **Not re-run in this pass** (the Unity MCP bridge was disconnected; the Editor holds the project lock). Rebuild and re-run before the demo. |
| Live hardware | — | **Not run** (no ESP32 connected) |

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

## 7. Hardware acceptance checklist (run in this order)

Record pass/fail, the date and observations for each. Stop at the first failure and fix it before continuing.

**A. Before power**
1. Photograph the wiring. Confirm TRIG → GPIO 25 and ECHO → GPIO 26 **through a divider/level shifter**
   (HC-SR04 ECHO is 5 V). Confirm SDA → GPIO 21 and note whether SCL goes to GPIO **22** or **23**.
2. Confirm common ground and the supply each module is on (VL53L0X/MPU6050 breakouts: VIN or 3V3).

**B. Firmware alone (Arduino Serial Monitor, 115200)**
3. Install the Pololu VL53L0X library; compile and flash `physxr_station` with no errors.
4. On reset: `#HELLO` shows `scl=` (record it), `mpu=0x68` (or 0x69) and `tof=0x29`; no `#ERR`.
5. `#CAL state=done` within ~2 s with `noise` < 0.03 g; `#DIAG` shows `imu_cal=1`.
6. Close the Serial Monitor.

**C. Monitor without Unity** (`--serial-monitor AUTO 120`)
7. `LINK CONNECTED COMx (station)`; state lines accepted, `rejected 0`, `lost 0` over 2 minutes.
8. Empty zone: `STATE NONE/HIGH`; ToF reads "nothing in range" or a far distance; sonar valid.
9. Move a hand/object from 120 cm to 10 cm and back: `tof_cm` and `us_cm` follow it; state goes
   NONE → NEAR → VERY_NEAR → NEAR → NONE with no flicker at the boundaries; HIGH when both agree.
10. Cover only the ToF, then only the ultrasonic: confidence drops to LOW; presence never jumps up.
11. Leave the station untouched 5 min: **no** `EVENT` lines; `imu_g` ≈ 1.00 and steady.
12. Tap/shake the station 20 times, ≥ 2 s apart: exactly **20** `EVENT n ACCEPTED`, each followed by 2 `DUPLICATE`.
13. Press the ESP32 reset button: `BOOT new boot`, state resumes within ~3 s, next event accepted after resync.
14. Unplug for 10 s and replug: `LINK FAULTED/DISCONNECTED` then `CONNECTED` again without restarting the monitor.
15. Calibrate thresholds if 9–12 need it (Firmware/README.md §5); record the values.

**D. In the game (Windows)**
16. Menu → `Physical - USB serial`, `AUTO`: panel shows `CONNECTED`, the HELLO identity and live readings.
17. Pick the explicit COM port instead of AUTO: same result. Pick a wrong port: `FAULTED` with a reason; back to the right one recovers.
18. Begin. HUD chip: `USB COMx CONNECTED - FRESH`. F1 overlay: readings update, `accepted` rises, `rejected` 0.
19. Before the encounter, NEAR + tap: The Hollow goes to INVESTIGATE (F1 `decision` line: INVESTIGATE `[INNER]`, reason "INVESTIGATE: qualified physical disturbance detected"); `PHYS_EVENT ACCEPTED` in telemetry.
20. Partially cover one sensor at NEAR: WARN, reason "WARN: physical condition detected with low confidence" (R4); a light flickers.
21. In the ghost's threat zone with a person VERY_NEAR (both sensors): PROTECT `[S]`, reason "PROTECT: player at risk + ..." (R1); lights steady.
22. Unplug during play: chip shows the fault, source STALE → UNAVAILABLE, the ghost continues on virtual rules; replug: FRESH again, no Unity restart.
23. Encounter, lantern, death/checkpoint, restart and ending still work with the station connected.
24. Switch the menu to `Simulated sensors`: the chip says `SIMULATED`, the COM port is released
    (the Arduino Serial Monitor can open it), and keys 1–9 drive the ghost.
25. Quit; confirm the COM port can be opened by the Arduino IDE immediately (port released).
26. Telemetry (`%USERPROFILE%\AppData\LocalLow\...\telemetry`): `SERIAL_LINK`, `STATION_LOG`, `SENSOR_DIAG`
    every 5 s, `PKT_RX`, `PHYS_EVENT`, `DECISION`; `python Tools/analyze_telemetry.py <file>` parses it.
27. Run a 30-minute soak with the station connected: no exceptions in the player log, loss recorded.

## 8. Open items and assumptions

- **SCL pin**: 23 (your latest code) vs 22 (poster, I²C scanner). The firmware probes both and reports the
  one that works; the wiring itself must be checked (A1, B4).
- **TRIG/ECHO**: 25/26 from the bench prototype; the poster says 5/18. Confirm against the wiring (A1).
- **ECHO level shifting** is required by the HC-SR04's 5 V output and was not verified on the build.
- **Thresholds** (proximity 30/80 cm, disturbance 0.08 g / 3 samples / 1.5 s) are starting values, not calibrated.
- **VL53L0X library**: the real Pololu library was not available on the PC; the firmware was compiled
  against a stub of its public API only. Compile with the real library before flashing (B3).
- **USB adapter behaviour on open**: DTR/RTS are left de-asserted so opening should not reset the board;
  if a board resets anyway, the boot-ID handling covers it. Not observed on hardware.
- **Player build and in-player self-tests** were not re-run after these changes.
