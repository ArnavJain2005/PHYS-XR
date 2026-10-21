# The Ghost decision system

The complete reference for how The Hollow decides what to do. The [main README](../README.md#the-ghost-decision-system) carries the summary: the two-layer design, the R1–R8 overview and the safety property. This file holds every input, threshold, reason string, timing and test. Source: [`RuleBasedDecisionProvider.cs`](../Assets/PHYSXR/Decision/RuleBasedDecisionProvider.cs), [`EncounterDecisionLayer.cs`](../Assets/PHYSXR/Decision/EncounterDecisionLayer.cs), [`GhostMotionController.cs`](../Assets/PHYSXR/Ghost/Presentation/GhostMotionController.cs).

The Hollow is an autonomous agent with a fixed, inspectable policy. This section documents it exactly as implemented in [`RuleBasedDecisionProvider.cs`](../Assets/PHYSXR/Decision/RuleBasedDecisionProvider.cs) and [`EncounterDecisionLayer.cs`](../Assets/PHYSXR/Decision/EncounterDecisionLayer.cs).

## Decision cycle

Every rendered frame while the run is `Playing`:

1. `SensorService.Update` (execution order −200) pumps queued lines through the pipeline.
2. `GameFlowController` bridges source status and accepted events into `WorldStateManager`. While the run is not `Playing`, events are dropped and logged `PHYS_EVENT_DROPPED`.
3. `EncounterDirector` updates threat level, beats, distance bands and restart grace. `GhostMotionController` reports phase and cooldown.
4. `WorldStateManager` builds a `WorldState`.
5. `GhostRuntimePipeline` checks the terminal gate, then calls `EncounterDecisionLayer.Decide`. That layer **always** evaluates the inner R1–R8 provider first, so the inner hysteresis history stays consistent, and then arbitrates.
6. The chosen `GhostAction` becomes a `GhostExecutionState`, which `GhostMotionController` turns into movement, pose and sound.
7. If the action, the rule ID or the reason changed since the last record (numbers inside the reason are ignored), one `DECISION` telemetry record is written (`dec`, `rule`, `reason`, `cause_evt`). Unchanged decisions are not logged; see [Explainability and telemetry](#explainability-and-telemetry).

Both layers use an injected **decision clock**: accumulated, scaled play time in ms (fix P1). Hysteresis therefore keeps counting while sensors are silent and stops while the game is paused.

```mermaid
flowchart TD
    WS[WorldState] --> IN["R1-R8, first match"] --> HY["inner hysteresis, 1.5 s"] --> G0
    G0["G0 run terminal"] -->|yes| o0([inner result])
    G0 -->|no| E0["E0 lantern hit"]
    E0 -->|yes| o1([RETREAT])
    E0 -->|no| E1["E1 attack phase running"]
    E1 -->|yes| o2([ATTACK continues])
    E1 -->|no| E2["E2 PROTECT cap: 6 s, then FOLLOW"]
    E2 --> S["S inner PROTECT or RETREAT"]
    S -->|yes| o3([safety action])
    S -->|no| W["W scripted beat"]
    W -->|yes| o4([WARN])
    W -->|no| E5["E5 first sighting"]
    E5 -->|yes| o5([ATTACK])
    E5 -->|no| E3["E3 within 2 m, warned 1.5 s"]
    E3 -->|yes| o6([ATTACK])
    E3 -->|no| E4["E4 in warn band"]
    E4 -->|yes| o7([WARN])
    E4 -->|no| o8([inner result])
    o7 --> DW["DWELL holds an E4 WARN 1.5 s"]
    o8 --> DW
```

<sub>The two-layer decision pipeline. Read top to bottom: the first matching box decides. No sensor value appears below the inner provider, so physical input can only reach E-rules through the inner result (R1 PROTECT, which S passes through and which blocks E3).</sub>

## Inputs the rules read

| Source | Field | Read by |
|---|---|---|
| Physical (semantic, gated) | `presence`, `confidence`, `disturbance` | R1, R3, R4, R5, R6, R7 |
| Player | `inThreatZone` (enter 3.5 m / exit 4.5 m) | R1–R7 |
| Player | `distanceToGhost`, `inWarnBand` (6.0 / 7.5 m) | E3, E4 |
| Virtual | `threatLevel` (0.2 baseline · 0.5 near the ghost after the fuse · 0.8 for 3 s after the surge) | R2, R6, R7 |
| Virtual | `eventActive`, `mandatoryWarningBeat` (4 s beats) | R6, W |
| Virtual | `encounterCommitted`, `attackGraceActive` (3 s), `firstSightingAttack` (3 s one-shot window), `gameplayTerminal` | E3, E4, E5, G0 |
| Ghost | `phase`, `attackCooldownMs` | E0, E1, E3, E5 |

`PlayerState.movement`, `GhostState.currentState` and `PhysicalState.timestamp` exist in the contract, but the rules deliberately do not read them.

## Inner rules R1–R8 (first match wins)

Defaults: `ElevatedThreatThreshold = 0.4`, `CriticalThreatThreshold = 0.7`. These are constructor parameters and are expected to be recalibrated.

| Rule | Condition | Action | Reason string (verbatim) |
|---|---|---|---|
| R1 | in threat zone ∧ presence VERY_NEAR ∧ confidence HIGH | **PROTECT** | `PROTECT: player at risk + high-confidence VERY_NEAR physical presence` |
| R2 | threat ≥ 0.7 ∧ not in threat zone | **RETREAT** | `RETREAT: sufficiently high threat without active player-risk condition` |
| R3 | presence NEAR/VERY_NEAR ∧ disturbance ∧ confidence HIGH ∧ not in zone | **INVESTIGATE** | `INVESTIGATE: qualified physical disturbance detected` |
| R4 | presence NEAR ∧ confidence LOW ∧ not in zone | **WARN** | `WARN: physical condition detected with low confidence` |
| R5 | disturbance ∧ presence NONE ∧ not in zone | **WARN** | `WARN: disturbance detected without proximity confirmation` |
| R6 | virtual event active ∧ presence NONE ∧ not in zone ∧ threat < 0.7 | **WARN** | `WARN: virtual event detected` |
| R7 | 0.4 ≤ threat < 0.7 ∧ not in zone ∧ presence NONE | **WARN** | `WARN: elevated virtual threat` |
| R8 | otherwise | **FOLLOW** | `FOLLOW: no active threat or event requiring intervention` |

Missing inputs fall back safely: a null physical state reads as NONE / LOW / no disturbance, a null player as outside the zone, and a null virtual state as threat 0 with no event. The result is FOLLOW.

## Hysteresis (anti-chatter)

Applied after R1–R8 to every candidate, with `MinimumActionDwellMs = 1500`:

| # | Situation | Policy |
|---|---|---|
| 0 | First decision (or after `Reset`) | Apply immediately |
| 1 | Candidate is PROTECT or RETREAT | **Apply immediately**: safety escalation is never delayed |
| 2 | Previous is PROTECT/RETREAT, candidate is not | Hold the previous action until 1.5 s dwell |
| 3 | Candidate FOLLOW, previous not FOLLOW | Hold until 1.5 s dwell (no flicker back to idle) |
| 4 | Anything else (FOLLOW→WARN, WARN↔INVESTIGATE, repeats) | Apply immediately |

A held decision is explained as `HYSTERESIS: retaining <prev>; candidate <cand> suppressed until minimum dwell elapsed (elapsed=…ms, required=1500ms).`

## Encounter layer (game arbitration)

| Rule | Condition | Result | Purpose |
|---|---|---|---|
| G0 | `gameplayTerminal` | inner result (pipeline gate also stops execution) | Nothing overrides a finished run |
| E0 | ghost phase INTERRUPTED (lantern hit) | RETREAT | The player's counter works |
| E1 | phase WINDUP / STRIKE / RECOVER | ATTACK | A started attack finishes its bounded window |
| E2 | inner PROTECT has lasted 6 s, or a 10 s refractory is active | inner treated as FOLLOW | PROTECT cannot be held indefinitely |
| S | effective inner PROTECT or RETREAT | inner result | Genuine safety responses win over scripting |
| W | mandatory scripted warning beat | WARN | Authored story beats (first sighting, fuse pickup) |
| E5 | first clear sighting, no restart grace, cooldown 0 | ATTACK | One telegraphed lunge per attempt (non-lethal before the encounter is committed) |
| E3 | committed, no grace, distance ≤ 2.0 m, previous WARN held ≥ 1.5 s, cooldown 0 | ATTACK | Attacks are always telegraphed |
| E4 | committed, player in warn band, inner FOLLOW/WARN | WARN | Close pressure during the final encounter |
| DWELL | an E4 WARN held < 1.5 s would drop to FOLLOW | keep WARN | Prevents flicker at the band edge |

Decisions recorded on deferred questions:

- A scripted beat does not stop an attack in progress (E1 ranks above W), but no new attack starts during a beat (W ranks above E3 and E5).
- **No sensor value appears in E1–E5.** Physical input can only *prevent* an attack (R1 → S), never cause one. SER-18h floods VERY_NEAR/HIGH plus events with the player at 1.2 m and verifies there is no ATTACK.

## Ghost attack-phase state machine

```mermaid
stateDiagram-v2
    [*] --> NORMAL
    NORMAL --> WINDUP: ATTACK decided (E3 or E5)
    WINDUP --> STRIKE: after 1.2 s, lunge at 3.5 m/s
    STRIKE --> RECOVER: after 0.3 s, hit if within 1.6 m and 60 deg
    RECOVER --> NORMAL: after 2 s, 6 s cooldown starts
    NORMAL --> INTERRUPTED: lantern hit
    WINDUP --> INTERRUPTED: lantern hit
    RECOVER --> INTERRUPTED: lantern hit
    INTERRUPTED --> NORMAL: 3 s stagger + 2.5 s retreat, cooldown at least 6 s
    note right of STRIKE
        TryInterrupt is refused during STRIKE
    end note
```

<sub>`GhostMotionController` owns these phases and reports them back through `GhostState.phase`, where E0/E1 read them. A strike before the encounter is committed is a non-lethal feint (`ATTACK event=STRIKE_FEINT`).</sub>

## How physical input changes the agent's behaviour

The same physical facts lead to different decisions depending on context. This table matches [`Docs/HARDWARE_SERIAL.md`](HARDWARE_SERIAL.md) §2 and the simulator hotkeys:

| Physical situation | Semantic input | Context required | Rule → action | Visible / audible response | Simulator keys |
|---|---|---|---|---|---|
| Someone near, both sensors agree, then a knock | NEAR/HIGH + event | player outside 3.5 m zone | R3 → **INVESTIGATE** | Ghost walks to the anchor nearest the player and lingers; thud | `2`, then `5` |
| Someone near, sensors disagree | NEAR/LOW | outside zone | R4 → **WARN** | Ghost stops, faces the player; a light flickers | `2`, `4` |
| Knock with nobody near | NONE + event | outside zone | R5 → **WARN** | As above | `1`, then `5` |
| Someone very close while the player is near the ghost | VERY_NEAR/HIGH | player inside 3.5 m zone | R1 → **PROTECT** (`S`) | Ghost guards the objective anchor; hum; lights hold steady | `3` |
| Same knock while the player is **inside** the zone | NEAR/HIGH + event | inside zone | none of R3–R5 apply → FOLLOW/WARN from virtual rules | No reaction to the knock | — |
| Station unplugged | no lines → STALE → UNAVAILABLE | any | physical neutral; R6–R8 only | Ghost behaves as in virtual-only mode | `9` |
| Any input in **Virtual only** condition | masked | any | physical neutral | No reaction; lines still logged | — |
| Knock while paused | event accepted by pipeline, dropped by world | run not Playing | none | No delayed reaction after resume (fix F1) | `Esc`, `5` |

## Explainability and telemetry

- **Live, every frame:** the F1 overlay shows condition, source freshness, the current decision line (action, `[rule]`, reason) and ghost phase.
- **Logged, on change only:** the game runs `GhostRuntimePipeline` with `ChangeOnlyTelemetry = true` ([`GhostRuntime.cs`](../Assets/PHYSXR/Ghost/GhostRuntime.cs)). A `DECISION` record is written only when the action, the rule ID or the reason differs from the last record. Numbers inside the reason (for example a hysteresis countdown) are ignored, so a held decision is not re-logged every frame. Each record has a sequential `dec` ID, the `rule` (`INNER`, `S`, `W`, `E0`–`E5`, `E2`, `DWELL`, `G0`), the verbatim `reason`, the previous action, and `cause_evt` when an accepted physical event caused it. `BEHAVIOUR` and `ATTACK` records reference the same `dec`.
- **Other mode:** in its default mode (used by some unit tests, not by the game) the pipeline writes four stage records on every evaluation instead.
- **Gap:** a decision can be logged with `cause_evt` while no `BEHAVIOUR START` follows, for example while the ghost is still dormant. Joining an event to a *visible* behaviour in a logged trial is still open (issue L6 in [`TechnicalSpec_MilestoneStatus.md`](TechnicalSpec_MilestoneStatus.md#open-issues)).

A real record from the 22 Oct SIMULATED self-test run:

```json
{"ts":13575,"type":"DECISION","trial":"selftest","cond":"SIMULATED","plat":"Windows","attempt":1,"frame":656,"action":"INVESTIGATE","rule":"INNER","reason":"INVESTIGATE: qualified physical disturbance detected","dec":2,"cause_evt":2,"rel":2,"detail":"prev=FOLLOW"}
```

## Determinism versus learning

The policy has no randomness, no learned parameters and no wall-clock reads; time comes from an injected clock. The same input sequence gives the same output every time, and TELJ-06 checks this with telemetry on and off. That makes it easy to explain, test and demonstrate, but it does not **adapt**. Variety in play comes from the *inputs* (player position, sensor state, objective stage), not from learning. A learned policy could replace it behind `IDecisionProvider` ([Phase 6](TechnicalSpec_MilestoneStatus.md#phase-6--optional-research-extensions)).

## Testing the decision system

| Suite | What it pins down |
|---|---|
| `RuleBasedDecisionProviderTests` (DEC) | Each of R1–R8 fires, does not fire, and priority between them |
| `RuleBasedDecisionProviderHysteresisTests` (HYST), `…TemporalContextTests` (PDC), `DecisionHistoryTests` (DH) | Dwell policy rules 0–4, history bookkeeping |
| `EncounterRuleTests` (ENC), `EncounterDecisionLayerTests` (WB), `TerminalGateTests` (TERM) | E0–E5, W, S, DWELL, priority, reset, T11 stability, T14 interruption, terminal gate |
| `DefectRegressionTests` (P1-*) | Decision clock keeps hysteresis moving while sensors are silent |
| `SensorScenarioTests` (SENS) | Packet → decision for T01–T10 plus pause (WSM-PAUSE-05) |
| `SerialTransportTests` SER-18 | Serial → R1–R8 end to end, serial/simulator parity, no ATTACK from sensors |
| `EncounterSelfTest` (player) | The same rules acting on the real ghost, lantern, death and checkpoint |

## Behaviour showcase (implemented behaviours only)

| Action | Execution state | Movement | How a viewer recognises it | Typical trigger |
|---|---|---|---|---|
| FOLLOW | Following | Trails 6–9 m behind at 1.4 m/s; closes to 4 m at 2.0 m/s during the encounter | Distant figure keeping pace | R8 default |
| WARN | Warning | Stops and faces the player; approaches at 1.9 m/s during the encounter | Turns to stare; a light flickers | R4–R7, W beats, E4 |
| INVESTIGATE | Investigating | Walks (2.0 m/s) to the anchor nearest the player, lingers 2 s | Leaves the player and searches a spot | R3 (physical) |
| PROTECT | Protecting | Guards the anchor for the current objective | Hum; lights held steady | R1 (physical) |
| RETREAT | Retreating | Moves to the farthest anchor at 3.0 m/s | Recoils with a hiss | R2 (surge), E0 (lantern) |
| ATTACK | Attacking | WINDUP 1.2 s lunge → STRIKE 0.3 s → RECOVER 2 s | Wind-up sound and pose, red pulse on hit | E5, E3 (never sensors) |
