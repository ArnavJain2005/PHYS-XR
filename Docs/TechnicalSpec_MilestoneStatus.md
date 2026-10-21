# PHYS-XR: The Presence — Milestone Status (Technical Spec addendum)

This file records milestone status against the **Technical Implementation Specification**
(`Technical Spec Doc.pdf`, consistency-reviewed 20 Oct 2026, review commit `5aa9642`) and
**GDD v1.1**. The spec PDF lives outside this repository, so its status tables (section G
"Status" columns, section H milestones) are updated here. A status is claimed only where
there is evidence, and the evidence is named.

Branch: `demo/warning-beat-arbitration`. Unity `6000.5.8f1`, URP 17.5.0, Input System 1.20.0.
Last updated 21 Oct 2026 (game implementation pass on top of `d9ef09d`; gameplay bug-fix pass `1b7a475` with team-reported manual acceptance; ESP32 USB-serial integration).

## Milestone summary

| Milestone | Status | Evidence / what is missing |
|---|---|---|
| M0 Baseline | Partial | Harness committed; asset register `Docs/ASSETS.md`. **Not done:** Git LFS rules. The Haunted Jaunt kit and Mixamo character were deliberately not imported (licences unconfirmed, no LFS); all art and audio are procedural. |
| M1 Repo + fixes | **Done, except asmdefs** | P1–P5 fixed, each with regression tests (`DefectRegressionTests`, 26). Main-thread hand-off (`UDPReceiver` queue mode, `SensorFeed`). Encounter layer complete (`EncounterRuleTests`, 21). **Not done:** asmdefs, the `GhostExecutionState` move to Core, `TestRunnerBridge`. |
| M2 Desktop loop | Done (automated) | `M2RouteSelfTest` 23/23 in the final build. |
| M3 Ghost, virtual-only | Done (automated) | `GhostMotionController` + `HollowVisual` on a runtime NavMesh; FOLLOW/WARN/surge RETREAT; DECISION/BEHAVIOUR telemetry; dev overlay (F1). |
| M4 Attack, lantern, death, checkpoint | Done (automated) | `EncounterSelfTest` 34/34 (T14, T15, T17, T18, T20, plus the bug-fix pass: readable modal, sprint/jump, first-sighting E5, solid ghost body). |
| M5 Simulated physical input | Done in software | `SimulatedSensorStation` → real validator/pipeline; hotkeys; `SensorScenarioTests` 11/11; encounter self-test under SIMULATED 36/36 (34 plus the F1 pause checks P01/P02). |
| M6 Telemetry + analysis | Done in software | `JsonLinesTelemetrySink`, all section G record types, `Tools/analyze_telemetry.py`, T13 determinism test. |
| M7 ESP32 on Windows | **Software done; hardware run pending** | Transport is USB serial (the project's link; final report §3.3), UDP kept. I-1 addressed in software: `Firmware/physxr_station` emits contract v1 at 115200 baud (host test 47/47; ESP32 compile with the real Pololu VL53L0X 1.3.1, 0 warnings). `SerialLineReceiver` → same `SensorFeed`/validator/pipeline; `SerialTransportTests` 29/29. A player in USB-serial mode against the real board (COM6, old bench sketch still flashed) passed 34/34 with 0 packets accepted and 0 physical decisions. **Not run with `physxr_station` on the physical station.** Checklist: [`HARDWARE_ACCEPTANCE.md`](HARDWARE_ACCEPTANCE.md). |
| M8 / M9 Quest | Not started | Desktop only (I-2, I-3). |
| M10 Art, audio, ending | Done with procedural assets | House dressing, lighting states, storm, post-processing, 39 synthesised cues, ending with window silhouette and subtitles. Reviewed from screenshots (capture tour), not in person. |
| M11 Acceptance + evaluation | Not started | Needs people and hardware. |

## What was built (this pass)

| Spec item | Implementation |
|---|---|
| P1 decision clock | `RuleBasedDecisionProvider(decisionClock:)`, `EncounterDecisionLayer(inner, clock)`, `GhostRuntimePipeline.CreateRuntimeChain(clock, …)`. The game passes accumulated scaled play time. |
| P2 neutral physical state | `WorldStateManager(physical, clock)`: neutral unless FRESH and not virtual-only; 3 s disturbance pulse from accepted events. |
| P3 boot identity | `PacketValidator`: per-boot ordering, `OLD_BOOT`, `isNewBoot`; contract v1 keys `v`, `dev`, `boot`, `tEvent`. |
| P4 dedup key | `PhysicalEventManager`: `(boot, eventId)`, plus `OLD_EVENT`. |
| P5 arrival-time freshness | `PhysicalCommunicationPipeline.Process(raw, arrivalMs)` and `GetSourceStatus(now)`; resync, max event age, restart blackout, masking. |
| Encounter rules E0–E5 | `EncounterDecisionLayer` (see README table). E5 = one-shot first-sighting attack (gameplay bug-fix pass). |
| `GhostMotionController` | `Ghost/Presentation/GhostMotionController.cs` |
| Checkpoint, death | `GameFlow.ReportDeath`, `GameFlow(ObjectiveStage)`, `GameSession` checkpoint/attempts, `GameFlowController.RestartFromCheckpoint`. |
| Lantern | `Game/Gameplay/DefensiveAbility.cs` (4 m, 45°, line of sight, 10 s / 3 s cooldowns). |
| Encounter trigger | `GameTriggerVolume` on both utility-room exits; commits only after power. |
| JSON Lines sink | `Telemetry/JsonLinesTelemetrySink.cs`, `App/TelemetryService.cs` |
| Bootstrap wiring | `App/AppBootstrap.cs`, `App/SensorService.cs`, `App/MainMenu.cs` |
| ScreenFader, GameUI | `Game/UI/GameUI.cs` (uGUI) |
| Ending | `Game/Gameplay/EndingSequence.cs` |
| `PlatformRigSpawner`, `XRInputAdapter`, `PhysxrSettings` assets | Not built (desktop only; settings are code constants and `GameSettings`). |

Deviations, recorded:
- **Dev hotkeys.** The spec's letter keys (D, S, R, X) collide with WASD, so the number row is used (1–9).
- **First-sighting attack (E5)**, added in the gameplay bug-fix pass: the first clear view of The Hollow triggers one attack before the encounter is committed. A strike that lands then is a non-lethal feint; death stays in the committed encounter (E3).
- **Sprint and jump** (desktop only): Left Shift 3.2 m/s with a 4 s stamina bar, Space 0.9 m jump. Not in the spec; added at the user's request.
- **Walk speed** is 1.8 m/s (spec 1.5) and encounter WARN approach 1.9 m/s (spec 1.6). This keeps the M2 route short while the ghost still out-paces a walking player.
- **Outer dwell.** The encounter layer adds a 1.5 s dwell for E4 WARN → FOLLOW (T11). A scripted beat still ends immediately (RT-11).
- **Deferred ATTACK vs beat question.** An attack in progress continues through a beat; no new attack starts during one.
- **Legacy APIs kept.** `PhysicalCommunicationPipeline.HandleRawPacket` and the one-argument `WorldStateManager` keep their original behaviour for their existing tests; the game uses the fixed paths.

## Test evidence

| Kind | What ran | Result |
|---|---|---|
| Standalone .NET harness (`dotnet run -c Release --project Tools/TestHarness`, C# 9, net9.0) | 24 suites | **314 passed, 0 failed** at `02cece8` plus the serial `dev=` requirement (full rebuild, `--no-incremental`; 310 at `ec4af1e`, then SER-19..22) |
| Unity compile (the open Editor, 6000.5.8f1) | Full script compile after the last change | 0 errors, 0 warnings in PHYS-XR code |
| Windows player build (from the open Editor, `BuildPipeline` with explicit scene paths) | StandaloneWindows64, Bootstrap + Game_BlackthornHouse | **Build Finished, Result: Success** |
| `-physxr-m2-selftest` (headless player) | Objective route | **23/23**, scripted route 33.5 s |
| `-physxr-selftest` (headless player) | Encounter checks | **34/34**. Next attack 11.53 s after the lantern hit; ghost body stops the player at 0.73 m (contact 0.65 m); 10 restarts: 1 camera/listener/EventSystem/flow/ghost; materials 53 → 53; managed heap 4.2 → 4.3 MB |
| `-physxr-selftest -physxr-condition SIMULATED` (headless player) | Same, simulated packets through the validator | **34/34**; telemetry: 459 packets accepted, 0 rejected; 0 inappropriate attacks. Intermittent early exits: see below |
| Repeat runs on `8bf28a6` (headless player, 21 Oct 2026, 20:56–21:10) | 1 × M2, 4 × VIRTUAL_ONLY + 4 × SIMULATED idle, then 3 + 3 alongside a looping harness | M2 23/23; **14 of 15** encounter runs 34/34; 1 VIRTUAL_ONLY run exited 1 early (see below) |
| Player logs of those three runs | Scan for exceptions and errors | None |
| `-physxr-capture` (windowed player) | 12 screenshots: menu, dark and powered rooms, first sighting, encounter, ending, end card | Reviewed; light levels and the ghost's materials tuned from them |
| `Tools/analyze_telemetry.py` on self-test logs | CSV summary | Parses; 0 inappropriate attacks, 0 dropped records |

The Editor-side Play-mode self-test was attempted, but an unfocused Editor throttles Play mode
to about 1.5 fps, so all in-engine checks were run in the Windows player instead.

### F1: physical events while paused (`ec4af1e`)

A knock that arrived during a pause used to start a disturbance pulse on the frozen game clock, so
The Hollow reacted after resume. Events are now ignored while the run is not Playing and logged as
`PHYS_EVENT_DROPPED`. Details and the audit session's own evidence: [`F1_PauseEventFix.md`](F1_PauseEventFix.md).

Independent re-check by the gameplay session (21 Oct 2026):
- harness: 24 suites, 310 passed, including WSM-PAUSE-01..05;
- fresh batch build (0 `error CS`): M2 23/23, SIMULATED 36/36 with P01/P02 passing and one
  `PHYS_EVENT_DROPPED run_state=Paused` record, default 34/34 (P01/P02 skipped by design).

The build also contained uncommitted serial-monitor additions in `SerialLineFramer.cs` and
`SerialLineReceiver.cs` (off by default, not used by these tests). Not yet checked on the ESP32
station or in manual play.

### Intermittent failures (investigated 21 Oct 2026)

**Player self-test early exit — open; most likely memory exhaustion, not proven.** Four
encounter self-test runs (VIRTUAL_ONLY and SIMULATED, on two builds: the 13:14 build and `8bf28a6`) exited with code 1
partway through. The log simply stops: no `SELFTEST FAIL`, no exception, no `SELFTEST END`, no
crash dump. The last line differs each time (after M05, S07 or T14b), so it is not one failing
check.
- 13:17, a SIMULATED run by the gameplay session: no event in the Windows logs.
- 19:35, the ESP32 session's two runs: Windows logged *"Your system is low on virtual memory"*
  (Application Popup 26) at 19:35:11. The two players died at 19:35:18 and 19:35:40, and the
  Unity Editor hung (Application Hang 1002) at 19:35:59.
- 20:56, reproduced in 1 of 15 repeat runs: it happened at the lowest free memory of the series,
  4.5 GB free commit and 1.1 GB free RAM. About 31 GB of commit was in use on a 15.7 GB machine:
  the Editor and two build workers (about 8 GB), Blender, three Claude processes and browsers.
  After the Editor was closed, free commit was about 14 GB and all 14 remaining runs passed,
  including 6 next to a looping harness (CPU load alone did not reproduce it).

No code path in the game exits with 1 except `Application.Quit(failed)` after `SELFTEST END`,
which is not reached. The tests are unchanged and still count such a run as a failure. To
re-check, run the self-tests with the Editor and other large processes closed, and record free
commit (`Get-CimInstance Win32_OperatingSystem`, `FreeVirtualMemory`) at each run.

**Harness SER-09 failure — resolved.** SER-09 failed 1 run in 5 on 21 Oct (*failed handle closed:
expected 0 but got 1*). Cause: a race in the test, not the product. `SerialLineReceiver` publishes
FAULTED inside its catch block and closes the port handle in the `finally` block just after; the
test asserted the handle count in between. Fixed in `5efd34f`: SER-09 and SER-10 now wait (3 s
limit) for the handle to reach 0, so they still require the release. Verification on `8bf28a6`:
the serial suite passed 30/30 runs on an idle machine, and the full harness passed 31/31 runs
under player load (24 suites, 305 passed).

## Acceptance matrix (section G)

| ID | Status | Evidence |
|---|---|---|
| T01 Virtual-only | **Pass (software)** | SENS-T01 |
| T02 Physical + virtual (simulated) | **Pass (software)** | SENS-T02a/b |
| T03 Real ESP32 | Not run | Firmware and serial link exist and pass software tests (SER-04, SER-18a–h); needs the station: [`HARDWARE_ACCEPTANCE.md`](HARDWARE_ACCEPTANCE.md) A–G |
| T04 Duplicate events | **Pass (software)** | SENS-T04 |
| T05 Old event ID | **Pass (software)** | SENS-T05 |
| T06 Stale packets | **Pass (software)** | SENS-T06, P5-04 |
| T07 Invalid packets | **Pass (software)** | SENS-T07 |
| T08 Out-of-order | **Pass (software)** | SENS-T08 |
| T09 Disconnection | **Pass (software)** | SENS-T09 (no real network) |
| T10 ESP32 reboot | **Pass (software)** | SENS-T10, P3/P4 tests |
| T11 Ghost state stability | **Pass (decision level)** | ENC-T11 |
| T12 Rule priority | **Pass** | EncounterRuleTests, DEC/HYST suites |
| T13 Determinism | **Pass** | TELJ-06 |
| T14 Defensive interruption | **Pass (player)** | EncounterSelfTest T14a–c, ENC-T14 |
| T15 Defensive miss | **Pass (player), partial** | Out-of-range miss, 3 s cooldown. Not run: outside-45° and blocked line-of-sight cases (the code paths exist in `DefensiveAbility.HitTest`) |
| T16 Recovery soak (60 s) | Not run | Single-cycle recovery is covered by T14c |
| T17 Death and checkpoint | **Pass (player)** | EncounterSelfTest T17a–c |
| T18 Repeated restarts | **Pass (player)** | EncounterSelfTest T18a–d. "Socket still receiving" was not checked (virtual-only run) |
| T19 Fuse and exit | **Pass (player)** | M2 C03–C07 (lights, hum loop and surge sound exist; audio not verified by ear) |
| T20 Full restart | **Pass (player)** | M2 C10a/c, EncounterSelfTest T20 |
| T21 Windows input | **Partial (team-reported manual pass)** | On `1b7a475` the team reported PASS for E open/close of readables, sprint and jump, and pause and recovery (see Manual acceptance). Not reported: mouse feel, cursor lock across focus changes, settings sliders |
| T22–T24 Quest | Not run | No Quest build |
| T25 Under five minutes | Not run | Scripted route 33.5 s; needs first-time players |
| T26 Latency | Not run | Needs hardware and video. `ts`/`tEvent` (T0/T1) and arrival `t2` are logged per line (`PKT_RX`, `PHYS_EVENT`) |
| T27 Telemetry correlation | Partial | The analyzer joins caused decisions to events and behaviours; no physical-event trial was logged yet |
| T28 Repo regression | **Pass** | 314/314 (21 Oct 2026, evening) |
| T29 Defect probes P1–P5 | **Pass** | DefectRegressionTests: each "legacy" case still shows the original defect through the old API, each "fixed" case passes |

## Manual acceptance (gameplay bug-fix pass)

Manual play-testing by the project team on commit `1b7a475` (branch
`demo/warning-beat-arbitration`, Windows desktop build). These results are **team-reported**:
they were recorded from the team's report and were not performed or observed by the automated
sessions that wrote this file. The automated checks covering the same areas are listed for
reference.

| # | Area | Team-reported result | Automated coverage of the same area |
|---|---|---|---|
| 1 | Ghost collision (the player cannot walk through The Hollow) | **PASS** | EncounterSelfTest B01 (player stops 0.73 m from the ghost's centre; contact 0.65 m) |
| 2 | Radio and letter open/close with E | **PASS** | EncounterSelfTest M01–M05 (E opens, controls blocked, E closes, controls restored, Esc/pause close) |
| 3 | First-sighting attack | **PASS** | EncounterSelfTest S04a–d; ENC-E5a–d; DIR-07/08 |
| 4 | Sprint and jump | **PASS** | EncounterSelfTest J01–J03; PlayerLocomotionTests LOCO-01..08 |
| 5 | Pause and recovery | **PASS** | M2 C09b; EncounterSelfTest M05, J03, T17a–c |

## Manual checks still needed

1. Play from the menu with keyboard and mouse: cursor lock across focus changes, mouse feel, settings sliders (T21; pause and recovery were team-reported as passing on `1b7a475`).
2. A first-time player's run time and difficulty of the encounter (T25), and the lantern timing in practice.
3. Audio balance and spatialisation by ear: ambience, footsteps, surge and hum, ghost voice, heartbeat, ending static.
4. Physical sensors: the ESP32 station over USB serial — the ordered checklist in [`HARDWARE_ACCEPTANCE.md`](HARDWARE_ACCEPTANCE.md) (ECHO divider and SCL pin first, then flashing, each sensor, USB reconnect, sensor-to-ghost responses, the F1 pause scenario, soak). UDP path: `python Tools/esp32_sim.py --scenario near` with "Physical - UDP 47800" selected; Windows Firewall may ask on first listen.
5. The in-Editor self-test menus with the Game view focused.
