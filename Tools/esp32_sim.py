#!/usr/bin/env python3
"""Send PHYS-XR sensor packets over real UDP, as the ESP32 station would.

Uses the proposed contract v1 (Technical Spec section E; not yet verified
against firmware): one UTF-8 key=value line per datagram,

    v=1;dev=phys-xr-01;boot=<u32>;seq=<n>;ts=<ms since boot>;presence=NEAR;
    confidence=HIGH;disturbance=false;eventType=;eventId=0;tEvent=0

so the full socket path (UDPReceiver -> queue -> validator -> pipeline) can be
tested against the Editor, a Windows build or a headset without hardware.
Start the game with "Physical sensors (UDP 47800)" selected.

Scenarios:
    idle        NONE/HIGH at 10 Hz
    near        NEAR/HIGH, a disturbance event every 6 s
    threat      VERY_NEAR/HIGH (R1 PROTECT when the player is in the threat zone)
    flap        presence toggles NEAR/NONE every 200 ms (T11)
    duplicates  event 17 sent five times, the last 2 s later (T04)
    outage      5 s of silence in the middle (T09)
    reboot      new boot ID with seq 1 half way (T10)
    interactive keys: 0/1/2 presence, c confidence, d disturbance, r reboot, s stop/resume, q quit

Example:
    python Tools/esp32_sim.py --host 127.0.0.1 --scenario near --seconds 60
"""
import argparse
import random
import socket
import sys
import time


class Station:
    def __init__(self, host, port, device, seed):
        self.addr = (host, port)
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.device = device
        self.rng = random.Random(seed)
        self.reboot()
        self.presence = "NONE"
        self.confidence = "HIGH"
        self.pending_event = None  # (event_id, t_event, lines_left)
        self.sent = 0

    def reboot(self):
        self.boot = self.rng.randint(1, 2**32 - 1)
        self.boot_t0 = time.monotonic()
        self.seq = 0
        self.next_event = 1

    def now_ms(self):
        return int((time.monotonic() - self.boot_t0) * 1000) + 1

    def disturbance(self):
        self.pending_event = (self.next_event, self.now_ms(), 3)
        self.next_event += 1
        return self.pending_event[0]

    def send_line(self, event=None):
        self.seq += 1
        if event is None and self.pending_event:
            eid, tev, left = self.pending_event
            event = (eid, tev)
            self.pending_event = (eid, tev, left - 1) if left > 1 else None
        eid, tev = event if event else (0, 0)
        line = ("v=1;dev={dev};boot={boot};seq={seq};ts={ts};presence={p};confidence={c};"
                "disturbance={d};eventType={et};eventId={eid};tEvent={tev}").format(
            dev=self.device, boot=self.boot, seq=self.seq, ts=self.now_ms(), p=self.presence,
            c=self.confidence, d="true" if eid else "false", et="DISTURBANCE" if eid else "",
            eid=eid, tev=tev)
        self.sock.sendto(line.encode("utf-8"), self.addr)
        self.sent += 1
        return line


def run(args):
    st = Station(args.host, args.port, args.device, args.seed)
    print("sending to {}:{} as {} boot={} scenario={}".format(args.host, args.port, args.device, st.boot, args.scenario))
    start = time.monotonic()
    last_event = start
    sending = True
    dup_done = False
    rebooted = False
    period = 1.0 / args.rate

    if args.scenario == "threat":
        st.presence = "VERY_NEAR"
    elif args.scenario in ("near", "duplicates"):
        st.presence = "NEAR"

    while time.monotonic() - start < args.seconds:
        t = time.monotonic() - start
        if args.scenario == "near" and time.monotonic() - last_event > 6.0:
            print("event", st.disturbance())
            last_event = time.monotonic()
        elif args.scenario == "flap":
            st.presence = "NEAR" if int(t / 0.2) % 2 == 0 else "NONE"
        elif args.scenario == "outage":
            sending = not (args.seconds / 2 - 2.5 <= t < args.seconds / 2 + 2.5)
        elif args.scenario == "reboot" and not rebooted and t > args.seconds / 2:
            st.reboot()
            rebooted = True
            print("reboot -> boot", st.boot)
        elif args.scenario == "duplicates" and not dup_done and t > 3:
            eid = st.next_event
            st.next_event += 1
            tev = st.now_ms()
            for i in range(4):
                st.send_line((eid, tev))
                time.sleep(0.05)
            time.sleep(2.0)
            st.send_line((eid, st.now_ms() - 10))
            dup_done = True
            print("sent event", eid, "five times")
        if sending:
            st.send_line()
        time.sleep(period)
    print("done: {} datagrams".format(st.sent))


def interactive(args):
    import msvcrt  # Windows console
    st = Station(args.host, args.port, args.device, args.seed)
    sending = True
    print(__doc__.split("Example:")[0])
    while True:
        while msvcrt.kbhit():
            k = msvcrt.getwch().lower()
            if k == "q":
                return
            if k in "012":
                st.presence = ["NONE", "NEAR", "VERY_NEAR"][int(k)]
            elif k == "c":
                st.confidence = "LOW" if st.confidence == "HIGH" else "HIGH"
            elif k == "d":
                print("event", st.disturbance())
            elif k == "r":
                st.reboot()
                print("reboot -> boot", st.boot)
            elif k == "s":
                sending = not sending
            print("presence={} confidence={} sending={}".format(st.presence, st.confidence, sending))
        if sending:
            st.send_line()
        time.sleep(1.0 / args.rate)


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--host", default="127.0.0.1")
    p.add_argument("--port", type=int, default=47800)
    p.add_argument("--device", default="phys-xr-01")
    p.add_argument("--rate", type=float, default=10.0, help="state lines per second")
    p.add_argument("--seconds", type=float, default=30.0)
    p.add_argument("--seed", type=int, default=2864130047)
    p.add_argument("--scenario", default="near",
                   choices=["idle", "near", "threat", "flap", "duplicates", "outage", "reboot", "interactive"])
    args = p.parse_args()
    if args.scenario == "interactive":
        interactive(args)
    else:
        run(args)


if __name__ == "__main__":
    sys.exit(main())
