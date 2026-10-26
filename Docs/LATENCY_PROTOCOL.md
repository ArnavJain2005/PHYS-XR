# Latency measurement protocol

Latency measurement has not yet been performed. This document defines a protocol; it does not report measured latency results.

It measures the time from a physical knock at the station to the ghost's visible response, hop by hop, using the timestamps the system already produces. Where a timestamp does not exist, this document says so rather than inventing one.

## Timestamps

| Point | Meaning | Source and clock | Recorded today? |
|---|---|---|---|
| **T0** | Firmware event time when the disturbance qualifies | `tEvent` in the state line. The firmware sets it from its device clock when the motion detector qualifies a knock (`activeEventTs = deviceTime()` in `physxr_station.ino`). ESP32 millisecond clock | **Sent but not logged.** `PacketValidator` parses `tEvent`, but no telemetry record stores it |
| **T1** | Timestamp of the semantic state line | `ts` in the state line, set by `deviceTime()` when the line is formatted. ESP32 millisecond clock. Logged as `t1_dev` in `PKT_RX` | Yes, per line. `PKT_RX` does not carry the event ID: match it to the event's `PHYS_EVENT` record by `boot` and `t2` |
| **T2** | USB-serial line arrival in Unity | `t2` in `PKT_RX` and `PHYS_EVENT`: the receiver thread's stopwatch at arrival | Yes |
| **T3** | `WorldState` updated from the accepted event | `GameFlowController` passes the event to `WorldStateManager.NotifyAcceptedEvent` in the same frame the pipeline accepts it | **Not logged.** The closest record is the `PHYS_EVENT result=ACCEPTED` record: its `ts` (trial clock), its `frame` and `q` (arrival → processing, receiver clock). A `PHYS_EVENT_DROPPED` record marks an event that was not applied (run not playing) |
| **T4** | The causally attributable decision | `DECISION` record with `cause_evt=<eventId>`. `ts` is the trial clock (`TelemetryService` stopwatch, ms since trial start) | Only when the decision changes (see [Knocks with no T4](#knocks-with-no-t4)) |
| **T5** | The matching behaviour start | `BEHAVIOUR event=START` with the same `dec` as T4. A knock that re-arms a search already running logs `BEHAVIOUR event=RESEARCH evt=<eventId>` instead. Trial clock | Yes, when the ghost is manifested. A dormant ghost logs `event=NOT_SHOWN` and has no T5 |
| **T6** | First visible in-game response attributable to the event | Independently synchronised high-frame-rate video, or an equivalent external capture | Not part of the software; needs the apparatus below |

Every telemetry record also carries `frame`, the Unity frame number.

## What each interval can and cannot show

- **T1 − T0:** delay on the device from qualification to the state line. Both values use the ESP32 clock, so the difference is meaningful. **But T0 is not logged today.** Measuring it needs a code change (log `tEvent` in `PHYS_EVENT`) or a separate capture of the raw serial lines. A separate capture cannot run alongside the game, because only one program can own the COM port. The pipeline does use the value: it rejects an event as `EVENT_TOO_OLD` when `ts − tEvent` exceeds 300 ms (`PhysicalCommunicationPipeline.DefaultMaxEventAgeMs`), so every accepted event had T1 − T0 ≤ 300 ms.
- **T2 − T1:** how the line's PC arrival time relates to its device time. It is **not** true absolute one-way transport latency. The two clocks share no epoch. `age` in `PKT_RX` is each line's offset minus the smallest offset in the last 50 lines, so it shows transit *jitter* above the best case.
  - An offset estimated from the same USB stream cannot determine absolute one-way latency.
  - That would need an independent reference, such as a round-trip exchange or external capture of both ends.
- **T3 − T2:** approximated by `q`, the time from arrival to processing, measured on the receiver's clock. T3 itself is not logged.
- **T4 − T3** and **T5 − T4:** meaningful only when both timestamps exist and their timing is comparable.
  - T4 and T5 share the trial clock.
  - T3 is approximated by the `PHYS_EVENT` record's `ts`, which is on the trial clock, in the same frame.
  - Report T4 − T3 as "decision delay after acceptance, frame-resolution".
- **T6 − T0:** needs a defensible synchronisation between the external capture and the firmware event time (see [Apparatus and synchronisation](#apparatus-and-synchronisation)). Without it, report T6 only relative to a visible marker in the same video.

`Tools/analyze_telemetry.py` already reports the Unity-side share per event (`evt_*` columns: `q` plus the trial-clock gap to the decision and to the visible behaviour) as median, max and n.

## Knock paths and trial counts

Collect **at least 20 deliberate trials per applicable physical knock path**. The rules that read a knock are below (source: `RuleBasedDecisionProvider`, `EncounterDecisionLayer`; see [`GHOST_DECISION_SYSTEM.md`](GHOST_DECISION_SYSTEM.md)).

| Path | Rule conditions | Expected T4 | Applicability to the station today |
|---|---|---|---|
| **R3 INVESTIGATE** | presence NEAR or VERY_NEAR, a fresh accepted knock, confidence **HIGH**, player outside the 3.5 m threat zone | `DECISION rule=INNER`, R3 reason | Requires HIGH confidence, which needs both ranging sensors to report valid readings. Confirm before collecting; if every line is LOW, record R3 as **not reachable** rather than collecting zero-valued samples |
| **X3 INVESTIGATE** | presence NEAR or VERY_NEAR, a fresh accepted knock, confidence **LOW**, the inner result R4 WARN or idle (default sensor-response profile) | `DECISION rule=X3` | The path observed in the logged live runs |
| **R5 WARN** | a fresh accepted knock, presence **NONE**, player outside the threat zone | `DECISION rule=INNER`, R5 reason | Applicable |

**Collection conditions:**
- Collect before the fuse objective, with the ghost manifested and the player outside the threat zone.
- Space knocks at least **4 s** apart, longer than the 3 s disturbance pulse and the firmware's 1.5 s re-arm.
- Use the **Physical - USB serial** condition only. Virtual-only and Simulated trials never count toward physical knock-latency samples.

### Knocks with no T4

`DECISION` records are written only when the action, rule or reason changes. A knock that does not change the decision has no T4, so **never fabricate one.** Classify each accepted knock by its `PHYS_EVENT_OUTCOME` record:

| Class | How to recognise it | Counted in |
|---|---|---|
| **Decided** | A `DECISION` with `cause_evt` equal to the event's ID | T4 and T5 timing |
| **Re-armed** | `outcome=ACTED` with no new `DECISION` (the event re-armed a search already running); a `BEHAVIOUR event=RESEARCH evt=<eventId>` follows | T5 timing from T3 only; excluded from decision-to-behaviour timing |
| **Outranked** | `outcome=OUTRANKED` | Counted separately; no latency sample |
| **Not qualified** | `outcome=NOT_QUALIFIED` | Counted separately; no latency sample |
| **Not shown** | T4 present, but `BEHAVIOUR event=NOT_SHOWN` (dormant ghost) | T4 timing only; excluded from T5 |

## Apparatus and synchronisation

**Record for every session:**
- **Build:** the executable path and its commit (`git rev-parse --short HEAD`).
- **Firmware:** the version from the `#HELLO` line (`fw=`, `boot=`).
- **Station:** the sensor and serial configuration: COM port, 115200 baud, the `#HELLO` pins and I²C devices, and which ranging sensors report valid readings in `#DIAG` / `SENSOR_DIAG`.
- **Setup:** the PC, display or headset path (desktop monitor, or PC VR over Quest Link), and the Trial ID.

**Event labelling:** give every trial a Trial ID, and keep a written log of knock number → path (R3 / X3 / R5) → wall-clock time.

**External video for T6:**
- Film the station and the display (or a headset mirror) in one frame, at **≥ 120 fps**.
- **Synchronisation markers:** start each block with a deliberate double knock, visible in the video and logged as two events, to align the video timeline to the event sequence.
  - A device-side marker (an LED driven at qualification) would tie video frames to T0 directly. It does not exist yet and would be a firmware change.
  - Without it, state that T6 is measured from the first visible contact in the video, not from T0.

## Data handling

**Stale, duplicate and missing samples:**
- Lines rejected by the validator appear as `PKT_REJECT`.
- Repeated copies of an event appear as `PHYS_EVENT result=DUPLICATE`.
- Sequence gaps appear as `gap` in `PKT_RX`.
- Use only the first accepted copy of each event.
- Exclude an event from timing if its line was stale, or if its `PHYS_EVENT` is missing.

**Failures and exclusions:** report every excluded knock with its reason: link not FRESH, dropped while paused, a missing record, the ghost dormant, outranked, or not qualified.

**Repeatability limits:**
- Unity's frame rate (VSync) quantises T3 to T5.
- Windows scheduling and the USB serial driver add jitter.
- A human knock varies in force and timing.
- Repeat each path on a second day where possible, and report the sessions separately before pooling them.

**Reporting:** for each path, interval and condition, report the **median, p95** (nearest rank), **min, max**, the **sample count** and the **exclusion count**, and list the excluded knocks by class. Commit the raw `.jsonl` files, with the build commit and trial IDs, to `Evidence/` in a new folder.

## What a complete measurement would still need

None of these exist yet; each is a code or hardware change:

1. Log `tEvent` (T0) in the `PHYS_EVENT` record.
2. Log the frame and trial-clock time of the `WorldState` update (T3) when an event is applied.
3. Add an independent time reference between the ESP32 and the PC (for example, a round-trip exchange) to bound one-way transport.
4. Add a device-side visual marker at qualification, for synchronising the video.
