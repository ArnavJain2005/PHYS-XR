# PHYS-XR faculty demonstration: run sheet

> **Historical document.** This run sheet was prepared on 21–22 Oct 2026 for a desktop demonstration and is kept as written. Since then the encounter rules gained E6 and the sensor-response rules X1–X3, the self-test counts changed (see [`TESTING_AND_EVALUATION.md`](TESTING_AND_EVALUATION.md)), the station was accepted (with the HC-SR04 fitted but giving no valid readings, so physical proximity arrives at LOW confidence and a knock with someone near produces X3 INVESTIGATE rather than R3), and the game was demonstrated as PC VR over Quest Link. The script below still works on Simulated sensors.

As written on 21–22 Oct: Windows desktop build only (no VR). Two parts:

1. **Tonight:** a short hardware run that decides whether the live demo uses the real ESP32 station.
2. **Tomorrow:** the demo script, written for **Simulated sensors** (the fallback), with the matching
   physical action noted where the station passes tonight.

Every reaction below comes from the game's rules (`RuleBasedDecisionProvider` R1–R8 and
`EncounterDecisionLayer` E0–E5). The automated evidence is the headless self-test under SIMULATED (36/36).
None of the hardware steps has been run yet.

---

## Part 1: hardware go/no-go (tonight, about 60–90 min)

Use the full procedure in [`HARDWARE_ACCEPTANCE.md`](HARDWARE_ACCEPTANCE.md) and record PASS or FAIL in
its Result column. This is the minimum subset that decides tomorrow. Close the Unity Editor and the
Arduino Serial Monitor before any game or harness run.

| Order | Steps | Why it matters for the demo | If it fails |
|---|---|---|---|
| 1 | A1–A4, with the **USB cable unplugged** (A1, A2 are **STOP**) | Multimeter checks before the board is powered with `physxr_station`: 5 V on GPIO 26 can damage the ESP32 (A1); the SCL pin must be known (A2); SDA and common ground (A3); 5 V supply to the HC-SR04 (A4) | Stop. Demo on SIMULATED |
| 2 | B1–B3 | The board still runs the old bench sketch, which sends nothing the game accepts | Demo on SIMULATED |
| 3 | C1, C2, C4 (**STOP**) | `#HELLO` with `dev=phys-xr-01` (required on USB serial since `e6e0ed9`), correct `scl=`, ultrasonic working | Demo on SIMULATED |
| 4 | D1, D3, D5, D8 | Presence classes change cleanly, both sensors agree (HIGH), one tap = one event | Re-calibrate (D9) if time allows, otherwise SIMULATED |
| 5 | F1, F3 | Menu `USB COM6 CONNECTED`; HUD `FRESH`; overlay `rejected 0` | SIMULATED |
| 6 | F4, F6, F7 | The three reactions you will show: INVESTIGATE, WARN, PROTECT | Show only the ones that pass; do the rest on SIMULATED |
| 7 | F9–F12 | The F1 pause fix on real hardware | Leave the pause beat out of the live demo |
| 8 | F13, F14 | Unplugging the cable must not break the run | **Do not** unplug during the demo |
| 9 | F17 | Switching to Simulated releases the port, so a mid-demo switch is safe | Quit and relaunch the game to switch instead |

**Decision rule.** Use the station live only if steps 1–6 all pass, in this game build, on the demo
PC. Otherwise run the whole demo on Simulated sensors. You lose no rule behaviour: the simulator feeds the
same validator, pipeline and rules.

Record the date, the commit (`git rev-parse --short HEAD`) and the COM port at the top of the checklist.

---

## Part 2: demo script (Simulated sensors)

### Before the audience arrives (10 min)

1. Use one fresh player build of the current commit, from `Builds/PHYSXR_Windows/PHYS-XR.exe`. Close the Editor
   and other large programs (earlier runs failed when the PC ran out of memory).
2. Launch, choose **Sensors → Simulated sensors** (or **Physical - USB serial** if Part 1 passed),
   **Begin**. Check the HUD chip reads `SIMULATED` (or `USB COM6 CONNECTED - FRESH`).
3. Press **F1** so the overlay shows. Its `decision` line (action, `[rule]`, reason) is what you point
   at: it proves the ghost is deciding, not playing a script.
4. Do one quick rehearsal of beats 3–5, then **Pause → Restart run** so the demo starts clean.

### Keys

| Key | Does |
|---|---|
| WASD, mouse | Move, look |
| Shift (hold), Space | Sprint, jump |
| E | Interact, open/close a note |
| F or right mouse | Lantern pulse |
| Esc | Pause menu |
| F1 | Developer overlay |
| **1 / 2 / 3** | Simulated presence NONE / NEAR / VERY_NEAR |
| **4** | Toggle confidence HIGH ↔ LOW (starts HIGH; press again to restore) |
| **5** | One disturbance event (a "knock") |
| **9** | Stop / resume the simulated station (link loss) |

Keys 6–8 (resend, stale packet, reboot) are fault injections. Leave them out of the demo.

### Beats

The ghost is dormant until the first sighting, so sensor beats only become visible after beat 2. Do the
sensor beats (3–6) **before picking up the fuse**. After the fuse, being within 6 m of the ghost raises the
threat level and the ghost warns on its own (rule R7), which blurs the sensor reactions.

| # | Do | Expect on screen (overlay `decision`) | Say | Physical equivalent |
|---|---|---|---|---|
| 1 | Walk into the corridor (do **not** go for the fuse yet). | Scripted beat: `WARN [W]` for 4 s. The Hollow appears at the far end. | "Some moments are authored; the overlay marks them `[W]`." | n/a |
| 2 | Look at The Hollow. | `ATTACK [E5]`: one lunge on first sight. It is a feint (cannot kill before the encounter) and happens once per run. Then `FOLLOW [INNER]`; it trails 6–9 m behind. | "First sight triggers one lunge; afterwards it follows." | n/a |
| 3 | Step **at least 5 m** away from the ghost. Press **2**, then **5**. | `INVESTIGATE [INNER]` "qualified physical disturbance detected" (R3). Thud; it walks to a nearby anchor and searches. Back to `FOLLOW` after about 3 s. | "Someone near the station plus a knock: the ghost investigates the room." | Person at NEAR, both sensors agree, tap the station |
| 4 | Press **4** (confidence LOW). | `WARN [INNER]` "physical condition detected with low confidence" (R4). It stops and faces you; a light flickers. Press **4** again (HIGH) → `FOLLOW` after about 1.5 s. | "Uncertain evidence gives a warning, not an investigation." | Cover one sensor |
| 5 | Press **1**, then **5**. | `WARN [INNER]` "disturbance detected without proximity confirmation" (R5). | "A knock with nobody near: a warning." | Nobody near, tap |
| 6 | Press **Esc** (pause). Press **5**. Wait 3 s. **Resume.** | Nothing happens after resume (fix F1). | "An event during a pause is dropped, not replayed later." | Same, tap while paused |
| 7 | Walk **towards** the ghost until you are within 3.5 m. Press **3** (VERY_NEAR). | `PROTECT [S]` "player at risk + high-confidence VERY_NEAR physical presence" (R1). Hum; the lights hold steady; it moves to its guard point. Press **1** within 5 s. | "A real person close by protects the player in the virtual house." | Person very close to the station while the player is near the ghost |
| 7b | Pick up the fuse on the kitchen table (E). | Scripted beat `WARN [W]` for 4 s. From now on the ghost also warns by itself (`WARN [INNER]` "elevated virtual threat", R7) whenever you are within 6 m of it. | "The house gets more dangerous as the objective progresses." | n/a |
| 8 | Walk to the utility-room fuse box. Stand at least 5 m from the ghost. Install the fuse (E). | Power surge: `RETREAT [S]` "sufficiently high threat ..." (R2) for about 3 s; hiss; it recoils away. Checkpoint saved. | "A severe virtual threat makes it flee." | n/a |
| 9 | Cross into the dining room (encounter commits). Let it approach. | `WARN [E4]` while it closes in (at least 1.5 s of warning), then `ATTACK [E3]` within 2 m: wind-up sound and pose. | "Attacks are always telegraphed and come only from the game, never from the sensors." | n/a |
| 10 | During the wind-up, face it within 4 m and press **F**. | `RETREAT [E0]` "ghost interrupted by defensive pulse": stagger, then retreat. The next attack waits at least about 11.5 s. | "The player's counter." | n/a |
| 11 | (Optional) let a strike land. | Death screen, then restart at the fuse box with 3 s of grace. | "Checkpointing and a clean reset." | n/a |
| 12 | Reach the front door. | Ending. | n/a | n/a |

### Avoid on stage

- **Beat 3 or 5 inside 4.5 m of the ghost.** Inside its threat zone, knocks are ignored unless the
  presence is VERY_NEAR (rules R3–R5 require the player outside the zone). Step back first.
- **Holding PROTECT for more than 6 s.** It caps at 6 s, and during the encounter an attack can follow
  about 1.5 s later. Press **1** soon after showing it.
- **Toggling 4 during an investigation.** It flips the ghost between INVESTIGATE and WARN at once.
  Toggle only from a calm FOLLOW.
- **Leaving confidence LOW.** Press **4** again after beat 4, or later beats show WARN instead of
  INVESTIGATE.
- **The surge inside 3.5 m of the ghost.** RETREAT does not happen there (it falls back to FOLLOW).
  Install the fuse with the ghost at a distance.
- **On hardware: unplugging the cable or resetting the board.** Only show this if F13–F15 passed tonight.

### If something goes wrong

| Symptom | Recovery |
|---|---|
| HUD not `FRESH` on hardware | Esc → Main menu → Simulated sensors → Begin. Continue from beat 1 |
| Ghost stuck or not visible | Esc → Restart from checkpoint (after the fuse), or Restart run |
| Reaction did not appear | Check the overlay: wrong presence key, confidence left LOW, or you are inside the threat zone. Press 1, 4 as needed, step back, and repeat |
| Player died early | That is the checkpoint beat (11); continue from the fuse box |
| Game freezes or crashes | Relaunch; choose Simulated sensors |

### Evidence to mention if asked

- Software tests: 26 suites, 382 tests, 0 failed (`dotnet run -c Release --project Tools/TestHarness`; last run 26 Oct 2026 on the final tree).
- Headless game self-tests: Simulated 36/36, default 34/34, route 23/23.
- Telemetry for every decision change (action, rule or reason), including the rule and the event that caused it:
  `%USERPROFILE%\AppData\LocalLow\DefaultCompany\PHYS_XR\telemetry`, analysed by
  `python Tools/analyze_telemetry.py <file>`.
- Hardware: the result of tonight's Part 1 run (state it as it is; do not claim steps that were not run).
