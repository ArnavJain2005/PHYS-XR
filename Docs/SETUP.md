# Setup and running

Full installation and run instructions. The main README has the [short path](../README.md#try-it); the [Quick Start from source](#quick-start-from-source) below is the full version. Hardware work must follow [`HARDWARE_ACCEPTANCE.md`](HARDWARE_ACCEPTANCE.md); firmware details are in [`Firmware/README.md`](../Firmware/README.md).

## Quick Start from source

The software demo needs no hardware: Windows, **Unity 6000.5.8f1** (from `ProjectSettings/ProjectVersion.txt`) and, for the tests, a .NET SDK that builds `net9.0`.

1. **Clone** the repository (see [Get the code](#get-the-code)).
2. **Open** the folder in Unity Hub with 6000.5.8f1. The first import takes a while.
3. **Run.** Open `Assets/PHYSXR/Scenes/Bootstrap.unity` and press Play, keeping the Game view focused.
4. **Simulate the sensors.** On the menu, click **`Sensors:`** until it reads **`Sensors:  Simulated sensors`**, then click **Begin**.
5. **Watch the ghost think.** Press **F1**; the overlay's `decision` line shows the action, `[rule]` and reason.
   - Walk into the corridor and look at The Hollow. You get one lunge (`ATTACK [E5]`), and then it follows you.
   - **Before picking up the fuse**, stand at least 5 m from The Hollow, outside its 3.5 m threat zone. Press **`2`** (someone NEAR) and then **`5`** (a knock).
   - Expect `INVESTIGATE`. The ghost walks off to search near you. Press **`1`** to clear the presence.
   - This is simulated input through the real validator and rules.
6. **Run the tests** (no Unity needed): see [Automated tests](#5-automated-tests). The harness should end with `SUITES RUN: 26, TOTAL FAILED: 0`, and the firmware host test with `53 checks, 0 failed`.

### Controls

**Desktop:** WASD and mouse to move and look · Shift to sprint · Space to jump · E to interact · F or right mouse for the lantern · Esc to pause · F1 for the developer overlay. Keys `1`–`9` drive the simulated station (only in Simulated mode). Speeds, cooldowns and the fault-injection keys are in [`ARCHITECTURE.md`](ARCHITECTURE.md#controls).

**Quest Touch (PC VR over Quest Link):** left stick to move (click to sprint) · right stick to turn · right trigger or A to interact · left trigger for the lantern · B to jump · left menu button to pause.

## Installation and setup

### Prerequisites

| Tool | Version | Needed for | Verified |
|---|---|---|---|
| Unity Editor | **6000.5.8f1** (`ProjectSettings/ProjectVersion.txt`) | Opening, playing, building | Yes (builds on 21 Oct) |
| Unity packages | URP 17.5.0, Input System 1.20.0, uGUI 2.5.0, OpenXR 1.17.1, XR Interaction Toolkit 3.5.1, XR Management 4.7.0 (all in `Packages/manifest.json`) | Game; PC VR | Yes |
| .NET SDK | Any SDK that builds `net9.0` (C# pinned to 9 to match Unity) | `Tools/TestHarness` | Yes, SDK 10.0.402 on 22 Oct |
| C++17 compiler | e.g. g++ | Firmware host test | Yes, g++ (WinLibs) |
| Python 3 | Standard library only | `esp32_sim.py`, `analyze_telemetry.py` | Yes, 3.14 |
| Arduino IDE 2.x or `arduino-cli` + **esp32 by Espressif** core 3.x + **Pololu VL53L0X** 1.3.1 | | Flashing the station | Compile verified 21 Oct (scratch install) |
| CP210x or CH340 USB driver | | ESP32 COM port | Station shows as CP210x on the dev PC |
| Windows 10/11 | | USB serial (kernel32) and the player | Yes |

The XR packages provide PC VR through OpenXR with the Oculus Touch profile. `Packages/manifest.json` also lists AI Navigation, AI Assistant and inference packages; the game's NavMesh uses the built-in `UnityEngine.AI` API, and no game code references the other two.

### Get the code

```bash
git clone https://github.com/ArnavJain2005/PHYS-XR.git
```

The default branch is `main`. Open the repository root in Unity Hub with 6000.5.8f1. The first import regenerates `Library/` (gitignored). The repository was previously published as `Dr-Dre420/PHYS-XR`, and GitHub redirects that address. An older clone can switch with `git remote set-url origin https://github.com/ArnavJain2005/PHYS-XR.git`.

### Build the Windows player

From the Editor: `PHYS-XR > M2 > Create Or Repair Scenes` (once, if scenes or the build list are broken). Then build with `File > Build Profiles`, using the scenes `Bootstrap` (index 0) and `Game_BlackthornHouse` (index 1).

From the command line (close the Editor first, so the project is not locked):

```bash
Unity -batchmode -quit -projectPath . -executeMethod PHYSXR.EditorTools.M2SceneSetup.BuildWindows -buildPath Builds/PHYSXR_Windows/PHYS-XR.exe
```

Without `-buildPath`, the output goes to `Builds/M2_Windows/PHYS-XR.exe`. `Builds/` is gitignored, so **check the executable's timestamp against your commit before a demo**. A stale build in one of the two folders is easy to launch by mistake.

### Flash the firmware (after the hardware safety checks)

> [!CAUTION]
> Do not power the station with this firmware until checklist steps **A1–A3** pass with the USB cable unplugged. A1 is the ECHO divider: an unprotected 5 V echo can damage GPIO 26. A2 is the SCL pin. See [Bring-up order](HARDWARE_ACCEPTANCE.md#overview-bring-up-order). The accepted station runs `physxr_station` 1.0.0; its `#HELLO` identity line appears in the logged telemetry ([`Evidence/`](../Evidence/README.md)).

```bash
arduino-cli lib install "VL53L0X"
```

```bash
arduino-cli compile --fqbn esp32:esp32:esp32 Firmware/physxr_station
```

```bash
arduino-cli upload --fqbn esp32:esp32:esp32 -p COM6 Firmware/physxr_station
```

`COM6` is the development PC's port; find yours under Device Manager → Ports (COM & LPT). Close the Arduino Serial Monitor before starting the game: only one program can hold the port.

### Where things are written

| Artifact | Location |
|---|---|
| Telemetry | `%USERPROFILE%\AppData\LocalLow\DefaultCompany\PHYS_XR\telemetry\` |
| Player log | Unity's default `Player.log` in the same `LocalLow\DefaultCompany\PHYS_XR` folder, or the path given with `-logFile <path>` |
| Capture tour | the folder passed to `-physxr-capture`, else `<persistentDataPath>/captures` |
| Saved menu choices | Unity `PlayerPrefs` (sensor mode, port, baud) |

### Troubleshooting

<details>
<summary>Common symptoms: port in use, no COM port, no data, not FRESH, slow Editor self-tests, early self-test exits, stale harness binary</summary>

| Symptom | Fix |
|---|---|
| `FAULTED … in use by another program` | Close the Arduino Serial Monitor, the harness monitor, or another game instance |
| `no USB COM ports, only Bluetooth` / `COMx not found` | ESP32 unplugged, charge-only cable, or missing CP210x/CH340 driver |
| `CONNECTED (no PHYS-XR data yet)` for > 3 s | Wrong baud or a different sketch; flash `physxr_station` |
| `CONNECTED (old raw-distance sketch…)` | The board still runs the bench sketch |
| HUD not `FRESH` while CONNECTED | Lines arrive but are rejected; read the `reject` line in the F1 overlay |
| Editor Play mode runs at ~1.5 fps during self-tests | An unfocused Editor throttles Play mode; use the Windows player |
| Player self-test exits 1 with no `SELFTEST END` | Most likely low commit memory (see [issue L7](TechnicalSpec_MilestoneStatus.md#open-issues)); close the Editor and large apps |
| Harness reports failures after a manual code mutation | Rebuild with `--no-incremental` (timestamps can let a stale binary run) |

</details>

More: [`Firmware/README.md`](../Firmware/README.md) §6, [`Docs/HARDWARE_SERIAL.md`](HARDWARE_SERIAL.md) §5.

## Running and demonstrating

### 1. In the Editor

Open `Assets/PHYSXR/Scenes/Bootstrap.unity` and press Play. Keep the Game view focused.

### 2. Standalone build

Launch `Builds/PHYSXR_Windows/PHYS-XR.exe` (or wherever you built it), choose **Sensors**, then **Begin**.

### 3. Simulation mode (no hardware)

- In game: **Sensors → Simulated sensors**. Keys `1–9` drive the simulated station; the HUD reads `SIMULATED`.
- Over real UDP: choose **Physical - UDP 47800**, then:

```bash
python Tools/esp32_sim.py --host 127.0.0.1 --scenario near --seconds 60
```

Scenarios: `idle`, `near`, `threat`, `flap`, `duplicates`, `outage`, `reboot`, `interactive`.

### 4. With real hardware

The team accepted the station hardware on 26 Oct 2026. For a new or rebuilt station, follow these steps:


1. Complete [`HARDWARE_ACCEPTANCE.md`](HARDWARE_ACCEPTANCE.md) A (meter checks), B (flash) and C (boot identity).
2. Check the station without Unity:

```bash
dotnet run -c Release --project Tools/TestHarness -- --serial-monitor AUTO 30
```

3. Quit the monitor (`Port released.`), start the game, choose **Sensors → Physical - USB serial**, port `AUTO` or `COMx`, and wait for live readings. **Begin**: the HUD chip must read `SENSORS USB COMx CONNECTED - FRESH`.
4. Kiosk-style launch: `PHYS-XR.exe -physxr-transport serial -physxr-serial-port COM6 -physxr-serial-baud 115200`.

### 4b. Meta Quest 2 / Quest 3 (PC VR)

The team has demonstrated the game with the physical station on Meta Quest 2 and Quest 3, running the Windows build as **PC VR through OpenXR over Quest Link**. There is no standalone (on-headset) Quest build: the station's USB-serial link needs the Windows PC. The project carries the OpenXR, XR Interaction Toolkit and Oculus Touch profile settings; Touch controllers are mapped in `DesktopInputAdapter` ([controls](#controls)). Step-by-step headset setup is not yet written down here.

> A **simulation run** proves the software chain from the validator onwards. A **hardware run** additionally proves wiring, firmware, calibration, USB timing and the device identity check. Only the second is evidence for the project's central claim.

### 5. Automated tests

```bash
dotnet run -c Release --project Tools/TestHarness
```

```bash
dotnet run -c Release --project Tools/TestHarness -- --suite SerialTransport
```

```bash
g++ -std=c++17 -I Firmware/physxr_station Firmware/tests/semantics_test.cpp -o semantics_test
```

Then run `./semantics_test`. Headless player self-tests (exit code = failure count):

```bash
Builds/PHYSXR_Windows/PHYS-XR.exe -batchmode -nographics -physxr-m2-selftest
```

```bash
Builds/PHYSXR_Windows/PHYS-XR.exe -batchmode -nographics -physxr-selftest -physxr-condition SIMULATED
```

Screenshot tour (windowed):

```bash
Builds/PHYSXR_Windows/PHYS-XR.exe -physxr-capture captures
```

### 6. The Ghost decision demonstration

The faculty run sheet [`Docs/DEMO_SCRIPT.md`](DEMO_SCRIPT.md) gives a hardware go/no-go, then a 12-beat script on **Simulated sensors** with the matching physical action next to each beat. The core beats, with the F1 overlay open:

| Beat | Do | Expect (`decision` line) |
|---|---|---|
| 3 | Stand ≥ 5 m from the ghost (outside its 3.5 m threat zone, before picking up the fuse), press `2` then `5` | `INVESTIGATE [INNER]` (R3) |
| 4 | Press `4` | `WARN [INNER]` low confidence (R4); press `4` again |
| 5 | Press `1` then `5` | `WARN [INNER]` without proximity (R5) |
| 6 | `Esc`, press `5`, resume | Nothing (F1 fix) |
| 7 | Within 3.5 m of the ghost, press `3` | `PROTECT [S]` (R1); press `1` within 5 s |
| 9–10 | After power, enter the dining room; lantern during wind-up | `WARN [E4]` → `ATTACK [E3]` → `RETREAT [E0]` |

Run the sensor beats **before** picking up the fuse, outside the threat zone. Afterwards R7 makes the ghost warn on its own.

### 7. Collecting telemetry

Every run writes telemetry automatically. Set a meaningful **Trial ID** in the menu, then pass the run's file (from the telemetry folder below, named `<trial>_Windows_<utc>.jsonl`) to the analyzer:

```bash
python Tools/analyze_telemetry.py path/to/trial_file.jsonl --csv summary.csv
```

### 8. Shutting down cleanly

- Quit from the pause menu or the end card. `SensorService` releases the serial link on quit (`USB serial link released` in the log), and also on returning to a non-serial mode or leaving Play mode.
- After quitting, check that `PHYS-XR.exe` is no longer running before reopening the port in the Arduino IDE (checklist F19).
- Stop the harness monitor with its duration argument or Ctrl+C; it prints `Port released.`
- `esp32_sim.py` stops after `--seconds`, or on `q` in interactive mode.
