# Latency measurement protocol

How to measure the time from a physical action at the station to the ghost's response, hop by hop. **Status: not run yet.** This file defines the measurement; it reports no results.

## Hops and where each timestamp comes from

| Point | Meaning | Logged as | Clock |
|---|---|---|---|
| T0 | The hand touches or knocks the station | **Not logged** | — |
| T1 | The firmware sends the state line carrying the qualified event | `t1_dev` in the `PKT_RX` record of that line (the line's `ts` field, in device milliseconds) | ESP32 `millis()` |
| T2 | The line arrives on Unity's reader thread | `t2` in `PKT_RX` and `PHYS_EVENT` | Receiver stopwatch |
| T3 | The main thread processes the line and accepts the event | the `ts` of the `PHYS_EVENT` record with `result=ACCEPTED`; `q` in the same record is T3 − T2 | Telemetry stopwatch (`ts`); receiver stopwatch (`q`) |
| T4 | The decision that acts on it | the `ts` of the `DECISION` record with `cause_evt=<evt>`, or of `PHYS_EVENT_OUTCOME outcome=ACTED` when the event re-armed a search already running | Telemetry stopwatch |
| T5 | The ghost starts the behaviour | the `ts` of `BEHAVIOUR event=RESEARCH evt=<evt>`, else `BEHAVIOUR event=START` (or `ESCALATE`) with the same `dec` as T4 | Telemetry stopwatch |

Every record also has `frame`, the Unity frame number, so T3–T5 can be expressed in frames as well as milliseconds. `PKT_RX` does not carry the event ID: find the event's line by matching `boot` and `t2` with its `PHYS_EVENT` record.

## What the existing telemetry can and cannot measure

- **T2 → T5 (Unity side): measurable now.** `python Tools/analyze_telemetry.py <file.jsonl>` computes it per event as `q + (ts_T4 − ts_T3)` and `q + (ts_T5 − ts_T3)` (columns `evt_*`), and the same for presence changes (`presence_*`). It prints median, max and n. Only builds that log `q` qualify; among the logged live runs in [`Evidence/`](../Evidence/README.md), the files from 23 Oct 20:57 onwards carry it.
- **T1 → T2 (device to PC): not measurable absolutely.** The ESP32 and the PC share no epoch. `age` in `PKT_RX` is each line's offset minus the smallest offset of the last 50 lines, so it shows transit *jitter* above the best case, not transit time.
- **T0 → T1: not logged.** The firmware qualifies a knock after the motion deviation stays above its threshold for 3 samples at 100 Hz, and the state line does not record when the first of those samples arrived.
- **T5 → photons: not logged.** `BEHAVIOUR START` is when execution begins, not when a changed frame reaches the monitor or the headset. Over Quest Link this also includes encode and streaming time.

## Procedure

1. Use one Windows build, recorded with its commit (`git rev-parse --short HEAD`). Close the Editor and the Arduino Serial Monitor.
2. Choose **Physical - USB serial**, set a Trial ID such as `LAT-desktop-01`, and **Begin**. Wait for the HUD to read `FRESH`.
3. Walk into the corridor so The Hollow manifests (a dormant ghost logs `NOT_SHOWN` and gives no T5). Stay **before the fuse** and at least 5 m from the ghost, outside its threat zone, so a knock with someone near qualifies (R3 or X3).
4. With a hand held at NEAR, knock the station once. Wait at least **4 s** before the next knock, longer than the 3 s disturbance pulse, so each knock starts a fresh decision.
5. Collect **at least 30 accepted knocks per condition**. Conditions: desktop monitor, and PC VR over Quest Link. Repeat on a second day if possible.
6. For T0 → T5 end to end, film the station and the display together with a phone in slow motion (240 fps, about 4 ms per frame). Count frames from first contact to the first frame in which the ghost visibly moves. Note the frame rate and count each clip twice.

## Reporting

- For each hop and condition: **n, median, p95, min, max** in ms. Use the nearest-rank p95 (the ⌈0.95·n⌉-th smallest value). The analyzer prints median, max and n; compute p95 from its per-event values.
- List exclusions with their reason: rejected or duplicate lines, `NOT_SHOWN` decisions, knocks that were `OUTRANKED` or `NOT_QUALIFIED` (`PHYS_EVENT_OUTCOME`).
- Commit the raw `.jsonl` files to `Evidence/`, with the build commit and the trial IDs, so every figure can be recomputed.

## What would need adding for a full T0 → T5 measurement

None of this exists yet; each item is a code change.

1. **Firmware:** a field in the event's state line for the device time of the first above-threshold motion sample (T0′, a sensor-side proxy for T0).
2. **Firmware and Unity:** a time-sync exchange (for example a periodic ping that the station echoes) to estimate the clock offset and round-trip time, so T1 → T2 becomes absolute.
3. **Unity:** the event ID on `PKT_RX`, or `t1_dev` on `PHYS_EVENT`, so no `(boot, t2)` join is needed.
4. **Unity:** a record of the first presented frame after `BEHAVIOUR START`, to bound T5 → photons on the desktop.
5. **Optional hardware:** an LED driven at event qualification, visible in the slow-motion video, to separate T0 → T1 from the rest.
