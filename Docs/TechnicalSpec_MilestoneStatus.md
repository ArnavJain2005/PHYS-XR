# PHYS-XR: The Presence — Milestone Status (Technical Spec addendum)

This file records milestone status against the **Technical Implementation Specification**
(`Technical Spec Doc.pdf`, consistency-reviewed 20 Oct 2026, review commit `5aa9642`) and
**GDD v1.1**. The spec PDF lives outside this repository, so its status tables (section G
"Status" columns, section H milestones) are updated here. A status is claimed only where
there is evidence, and the evidence is named.

Branch: `demo/warning-beat-arbitration`. Unity `6000.5.8f1`, URP 17.5.0, Input System 1.20.0.
Last updated 21 Oct 2026 (game implementation pass on top of `d9ef09d`).

## Milestone summary

| Milestone | Status | Evidence / what is missing |
|---|---|---|
| M0 Baseline | Partial | Harness committed; asset register `Docs/ASSETS.md`. **Not done:** Git LFS rules. The Haunted Jaunt kit and Mixamo character were deliberately not imported (licences unconfirmed, no LFS); all art and audio are procedural. |
| M1 Repo + fixes | **Done, except asmdefs** | P1–P5 fixed, each with regression tests (`DefectRegressionTests`, 26). Main-thread hand-off (`UDPReceiver` queue mode, `SensorFeed`). Encounter layer complete (`EncounterRuleTests`, 21). **Not done:** asmdefs, the `GhostExecutionState` move to Core, `TestRunnerBridge`. |
| M2 Desktop loop | Done (automated) | `M2RouteSelfTest` 23/23 in the final build. |
| M3 Ghost, virtual-only | Done (automated) | `GhostMotionController` + `HollowVisual` on a runtime NavMesh; FOLLOW/WARN/surge RETREAT; DECISION/BEHAVIOUR telemetry; dev overlay (F1). |
| M4 Attack, lantern, death, checkpoint | Done (automated) | `EncounterSelfTest` 34/34 (T14, T15, T17, T18, T20, plus the bug-fix pass: readable modal, sprint/jump, first-sighting E5, solid ghost body). |
| M5 Simulated physical input | Done in software | `SimulatedSensorStation` → real validator/pipeline; hotkeys; `SensorScenarioTests` 11/11; encounter self-test under SIMULATED 34/34. |
| M6 Telemetry + analysis | Done in software | `JsonLinesTelemetrySink`, all section G record types, `Tools/analyze_telemetry.py`, T13 determinism test. |
| M7 ESP32 on Windows | **Software done; hardware run pending** | Transport is USB serial (the project's link; final report §3.3), UDP kept. I-1 addressed in software: `Firmware/physxr_station` emits contract v1 at 115200 baud (host test 47/47; ESP32 compile clean against a VL53L0X API stub). `SerialLineReceiver` → same `SensorFeed`/validator/pipeline; `SerialTransportTests` 25/25. **Not run on the physical station** (no ESP32 connected). Checklist: `Docs/HARDWARE_SERIAL.md` §7. |
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
| Standalone .NET harness (`dotnet run -c Release --project Tools/TestHarness`, C# 9, net9.0) | 24 suites | **305 passed, 0 failed** (gameplay bug-fix pass, 21 Oct 2026; includes the serial suite) |
| Unity compile (the open Editor, 6000.5.8f1) | Full script compile after the last change | 0 errors, 0 warnings in PHYS-XR code |
| Windows player build (from the open Editor, `BuildPipeline` with explicit scene paths) | StandaloneWindows64, Bootstrap + Game_BlackthornHouse | **Build Finished, Result: Success** |
| `-physxr-m2-selftest` (headless player) | Objective route | **23/23**, scripted route 33.5 s |
| `-physxr-selftest` (headless player) | Encounter checks | **34/34**. Next attack 11.53 s after the lantern hit; ghost body stops the player at 0.73 m (contact 0.65 m); 10 restarts: 1 camera/listener/EventSystem/flow/ghost; materials 53 → 53; managed heap 4.2 → 4.3 MB |
| `-physxr-selftest -physxr-condition SIMULATED` (headless player) | Same, simulated packets through the validator | **34/34** (three runs; one earlier run exited early with a truncated log and no failed check, not reproduced); telemetry: 459 packets accepted, 0 rejected; 0 inappropriate attacks |
| Player logs of those three runs | Scan for exceptions and errors | None |
| `-physxr-capture` (windowed player) | 12 screenshots: menu, dark and powered rooms, first sighting, encounter, ending, end card | Reviewed; light levels and the ghost's materials tuned from them |
| `Tools/analyze_telemetry.py` on self-test logs | CSV summary | Parses; 0 inappropriate attacks, 0 dropped records |

The Editor-side Play-mode self-test was attempted, but an unfocused Editor throttles Play mode
to about 1.5 fps, so all in-engine checks were run in the Windows player instead.

## Acceptance matrix (section G)

| ID | Status | Evidence |
|---|---|---|
| T01 Virtual-only | **Pass (software)** | SENS-T01 |
| T02 Physical + virtual (simulated) | **Pass (software)** | SENS-T02a/b |
| T03 Real ESP32 | Not run | Firmware and serial link exist and pass software tests (SER-04, SER-18a–h); needs the station (`Docs/HARDWARE_SERIAL.md` §7, steps 1–27) |
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
| T21 Windows input | Not run | Needs a person |
| T22–T24 Quest | Not run | No Quest build |
| T25 Under five minutes | Not run | Scripted route 33.5 s; needs first-time players |
| T26 Latency | Not run | Needs hardware and video. `ts`/`tEvent` (T0/T1) and arrival `t2` are logged per line (`PKT_RX`, `PHYS_EVENT`) |
| T27 Telemetry correlation | Partial | The analyzer joins caused decisions to events and behaviours; no physical-event trial was logged yet |
| T28 Repo regression | **Pass** | 305/305 |
| T29 Defect probes P1–P5 | **Pass** | DefectRegressionTests: each "legacy" case still shows the original defect through the old API, each "fixed" case passes |

## Manual checks still needed

1. Play from the menu with keyboard and mouse: cursor lock, mouse feel, pause/resume, settings sliders (T21).
2. A first-time player's run time and difficulty of the encounter (T25), and the lantern timing in practice.
3. Audio balance and spatialisation by ear: ambience, footsteps, surge and hum, ghost voice, heartbeat, ending static.
4. Physical sensors: the ESP32 station over USB serial — the ordered checklist in `Docs/HARDWARE_SERIAL.md` §7 (wiring, SCL pin, firmware flash, `--serial-monitor`, in-game reactions, unplug/replug, reset, calibration). UDP path: `python Tools/esp32_sim.py --scenario near` with "Physical - UDP 47800" selected; Windows Firewall may ask on first listen.
5. The in-Editor self-test menus with the Game view focused.
