# PHYS-XR: The Presence — Milestone Status (Technical Spec addendum)

This file records milestone status against the **Technical Implementation Specification**
(`Technical Spec Doc.pdf`, consistency-reviewed 20 Oct 2026, review commit `5aa9642`) and
**GDD v1.1**. The spec PDF lives outside this repository and has no editable source here, so its
status tables (section G, "Status" columns; section H, milestones) are updated by this addendum
rather than in the PDF itself. Statuses below are only claimed where there is evidence.

Branch: `demo/warning-beat-arbitration`. Unity `6000.5.8f1`, URP 17.5.0, Input System 1.20.0.

## Milestone summary

| Milestone | Status | Evidence / what is missing |
|---|---|---|
| M0 Baseline | Partial | Feature branch exists. `Tools/TestHarness` is now committed (spec recommendation). Not done: Git LFS rules, asset register, Haunted Jaunt / ghost import (art import deliberately deferred). |
| M1 Repo + fixes | Partial | Done earlier on this branch: `EncounterDecisionLayer`, `GhostPhase`, `IExplainableDecisionProvider`, terminal gate. **Not done: P1–P5 (all unresolved), asmdefs, `GhostExecutionState` move to Core, `TestRunnerBridge`.** |
| **M2 Desktop loop** | **Implemented; automated checks pass; manual Editor play-through pending** | See below. |
| M3–M11 | Not started | — |

## M2 — Desktop greybox loop, no ghost

### What was built

| Spec item | Implementation | Notes / deviation |
|---|---|---|
| Bootstrap + Game scenes | `Assets/PHYSXR/Scenes/Bootstrap.unity` (index 0), `Game_BlackthornHouse.unity` (index 1) | Created with Unity's scene API (`PHYS-XR > M2 > Create Or Repair Scenes`). `SampleScene` stays in the project as a dev scene, out of the build. |
| `AppBootstrap`, `GameSession` | `App/AppBootstrap.cs`, `Game/Flow/GameSession.cs` | Session only. UDP/pipeline/telemetry wiring is M5–M7. `GameSession` sits in `Game/Flow` (plain C#, so the harness can test it), not `App/`. |
| `PlatformRigSpawner` (desktop path) | Not built | `BlackthornGreybox` builds the desktop rig directly. A spawner arrives with the Quest rig (M8). |
| `IPlayerInput`, `DesktopInputAdapter`, `DesktopMotor`, `PlayerController` | `Game/Input`, `Game/Player` | Move, Look, Interact come from the unmodified project-wide `InputSystem_Actions` (Player map); Pause is a local Esc action, because the asset has none. |
| `PlayerInteractor`, `Interactable`, `FusePickup`, `FuseBox`, `ExitDoor` | `Game/Player`, `Game/Gameplay` | Centre-of-camera ray, 2 m range, walls block. Interactables never read devices. |
| `PowerSystem` | `Game/Gameplay/PowerSystem.cs` | Two placeholder lighting states and a 0.8 s flicker. No hum audio: no suitable asset in this repo. Emissive change is best-effort (the URP variant may be stripped from builds); the light changes are the real cue. |
| `GameFlowController` | `Game/Gameplay/GameFlowController.cs` over plain-C# `Game/Flow/GameFlow.cs` | Explicit `RunState` (Playing/Paused/Succeeded) and ordered `ObjectiveStage` (FindFuse → InstallFuse → RestoringPower → ReachExit → Completed); invalid transitions are refused with a reason. |
| `GameUI`, `ScreenFader` | `Game/UI/GameUI.cs` (IMGUI) | IMGUI placeholder HUD, pause menu and success screen. No ScreenFader or ending Timeline (M10). Quest needs world-space UI (M8). |
| Greybox from Haunted Jaunt rooms | `Game/Level/BlackthornGreybox.cs` (primitives) | Primitives by instruction; no art import in M2. Five-room loop per spec section C; critical path about 60 m. |
| NavMesh bake | Deferred to M3 | No ghost in M2. |
| Restart | Full restart reloads `Game_BlackthornHouse` | This is not a checkpoint system. Death, checkpoint save/restore and respawn are M4. |

### Controls (Windows)

WASD move · mouse look (pitch clamped ±80°) · E interact · Esc pause/resume. Pause menu: Resume / Restart run / Quit. Success screen: Restart run / Quit.

### Test evidence

| Kind | What ran | Result |
|---|---|---|
| Standalone .NET (`Tools/TestHarness`, C# 9, net9.0) | All 16 `RunAll()` suites: the 170 existing tests plus `GameFlowTests` FLOW-01..16 | **186 passed, 0 failed** |
| Unity batch compile (copy of this project, 6000.5.8f1) | Import + script compilation | 0 compiler errors, 0 compiler warnings in PHYS-XR code |
| Unity Windows player build (same copy) | `M2SceneSetup.BuildWindows`, StandaloneWindows64 | Build **Succeeded**, 0 errors |
| In-player automated route (`-physxr-m2-selftest`, headless) | `M2RouteSelfTest`: scripted input drives the real CharacterController, camera ray and interact path | **23 passed, 0 failed**; scripted walk-only route **27.1 s** |

The Unity checks ran on a scratch copy of the project, because the Editor had this checkout open.
What matched the commit, and what did not:

- **Identical to `bf7fb52`:** all of `Assets/PHYSXR` (byte-for-byte), `Packages/manifest.json`, and the
  regenerated `Packages/packages-lock.json`. The copy's manifest is the committed one, because the
  local `com.coplaydev.unity-mcp` line is uncommitted and was removed from the copy.
- **Not identical:**
  - The copy included the developer's uncommitted XR files: a modified `OpenXR Package Settings.asset`
    and an untracked `Assets/XR/Loaders/OpenXRLoader.asset`. Unity then generated
    `XRGeneralSettingsPerBuildTarget.asset` and `OpenXR Editor Settings.asset`.
  - The copy's `EditorBuildSettings.asset` had an extra `xr.management.loader_settings` entry. Its
    scene GUIDs were also stale, the same mismatch the commit had (fixed in the follow-up commit).
    The build was not affected, because `BuildWindows` passes scene paths explicitly.
  - The fresh import re-serialised `ProjectSettings.asset`, `UnityConnectSettings.asset`,
    `Assets/Settings/PC_RPAsset.asset`, `UniversalRenderPipelineGlobalSettings.asset` and
    `DefaultVolumeProfile.asset`. None of these changes was committed.
- **Not covered:** the self-test ran headless (`-batchmode -nographics`), so nothing was rendered.
  Cursor lock, the IMGUI HUD and menus, and real keyboard/mouse input through `DesktopInputAdapter`
  were not exercised.

### Spec test cases

| ID | Status | Evidence |
|---|---|---|
| T19 Fuse and exit progression | **Pass (automated), lights only** | Self-test C03–C07, C11; `OBJECTIVE` log lines arrive in order. The "hum changes" part is not implemented (no audio asset). |
| T20 Full restart | **Pass (automated)** | C10a after success, C10c while paused mid-run: clean start state, run number increments, attempt back to 1. |
| T21 Windows input | Not run | Needs a person at the keyboard (bindings, cursor lock, pause). |
| T28 Repo regression suite | Pass | 186/186 (was 130 at review, 170 before M2). |
| T29 Defect probes P1–P5 | **Fail (unchanged)** | P1–P5 are still unresolved. M2 does not touch them. |

### Manual Editor checklist (not yet run)

Open `Assets/PHYSXR/Scenes/Bootstrap.unity`, press Play, and check each item. `PHYS-XR > M2 > Run Route Self-Test (Play Mode)` runs the automated version in the Editor.

| # | Check | Automated equivalent | Manual |
|---|---|---|---|
| 1 | Starts at the entry hall with power off | C01 | Not run |
| 2 | Fuse can be picked up exactly once | C02, C11a | Not run |
| 3 | Fuse box rejects use before pickup | C03 | Not run |
| 4 | Fuse installs | C04 | Not run |
| 5 | Lighting changes on power restoration | C05 | Not run |
| 6 | Exit refuses before power | C06 | Not run |
| 7 | Exit succeeds after power | C07 | Not run |
| 8 | Success disables gameplay | C08 | Not run |
| 9 | Pause and resume work | C09 | Not run |
| 10 | Restart restores a clean run | C10a, C10c | Not run |
| 11 | Repeated interactions cannot corrupt state | C11a, C11b, FLOW-02/10/15 | Not run |
| 12 | Walk-only route < 2.5 min | C12 (27.1 s scripted) | Not run (time a first-time human run) |

## Open items carried forward

- **P1** (hysteresis dwell measured on a frozen sensor timestamp in virtual-only mode) **must be fixed before the ghost enters in M3.** P2–P5 are also still open. Sensor validation is not complete; no ESP32 firmware exists.
- **Deferred decision:** whether a new ATTACK outranks a mandatory scripted warning beat. Leave it to the encounter milestone (M4); M2 does not settle it.
- M2 runs with no ESP32, headset or external service. No raw sensor data reaches objectives.
- No asmdefs yet, so the new `PHYSXR.Game.*`/`PHYSXR.App` code compiles into `Assembly-CSharp` like the rest of the repo.
