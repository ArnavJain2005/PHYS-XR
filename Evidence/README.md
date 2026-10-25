# PHYS-XR evidence package

Raw logs and reproducible summaries behind the figures in the [main README](../README.md#evidence) and [`Docs/TESTING_AND_EVALUATION.md`](../Docs/TESTING_AND_EVALUATION.md). Collected on 26 Oct 2026 from the development machine.

**What each kind of file can prove.** A telemetry file records what the game received and decided during one run. A player log records an automated self-test or capture run. A harness output records the Unity-independent tests on a named code tree. None of these is a video, and none of them records a headset session; the Meta Quest 2 and Quest 3 demonstrations are confirmed by the project team, not by a file in this folder.

## Layout

| Path | Contents | Count |
|---|---|---|
| [`Logs/telemetry/trials/`](Logs/telemetry/trials) | Game telemetry of human-run trials, `T<date>-<time>_Windows_<utc>.jsonl` plus its `.trial.json` header (condition, platform, build, start time). Virtual-only, simulated and physical-station runs, 21–24 Oct 2026 | 56 files (28 runs) |
| [`Logs/telemetry/selftest/`](Logs/telemetry/selftest) | Telemetry written by the automated player self-tests | 286 files |
| [`Logs/telemetry/capture/`](Logs/telemetry/capture) | Telemetry written by the scripted capture tour | 18 files |
| [`Logs/player/`](Logs/player) | Unity player logs of self-tests, capture tours and live station sessions, one folder per work session (dates in the folder names); a few folders also keep the harness output of the same session | 83 files |
| [`Logs/harness/`](Logs/harness) | Unity-independent harness outputs from the game, serial and menu passes (21–23 Oct) | 4 files |
| [`Logs/2026-10-26_post-fix/`](Logs/2026-10-26_post-fix) | The final verification after the telegraph fix: harness outputs for each tree, firmware host tests, the Windows build log and the four player self-tests | 11 files |
| [`Logs/unity-test-runner/`](Logs/unity-test-runner) | Unity Test Runner result file from the project data folder | 1 file |
| [`Logs/collected_logs.csv`](Logs/collected_logs.csv) | Manifest of every collected player, harness and test-runner log: original location (user name redacted), modification time, size, SHA-256 of the original, number of redactions, purpose | — |
| [`Logs/telemetry_runs.csv`](Logs/telemetry_runs.csv), [`Logs/telemetry_summary.md`](Logs/telemetry_summary.md) | One row per telemetry run, and the aggregate figures with their definitions | generated |
| [`summarize_telemetry.py`](summarize_telemetry.py) | Recomputes `telemetry_runs.csv` and `telemetry_summary.md` from the raw telemetry | — |

## Provenance and handling

- **Telemetry** was copied from `%USERPROFILE%\AppData\LocalLow\DefaultCompany\PHYS_XR\telemetry\` unchanged. The SHA-256 of every copy matches its original.
- **Player, harness and test-runner logs** were copied from the per-session scratch folders of the development tools (paths listed in `collected_logs.csv`) and from the repository root (`responsiveness_run.log`).
- **Redaction:** in these text logs the Windows account name inside file paths is replaced by `<user>`. Nothing else was changed, so measurements and event order are as recorded. The originals remain on the development machine, and `collected_logs.csv` gives each original's SHA-256.
- **Secrets:** all files were searched for credentials, tokens and passwords before publishing; none were found.
- **Not included:**
  - Unity build folders, `Library/`, and caches.
  - Earlier full Unity build and import logs (0.8–8 MB each). They are superseded by the post-fix build log in `Logs/2026-10-26_post-fix/build/`.
  - Screenshots, which are already in [`Docs/media/`](../Docs/media).

## Reproducing the session figures

```bash
python Evidence/summarize_telemetry.py
```

It prints and writes the aggregate table. Its definitions:

- **Run:** one telemetry file, from **Begin** to the end of the run or a restart.
- **Trial ID:** the label typed in the menu. One trial ID can cover several runs.
- **Physical run:** a human-run file in the `PHYSICAL_PLUS_VIRTUAL` condition.
- **Live physical run:** a physical run that received at least one state line from the station.

### Reconciling "15" and "18"

Both numbers appeared in earlier notes, and both are correct for different units:

| Figure | Unit | Breakdown |
|---|---|---|
| **18** | Human-run files with the **USB-serial link configured** | 16 `PHYSICAL_PLUS_VIRTUAL` runs, plus 2 `VIRTUAL_ONLY` runs in which the link was open but physical input was masked |
| **16** | Physical runs | The 16 `PHYSICAL_PLUS_VIRTUAL` runs, one of which (`T20261024-1157_Windows_20261024T062740Z`) received no station data |
| **15** | **Live physical runs** | Physical runs that received station data |
| **9** | Distinct trial IDs among the 15 live runs | — |

The 15 live runs total 48.1 minutes over 22, 23 and 24 Oct 2026. They received 29,896 state lines (4 rejected, 4 lost to sequence gaps) and accepted 131 knock events, with 262 repeated copies ignored. They produced 70 decisions caused by a knock, 44 of them followed by a logged `BEHAVIOUR START`, and none of their 48 ATTACK decisions had a physical cause.
