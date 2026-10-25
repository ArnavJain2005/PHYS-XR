# Testing and evaluation

Detailed test inventory, verification log, telemetry schema and the planned evaluation protocol behind the evidence summary in the [main README](../README.md#evidence). The acceptance matrix (Technical Spec section G) lives in [`TechnicalSpec_MilestoneStatus.md`](TechnicalSpec_MilestoneStatus.md#acceptance-matrix-section-g); hardware procedures in [`HARDWARE_ACCEPTANCE.md`](HARDWARE_ACCEPTANCE.md).

Results are dated. A result is fresh only if its date says so; older figures are kept as historical records.

## Testing, experiments and validation

### Test inventory

| Level | What | How to run | Latest result |
|---|---|---|---|
| Unit + integration (no Unity) | 26 suites, 382 tests (final tree, 26 Oct 2026; 318 in 24 suites on `eccdb3b`) over `Core`, `Communication`, `PhysicalWorld`, `WorldState`, `Decision`, `Telemetry`, `Ghost` (non-Unity parts), `Game/Flow` | `dotnet run -c Release --project Tools/TestHarness` | **318 passed, 0 failed**, fresh 22 Oct 2026 on an export of the committed tree `eccdb3b` (the working tree did not build at that moment, with unfinished uncommitted test code). Earlier: 314 passed on `5124e90` and `dc932ca`, each after a `--no-incremental` rebuild |
| Firmware host test | Semantic core: classes, hysteresis, fusion, disturbance detector, line format | `g++ -std=c++17 …` (below) | **47 checks, 0 failed** (22 Oct, same two commits) |
| Firmware compile | ESP32 core 3.3.11 + Pololu VL53L0X 1.3.1 | `arduino-cli compile --fqbn esp32:esp32:esp32 Firmware/physxr_station` | 0 warnings, 305,548 B flash (21 Oct; `arduino-cli` is not on the PATH of the machine used for this README, so not re-run) |
| Player self-test: route | Objective route through the real controller | `-physxr-m2-selftest` | **23/23** (22 Oct) |
| Player self-test: encounter | Readables, sprint/jump, E5, surge, attack, lantern, death, solid body, checkpoint, 10 restarts | `-physxr-selftest` | 34/34 (21 Oct) |
| Player self-test: encounter, simulated input | Above + F1 pause checks P01/P02 through the real validator | `-physxr-selftest -physxr-condition SIMULATED` | **36/36** (22 Oct) |
| Player self-test: USB serial | Above, with the serial link to a real COM port | `… -physxr-condition PHYSICAL_PLUS_VIRTUAL -physxr-transport serial -physxr-serial-port COM6` | 34/34 with the **old sketch** attached: 0 packets accepted (21 Oct) |
| Hardware-in-the-loop | Station with `physxr_station` | [`HARDWARE_ACCEPTANCE.md`](HARDWARE_ACCEPTANCE.md) A–G | **Not run** |
| Build | Windows player, `BuildPipeline` | `M2SceneSetup.BuildWindows` | Succeeded, 0 errors (21 Oct) |
| Manual play | Team play-through on `1b7a475` | — | 5 items **team-reported PASS** (collision, readables, first sighting, sprint/jump, pause) |
| User study | First-time players | — | **Not run** |

Each level proves only its own layer. Passing harness tests and a firmware compile do **not** show that the station works. A player self-test with the USB link open does **not** show that real sensor data was accepted (that run accepted 0 packets). Simulated input is a development and pilot condition, not a substitute for hardware-in-the-loop testing.

**How "318" is counted.** The harness registers 24 suites in [`Tools/TestHarness/Program.cs`](../Tools/TestHarness/Program.cs) and prints one `PASS:`/`FAIL:` line per test, then `SUITES RUN: 24, TOTAL FAILED: 0`; its exit code is the failure count. 314 is the number of `PASS:` lines. History: 310 at `ec4af1e` (F1 fix, which added WSM-PAUSE-01..05), then 314 at `02cece8` (SER-19..22), then 318 at `1978b09` (PRI1 event-attribution tests in `RuntimePipelineIntegrationTests`). Older documents quoting 310 are dated before that change.

<details>
<summary><b>Per-suite breakdown</b> (from the 22 Oct harness run)</summary>

| Suite | IDs | Tests |
|---|---|---|
| 12 original suites: simulated source, communication, world state, R1–R8, executor, controller, telemetry, pipeline, E2E, decision history, temporal context, hysteresis | COMM, WORLD, DEC, GHOSTEXEC, GHOSTCTRL, TEL, GHOSTPIPE, E2E, DH, PDC, HYST, … | 134 |
| `EncounterDecisionLayerTests` | WB | 13 |
| `RuntimePipelineIntegrationTests` | RT, PRI1 | 16 |
| `TerminalGateTests` | TERM | 15 |
| `GameFlowTests` | FLOW-01..16 | 16 |
| `DefectRegressionTests` | P1..P5, Q | 26 |
| `EncounterRuleTests` | ENC | 25 |
| `SensorScenarioTests` | SENS-T01..T10, BLACKOUT, WSM-PAUSE-05 | 12 |
| `SerialTransportTests` | SER-01..22 | 29 |
| `TelemetryJsonLinesTests` | TELJ-01..06 | 6 |
| `GameFlowEncounterTests` | FLOW-17..26 | 10 |
| `EncounterDirectorTests` | DIR-01..08 | 8 |
| `PlayerLocomotionTests` | LOCO-01..08 | 8 |
| **Total** | 24 suites | **318** |
</details>

### Acceptance matrix (Technical Spec section G), condensed

| Area | IDs | Status |
|---|---|---|
| Conditions and sensing robustness | T01, T02, T04–T10 | **Pass (software / simulated)**; T03 real ESP32 **not run** |
| Decision behaviour | T11 stability, T12 priority, T13 determinism, T29 P1–P5 probes | **Pass** |
| Encounter mechanics | T14, T17, T18, T19, T20 | **Pass (player self-tests)**; T15 partial (angle/LOS miss cases not run); T16 60 s soak not run |
| Input, length, latency, telemetry | T21, T25, T26, T27 | T21 **partial (team-reported)**; T25, T26 **not run**; T27 **partial** |
| Quest | T22–T24 | **Not run** |
| Repository regression | T28 | **Pass** (382/382 on the final tree, 26 Oct 2026; 318/318 on `eccdb3b`; 314/314 at `5124e90`) |

Full matrix with evidence per row: [`Docs/TechnicalSpec_MilestoneStatus.md`](TechnicalSpec_MilestoneStatus.md).

### Re-verification performed for this README (22 Oct 2026)

Run on the development machine against `5124e90` with the Unity Editor closed. The harness and the firmware host test were repeated on `dc932ca` (current `main`, whose only later change is the team-roles table) with the same results; the player self-tests were not repeated, because no runtime code changed.

| Check | Result |
|---|---|
| `dotnet build -c Release --no-incremental Tools/TestHarness` then `dotnet run` | Build 0 warnings / 0 errors; **24 suites, 314 PASS, 0 FAIL**, exit 0 (historical: this table row is the 22 Oct run on `5124e90`) |
| Firmware host test (g++ 13.2, C++17) | **47 checks, 0 failed** |
| `Builds/M2_Windows/PHYS-XR.exe -physxr-m2-selftest` (headless) | **23 passed, 0 failed** |
| `… -physxr-selftest -physxr-condition SIMULATED` (headless) | **36 passed, 0 failed** |
| `… -physxr-capture` (windowed) | 12 screenshots, `CAPTURE END`, no exceptions |
| `python Tools/analyze_telemetry.py` on the SIMULATED run | Row below |

The analyzer's row for that SIMULATED self-test run is real output. It is **scripted, simulated input in a headless player**, so it is not an experimental result. The fps figures are meaningless in `-nographics` mode:

```text
condition=SIMULATED outcome=restart duration_s=51.2 deaths=1 attempts=12 decisions=37
share_FOLLOW=0.49 share_WARN=0.241 share_RETREAT=0.153 share_ATTACK=0.116
packets_rx=549 packets_rejected=0 packets_lost=0 event_results=ACCEPTED=2;DUPLICATE=4
caused_decisions=2 caused_joined=0 inappropriate_attacks=0 telemetry_dropped=0
```

`caused_joined=0` is a real finding, explained in [issue L6](TechnicalSpec_MilestoneStatus.md#open-issues).

### Re-verification, 26 Oct 2026

Run on copies of each tree (the repository's own build folders were not touched). Raw outputs: [`Evidence/Logs/2026-10-26_post-fix/`](../Evidence/Logs/2026-10-26_post-fix).

| Tree | Harness | Firmware host test |
|---|---|---|
| Committed `eccdb3b` (the content of `main` at merge `fd1c047`) | 24 suites, **318 passed, 0 failed**, exit 0 | **47 checks, 0 failed** |
| Working tree before the telegraph fix (with `SensorResponseProfileTests` 43 and `SensorPanelModelTests` 13) | 26 suites, **375 passed, 0 failed**, exit 0 | **53 checks, 0 failed** |
| **Final tree (telegraph fix + 7 regression tests)** | 26 suites, **382 passed, 0 failed**, exit 0 | **53 checks, 0 failed** |
| Final tests against the previous telegraph logic (mutation check) | 375 passed, **7 failed** (exactly ENC-TG1..4 and E6-10..12), exit 7 | — |

Windows player build of the final tree (`Builds/PHYSXR_Windows_Final`, `M2SceneSetup.BuildWindows`: Build Finished, Result: Success, 0 errors), headless:

| Self-test | Result |
|---|---|
| `-physxr-responsiveness-selftest -physxr-condition SIMULATED` | **24 passed, 0 failed** (Q20: VERY_NEAR escalation closes the ghost to 3.97 m; Q03: sensor input alone never attacks; Q14: E6 attacks only after a 3.1 s warning) |
| `-physxr-m2-selftest` | **23 passed, 0 failed** |
| `-physxr-selftest` | **40 passed, 0 failed** |
| `-physxr-selftest -physxr-condition SIMULATED` | **42 passed, 0 failed** |

Not re-run: the ESP32 firmware compile (`arduino-cli` is not installed on this machine; last result 21 Oct, 0 warnings).

**The telegraph defect.** Before the fix, E3 and E6 required only that the previous decision had been WARN for 1.5 s, whatever rule produced it. A scratch program driving the real decision classes showed:

- **Committed code:** an R4 WARN from NEAR/LOW sensor input followed by a sprint into reach was attacked 1.0 s after the player entered the warn band, instead of 1.5 s.
- **Working tree:** a held X2 WARN let E6 attack the moment the player came within 2 m.

The fix adds an encounter-specific telegraph (see [`GHOST_DECISION_SYSTEM.md`](GHOST_DECISION_SYSTEM.md#encounter-layer-game-arbitration)).

### Live station sessions, 22-24 Oct 2026

Recomputed from the raw telemetry in [`Evidence/Logs/telemetry/trials/`](../Evidence/Logs/telemetry/trials) by `python Evidence/summarize_telemetry.py`. Definitions and the "15 vs 18" reconciliation are in [`Evidence/README.md`](../Evidence/README.md#reconciling-15-and-18). These runs used the game's sensor-response profile.

| Measure | Value |
|---|---|
| Live physical runs / total time | 15 runs (9 trial IDs) / 48.1 min, on 22, 23 and 24 Oct, USB serial |
| Station identity | `#HELLO proto=1;fw=1.0.0;dev=phys-xr-01;…;sda=21;scl=23;mpu=0x68;tof=0x29` (first logged 21 Oct) |
| State lines received / rejected / lost (sequence gaps) | 29,896 / 4 / 4 |
| Knock events accepted / repeated copies ignored | 131 / 262 |
| `PHYS_EVENT_OUTCOME` | 95 `ACTED`, 28 `NOT_QUALIFIED`, 2 `OUTRANKED` |
| Decisions carrying `cause_evt` | 70, of which 44 are followed by a `BEHAVIOUR START` for the same decision |
| ATTACK decisions / with a physical cause | 48 / 0 |

The telemetry shows the firmware, the USB link, validation, event de-duplication, attribution and behaviour execution working with the real station, and knocks producing executed ghost behaviours (mostly X3 INVESTIGATE, otherwise R5 WARN). `BEHAVIOUR START` means the motion controller began the behaviour. The hardware acceptance and the Meta Quest 2 and Quest 3 demonstrations are team-confirmed (26 Oct 2026); no recording of them is stored in the repository. Device-to-screen latency has not been measured.

### Planned experiment: virtual-only vs. physical-plus-virtual

```mermaid
flowchart TD
    P["expected-action table<br/>written before trials"] --> A["A: VIRTUAL_ONLY<br/>station on, masked"]
    P --> B["B: PHYSICAL_PLUS_VIRTUAL<br/>station live"]
    A --> R["repeated trials<br/>same cues, counterbalanced"]
    B --> R
    R --> L[("jsonl + video")]
    L --> AN["analyzer + latency script"]
    AN --> M["latency, reliability,<br/>correctness, clarity"]
```

<sub>The **masked** baseline is what makes the comparison fair: in condition A the station is plugged in and logged, so the only difference is whether its state reaches the agent.</sub>

**Status: planned. No trial has been run and nothing below has been measured.**

<details>
<summary><b>Protocol</b>: expected-action table, conditions, per-hop timestamps T0–T4, reliability, repeats, human measures</summary>

1. **Expected-action table.** For each scripted cue (NEAR+knock outside the zone, LOW-confidence NEAR, knock with nobody near, VERY_NEAR inside the zone, cable pull), write the expected `GhostAction` from the rule tables above before running trials. Decision correctness = fraction of cues where the logged `DECISION` matches.
2. **Conditions.** A and B as above. SIMULATED is a pilot condition only and is reported separately.
3. **Timestamps.** Each layer has its own clock, so latency is computed per hop, never by subtracting across clocks:
   - **T0** `tEvent`: device ms when the event qualified (ESP32 clock).
   - **T1** `ts` in the line: device ms when the line was formatted (ESP32 clock). T1 − T0 is the on-device latency.
   - **T2** `t2`: Unity arrival, `SensorService` stopwatch. Transit = T2 − T1 − offset, with the offset estimated as the minimum over the run (the pipeline already keeps a 50-line window).
   - **T3**: telemetry `ts` of the `DECISION` with `cause_evt`, on the trial clock. The `PHYS_EVENT` record carries both `ts` and `t2`, which gives the per-trial offset between the arrival and trial clocks.
   - **T4**: `BEHAVIOUR event=START` for the same `dec`; the visible response is confirmed by video (T26).
4. **Reliability.** `packets_lost` (sequence gaps), `packets_rejected` by reason, stale/unavailable time share from `SOURCE_STATUS`, duplicate ratio.
5. **Repeats.** At least the 20-tap protocol of checklist D8 per condition, plus full runs. Report the median and spread; do not average away outliers.
6. **Human measures.** Perceived responsiveness and interaction clarity by questionnaire with first-time players (T25).
7. **Limitations to report.** A single station, a single room, an authored policy, and a small sample.

</details>

## Telemetry and dashboard

### What is recorded

`TelemetryService` writes `%USERPROFILE%\AppData\LocalLow\DefaultCompany\PHYS_XR\telemetry\<trial>_<platform>_<utc>.jsonl` plus a `.trial.json` header (condition, platform, build, settings hash, UTC start). `ts` is milliseconds since trial start. The `JsonLinesTelemetrySink` enqueues only; a writer thread flushes at least every second, and drops are counted (`TELEMETRY_DROPPED`).

| Group | Record types | Source | Live / simulated / historical |
|---|---|---|---|
| Link and packets | `SOURCE_STATUS`, `SERIAL_LINK`, `SERIAL_COUNTERS`, `SERIAL_LEGACY_FIRMWARE`, `PKT_RX` (`boot seq t1_dev t2 age gap presence confidence`), `PKT_REJECT` (`reason`), `ESP32_REBOOT` | `SensorService`, pipeline | Whatever the condition feeds: SIMULATED and VIRTUAL_ONLY are labelled in every record (`cond`) |
| Station diagnostics | `STATION_LOG` (`#HELLO`/`#CAL`/`#ERR`), `SENSOR_DIAG` (every 5 s) | `StationDiagnostics` | Hardware only (none recorded yet) |
| Events | `PHYS_EVENT` (`evt result boot t2`), `PHYS_EVENT_DROPPED`, `PHYS_EVENT_OUTCOME` (`ACTED` / `OUTRANKED` / `NOT_QUALIFIED`, one per event reaching the decision layer, since `1978b09`) | Pipeline, `GameFlowController` | As above |
| Decisions and behaviour | `DECISION`, `BEHAVIOUR` (START/PREEMPTED/INTERRUPT/RECOVER), `ATTACK`, `DEFENSE` | Ghost pipeline, `GhostMotionController`, lantern | — |
| Game | `FLOW`, `OBJECTIVE`, `CLUE`, `RUN_END`, `PERF` | Game | — |

### Dashboard: current state

**There is no dashboard (Planned).** An untracked firmware prototype (`Firmware/physxr-unified/`, an on-board Wi-Fi access point serving `/api`) exists in the development tree but has not been built, run or tested. The live views are in the game itself: the menu's station panel, the HUD sensor chip, and the F1 overlay. Telemetry is written to disk and summarised after the run by [`Tools/analyze_telemetry.py`](../Tools/analyze_telemetry.py) (one CSV row per file). Any future dashboard must read the telemetry stream or file, or a new local feed published by the game. It must not open the COM port the game already owns.

### Metrics available today

From `analyze_telemetry.py`: outcome, duration, deaths, attempts, time share per action, decision count, packets received / rejected (by reason) / lost (sequence gaps), event results, decisions caused by events and their join to `PHYS_EVENT`+`BEHAVIOUR START` (T27), inappropriate attacks (ATTACK directly after PROTECT; must be 0), mean and min fps, and Unity-side latency (median, max, n in ms): accepted event → the decision that acted on it (its caused `DECISION`, or `PHYS_EVENT_OUTCOME ACTED` when it re-armed a running search) → the visible response (`BEHAVIOUR RESEARCH evt=N`, else that decision's `START`/`ESCALATE`) (`evt_to_*`; `researches` counts re-armed searches), and a presence change in `PKT_RX` → the next sensor-driven decision within 1 s → its `BEHAVIOUR` (`presence_*`). Each adds the line's queue time (`q=` in `PKT_RX`/`PHYS_EVENT`, arrival on the reader thread to processing, same clock) to the telemetry-time gap. Device → PC transit is not included (no shared epoch), and logs written before `q=` existed report n = 0. `not_shown_dormant` counts decisions the dormant ghost could not show.
