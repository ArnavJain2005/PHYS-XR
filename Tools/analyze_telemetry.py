#!/usr/bin/env python3
"""Summarise PHYS-XR telemetry (JSON Lines) into per-trial CSV rows.

Reads one or more <trial>_<platform>_<utc>.jsonl files (written by the game to
<persistentDataPath>/telemetry) and prints, per file:

    run outcome and duration, deaths, attempts,
    time share per ghost action (from DECISION records),
    packets received / rejected (by reason) / lost, events by result,
    decisions caused by an accepted event (cause_evt) and whether each joins
    to an accepted PHYS_EVENT and a BEHAVIOUR START (spec test T27),
    inappropriate actions: ATTACK decided while the decision before it was
    PROTECT (must be 0), mean/min fps,
    latency on the Unity side (median / max / n, ms) from a line's arrival
    to the ghost's visible response:
      evt_*       accepted disturbance event -> the decision that acted on it
                  (its caused DECISION, or PHYS_EVENT_OUTCOME ACTED when it
                  re-armed a search already running) -> the visible response
                  (BEHAVIOUR RESEARCH evt=N, else START/ESCALATE of that
                  decision)
      presence_*  a change of presence in PKT_RX -> the next sensor-driven
                  DECISION within 1 s (R1/R4 reasons, X1/X2) -> its BEHAVIOUR
    Each is q (arrival -> processed, same clock) plus the telemetry-time gap.
    Device -> PC transit is not included: the two clocks share no epoch.
    Decisions made while the ghost was dormant (BEHAVIOUR NOT_SHOWN) are
    counted, never joined.

Usage:
    python Tools/analyze_telemetry.py <file.jsonl>... [--csv out.csv]
"""
import argparse
import collections
import csv
import json
import statistics
import sys


def kv(detail):
    out = {}
    for part in (detail or "").split():
        if "=" in part:
            k, v = part.split("=", 1)
            out[k] = v
    return out


def stats(values):
    if not values:
        return "", "", 0
    return round(statistics.median(values), 1), max(values), len(values)


# DECISION reasons that come from the physical station's proximity.
SENSOR_PROXIMITY_REASONS = ("WARN: physical condition", "PROTECT: player at risk + high-confidence")


def sensor_proximity_decision(d):
    if d.get("rule") in ("X1", "X2"):
        return True
    return d.get("rule") == "INNER" and (d.get("reason") or "").startswith(SENSOR_PROXIMITY_REASONS)


def latency(rows, decisions):
    """Unity-side receipt -> decision -> visible-behaviour latencies (ms)."""
    responses = {}
    for x in rows:
        if x.get("type") != "BEHAVIOUR":
            continue
        detail = x.get("detail") or ""
        if ("event=START" in detail or "event=ESCALATE" in detail) and x.get("dec") not in responses:
            responses[x.get("dec")] = x["ts"]

    research = {}
    for x in rows:
        detail = x.get("detail") or ""
        if x.get("type") == "BEHAVIOUR" and "event=RESEARCH" in detail:
            evt_id = kv(detail).get("evt")
            if evt_id:
                research.setdefault(evt_id, x["ts"])
    acted = {}
    for x in rows:
        d = kv(x.get("detail"))
        if x.get("type") == "PHYS_EVENT_OUTCOME" and d.get("outcome") == "ACTED":
            acted.setdefault(d.get("evt"), x)

    out = collections.OrderedDict()

    evt_dec, evt_vis = [], []
    for x in rows:
        d = kv(x.get("detail"))
        if x.get("type") != "PHYS_EVENT" or d.get("result") != "ACCEPTED" or "q" not in d:
            continue
        q = int(d["q"])
        caused = next((c for c in decisions if str(c.get("cause_evt")) == d.get("evt") and c["ts"] >= x["ts"]), None)
        outcome = acted.get(d.get("evt"))
        if caused is None and outcome is None:
            continue
        decided = min(r["ts"] for r in (caused, outcome) if r is not None)
        evt_dec.append(q + decided - x["ts"])
        if d.get("evt") in research:
            evt_vis.append(q + research[d["evt"]] - x["ts"])
        elif caused is not None and caused.get("dec") in responses:
            evt_vis.append(q + responses[caused["dec"]] - x["ts"])

    pres_dec, pres_vis = [], []
    last = None
    for x in rows:
        if x.get("type") != "PKT_RX":
            continue
        d = kv(x.get("detail"))
        presence = d.get("presence")
        if last is not None and presence != last and "q" in d:
            q = int(d["q"])
            hit = next((c for c in decisions if x["ts"] <= c["ts"] <= x["ts"] + 1000 and sensor_proximity_decision(c)), None)
            if hit is not None:
                pres_dec.append(q + hit["ts"] - x["ts"])
                if hit.get("dec") in responses:
                    pres_vis.append(q + responses[hit["dec"]] - x["ts"])
        last = presence

    for name, values in (("evt_to_decision", evt_dec), ("evt_to_visible", evt_vis),
                         ("presence_to_decision", pres_dec), ("presence_to_visible", pres_vis)):
        med, worst, n = stats(values)
        out[name + "_ms_median"] = med
        out[name + "_ms_max"] = worst
        out[name + "_n"] = n
    out["not_shown_dormant"] = sum(1 for x in rows if x.get("type") == "BEHAVIOUR"
                                   and "event=NOT_SHOWN" in (x.get("detail") or ""))
    out["researches"] = len(research)
    return out


def analyse(path):
    rows = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line:
                try:
                    rows.append(json.loads(line))
                except json.JSONDecodeError:
                    pass

    r = collections.OrderedDict()
    r["file"] = path
    r["trial"] = rows[0].get("trial", "") if rows else ""
    r["condition"] = rows[0].get("cond", "") if rows else ""
    r["platform"] = rows[0].get("plat", "") if rows else ""

    end = [x for x in rows if x.get("type") == "RUN_END"]
    last_end = kv(end[-1].get("detail")) if end else {}
    r["outcome"] = last_end.get("outcome", "none")
    r["duration_s"] = last_end.get("duration_s", "")
    r["deaths"] = last_end.get("deaths", "")
    r["attempts"] = max([x.get("attempt", 1) for x in rows] or [1])

    decisions = [x for x in rows if x.get("type") == "DECISION"]
    share = collections.Counter()
    for a, b in zip(decisions, decisions[1:] + [None]):
        t_end = b["ts"] if b else (rows[-1]["ts"] if rows else a["ts"])
        share[a.get("action", "?")] += max(0, t_end - a["ts"])
    total = sum(share.values()) or 1
    for action in ("FOLLOW", "WARN", "INVESTIGATE", "PROTECT", "RETREAT", "ATTACK"):
        r["share_" + action] = round(share[action] / total, 3)
    r["decisions"] = len(decisions)

    pkt = [x for x in rows if x.get("type") == "PKT_RX"]
    rej = [x for x in rows if x.get("type") == "PKT_REJECT"]
    r["packets_rx"] = len(pkt)
    r["packets_rejected"] = len(rej)
    r["packets_lost"] = sum(int(kv(x.get("detail")).get("gap", 0)) for x in pkt)
    reasons = collections.Counter(kv(x.get("detail")).get("reason", "?") for x in rej)
    r["reject_reasons"] = ";".join("{}={}".format(k, v) for k, v in sorted(reasons.items()))
    events = collections.Counter(kv(x.get("detail")).get("result", "?") for x in rows if x.get("type") == "PHYS_EVENT")
    r["event_results"] = ";".join("{}={}".format(k, v) for k, v in sorted(events.items()))

    accepted = {kv(x.get("detail")).get("evt") for x in rows
                if x.get("type") == "PHYS_EVENT" and kv(x.get("detail")).get("result") == "ACCEPTED"}
    started = {x.get("dec") for x in rows if x.get("type") == "BEHAVIOUR" and "event=START" in (x.get("detail") or "")}
    caused = [x for x in decisions if x.get("cause_evt") is not None]
    joined = [x for x in caused if str(x["cause_evt"]) in accepted and x.get("dec") in started]
    r["caused_decisions"] = len(caused)
    r["caused_joined"] = len(joined)

    inappropriate = 0
    for a, b in zip(decisions, decisions[1:]):
        if b.get("action") == "ATTACK" and a.get("action") == "PROTECT":
            inappropriate += 1
    r["inappropriate_attacks"] = inappropriate
    r.update(latency(rows, decisions))

    perf = [kv(x.get("detail")) for x in rows if x.get("type") == "PERF"]
    if perf:
        r["fps_mean"] = round(sum(float(p.get("fps_mean", 0)) for p in perf) / len(perf), 1)
        r["fps_min"] = round(min(float(p.get("fps_min", 0)) for p in perf), 1)
    else:
        r["fps_mean"] = r["fps_min"] = ""
    r["telemetry_dropped"] = sum(int(kv(x.get("detail")).get("dropped", 0)) for x in rows if x.get("type") == "TELEMETRY_DROPPED")
    return r


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("files", nargs="+")
    p.add_argument("--csv")
    args = p.parse_args()
    results = [analyse(f) for f in args.files]
    if not results:
        return 1
    out = open(args.csv, "w", newline="", encoding="utf-8") if args.csv else sys.stdout
    w = csv.DictWriter(out, fieldnames=list(results[0].keys()))
    w.writeheader()
    for r in results:
        w.writerow(r)
    if args.csv:
        out.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
