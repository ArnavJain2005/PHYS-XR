# F1: physical events while the run is not Playing

Fix F1 from the ghost decision-system audit (21 Oct 2026, branch `demo/warning-beat-arbitration`).
This file records what changed and what was verified, and keeps automated evidence separate from
physical-hardware evidence. Nothing here was tested with the ESP32 station.

## Defect

The disturbance pulse is timed on the game clock (`GameClockMs`), which stops while the run is paused.
`GameFlowController.BridgeSensing` forwarded accepted events to `WorldStateManager.NotifyAcceptedEvent`
every frame, paused or not, so an event that arrived during a pause got a full 3 s pulse on the frozen
clock and played it out after resume. A knock during a five-minute pause therefore made The Hollow
INVESTIGATE (R3) or WARN (R5) on resume, minutes after the event. The pipeline's own 300 ms event-age
limit (`MaxEventAgeMs`) did not apply, because the event was fresh when it was accepted.

## Change

| File | Change |
|---|---|
| `Assets/PHYSXR/WorldState/WorldStateManager.cs` | New `EventsSuspended` flag. While set, `NotifyAcceptedEvent` starts no pulse. It now returns `bool` (true if a pulse started). A pulse already running keeps its remaining game time. |
| `Assets/PHYSXR/Game/Gameplay/GameFlowController.cs` | `BridgeSensing` sets `EventsSuspended = RunState != Playing` and logs each dropped event as `PHYS_EVENT_DROPPED evt=<id> boot=<boot> reason=NOT_PLAYING run_state=<state>`. A separate record type is used so `Tools/analyze_telemetry.py` does not count the event twice. |

Unchanged: R1-R8, the encounter layer, the decision-provider interfaces, calibration, the communication
pipeline (paused events are still validated, deduplicated and logged `PHYS_EVENT result=ACCEPTED`), and the
freshness bridging (source status still updates while paused).

## Automated verification

Run on 21 Oct 2026 against the working tree with the change, before it was committed.

| What | Result |
|---|---|
| Plain C# harness, `dotnet run -c Release --project Tools/TestHarness` | 24 suites, 0 failed (it was also 0 failed before the change) |
| New `WSM-PAUSE-01..04` (`WorldStateManagerTests`) | Pass. A suspended event starts no pulse; no late pulse after a long pause; a pulse started in play keeps its remaining 3 s of game time and is not extended or re-attributed by a paused event; unsuspended and legacy behaviour unchanged |
| New `WSM-PAUSE-05` (`SensorScenarioTests`, packet to decision with a separate game clock) | Pass. 60 s pause: the pipeline accepts the knock once, the world drops it, no pulse, FOLLOW for 5 s after resume. The next knock in play gives exactly one INVESTIGATE episode of at least 3 s |
| Mutation check (guard removed temporarily, then restored) | WSM-PAUSE-01, 02, 03 and 05 fail without the guard. 04 passes either way, as intended, because it covers the unsuspended path |
| Final harness run, after a forced full rebuild (`dotnet build -c Release --no-incremental`) | 24 suites, 0 failed, all five WSM-PAUSE cases pass. The rebuild was needed because restoring the file after the mutation check gave it an older timestamp than the mutated build, so an incremental `dotnet run` reused the mutated binary and reported 4 failures. Rerun the harness with `--no-incremental` after any manual mutation check |
| Windows player build (`M2SceneSetup.BuildWindows`, batch mode) | Build Finished, Result: Success, 0 `error CS` |
| `-physxr-selftest -physxr-condition SIMULATED` (headless player) | 36/36, including the new P01_PausedKnockIgnored and P02_KnockInPlayInvestigates |
| `-physxr-selftest` (default condition) | 34/34. P01/P02 are skipped (they need the simulated station) |
| `-physxr-m2-selftest` | 23/23 |

The SIMULATED run's telemetry shows the full sequence: `FLOW event=PAUSE`, then `PHYS_EVENT evt=1 result=ACCEPTED`,
`PHYS_EVENT_DROPPED evt=1 ... run_state=Paused`, two `DUPLICATE` repeats, `FLOW event=RESUME`, and no
decision caused by event 1. The next knock in play logs `PHYS_EVENT evt=2 result=ACCEPTED` followed by
`DECISION action=INVESTIGATE rule=INNER cause_evt=2`.

### WARN after INVESTIGATE in the self-test (benign)

In that run, one decision after the INVESTIGATE is `WARN: disturbance detected without proximity
confirmation` (R5), followed by FOLLOW about 3 s after the event. This was checked against the test and the
telemetry: P02 passes on the INVESTIGATE frame, and its cleanup then calls `SetPresence(NONE)` while the
pulse for event 2 is still running. The simulator marks its state dirty and sends `seq=113 presence=NONE`
on the next poll, 16 ms later, so R5 applies until the pulse ends (FOLLOW at ts 16430). This is the
existing R5 behaviour on the input the cleanup creates, not an effect of F1.

## Not yet verified

- **Physical hardware.** No run with the ESP32 station: pause, knock the station, resume, and expect
  `PHYS_EVENT_DROPPED` and no reaction, then a knock in play giving INVESTIGATE. This belongs with the
  checklist in [`HARDWARE_ACCEPTANCE.md`](HARDWARE_ACCEPTANCE.md), steps F9–F12.
- **Manual play.** Pause and resume with a person present was not checked by hand.

## Milestone record

`Docs/TechnicalSpec_MilestoneStatus.md` records F1 since `4c9c7c5` (section "F1: physical events while
paused"). The gameplay session repeated the automated checks there on a fresh build: harness 310/310,
SIMULATED 36/36 with P01/P02 passing, default 34/34, M2 23/23. The physical-hardware and manual-play items
above are still open; step F of [`HARDWARE_ACCEPTANCE.md`](HARDWARE_ACCEPTANCE.md) is the hardware run.
