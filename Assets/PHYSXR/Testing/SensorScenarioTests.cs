using System;
using System.Collections.Generic;
using System.Text;
using PHYSXR.Communication;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.Decision;
using PHYSXR.PhysicalWorld;
using PHYSXR.WorldStateSystem;

namespace PHYSXR.Testing
{
    // Scripted sensor scenarios (Technical Spec section G, T01, T02 and
    // T04-T10) run end to end in software: SimulatedSensorStation bytes ->
    // UDPReceiver queue -> SensorFeed -> validator/pipeline -> gated
    // WorldStateManager -> EncounterDecisionLayer(R1-R8), all on one injected
    // clock. These are the same components the game composes; no socket,
    // ESP32 or Unity is involved, so they do not replace the hardware runs
    // (T03 and T09/T10 on the real station).
    public static class SensorScenarioTests
    {
        public static int RunAll()
        {
            int passed = 0;
            int failed = 0;

            void Run(string name, Action test)
            {
                try
                {
                    test();
                    passed++;
                    Console.WriteLine("PASS: " + name);
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine("FAIL: " + name + " - " + ex.Message);
                }
            }

            Run("SENS-T01_VirtualOnly_NoPhysicalRuleFires_EventsMasked", T01);
            Run("SENS-T02a_Simulated_VeryNearInThreatZone_ProtectSameFrame", T02a);
            Run("SENS-T02b_Simulated_NearDisturbance_InvestigateForPulsePlusDwell", T02b);
            Run("SENS-T04_DuplicateEvent_OneAcceptedFourDuplicate_OnePulse", T04);
            Run("SENS-T05_OldEventId_Rejected", T05);
            Run("SENS-T06_StaleTransitAndOldEvent_Rejected", T06);
            Run("SENS-T07_InvalidPackets_MatchingReasons_NoException", T07);
            Run("SENS-T08_OutOfOrder_RejectedAndStateFromNewest", T08);
            Run("SENS-T09_Disconnection_StaleThenUnavailable_ResyncBeforeEvents", T09);
            Run("SENS-T10_Reboot_LoggedAccepted_EventAfterResync", T10);
            Run("SENS-BLACKOUT_RestartBlackout1s", Blackout);
            Run("WSM-PAUSE-05_EventDuringPause_NoReactionOnResume_NextEventInvestigates", Pause05);

            Console.WriteLine("SensorScenarioTests: " + passed + " passed, " + failed + " failed");
            return failed;
        }

        private sealed class Rig
        {
            public long Now = 100000;
            public readonly SimulatedSensorStation Station = new SimulatedSensorStation();
            public readonly UDPReceiver Receiver;
            public readonly PhysicalStateManager Physical = new PhysicalStateManager();
            public readonly PhysicalEventManager Events = new PhysicalEventManager();
            public readonly PhysicalCommunicationPipeline Pipeline;
            public readonly SensorFeed Feed;
            public readonly WorldStateManager World;
            public readonly EncounterDecisionLayer Layer;
            public readonly List<PacketOutcome> Log = new List<PacketOutcome>();
            public int PulsesStarted;
            public int EventsDropped;

            // Game clock, used by the world and the decision layers only when
            // the rig is built with gameClock: true. It stops while Paused, as
            // GameFlowController's GameClockMs does; the sensor clock (Now)
            // keeps running.
            public long Game = 100000;
            public bool Paused;

            public Rig(bool masked = false, bool gameClock = false)
            {
                Func<long> worldClock = gameClock ? (Func<long>)(() => Game) : () => Now;
                Receiver = new UDPReceiver(0, () => Now);
                Pipeline = new PhysicalCommunicationPipeline(new PacketValidator(SimulatedSensorStation.DefaultDeviceId),
                    new PhysicalStateParser(), Physical, Events) { EventsMasked = masked };
                Feed = new SensorFeed(Receiver, Pipeline);
                World = new WorldStateManager(Physical, worldClock) { PhysicalInputMasked = masked };
                Layer = new EncounterDecisionLayer(new RuleBasedDecisionProvider(decisionClock: worldClock), worldClock);
                World.UpdatePlayerState(new PlayerState { distanceToGhost = 8f });
                World.UpdateVirtualState(new VirtualState { threatLevel = 0.2f });
                World.UpdateGhostState(new GhostState());
            }

            // One 50 ms frame: station sends, feed pumps, world updates, decide.
            public GhostAction Frame(long dtMs = 50)
            {
                Now += dtMs;
                if (!Paused)
                    Game += dtMs;
                foreach (byte[] datagram in Station.Poll(Now))
                    Receiver.Enqueue(datagram, Now);
                Station.ClearOutbox();
                return Pump();
            }

            // Mirrors GameFlowController: sensing is bridged every frame, events
            // are suspended while paused, and the ghost decides only in play.
            public GhostAction Pump()
            {
                World.EventsSuspended = Paused;
                foreach (PacketOutcome outcome in Feed.Pump(Now))
                {
                    Log.Add(outcome);
                    if (outcome.acceptedEvent != null)
                    {
                        if (World.NotifyAcceptedEvent(outcome.acceptedEvent))
                            PulsesStarted++;
                        else
                            EventsDropped++;
                    }
                }
                World.UpdateSourceStatus(Feed.Status);
                if (Paused)
                    return Layer.PreviousAction ?? GhostAction.FOLLOW;
                return Layer.Decide(World.GetCurrentWorldState());
            }

            public void Raw(string text, long arrivalMs)
            {
                Receiver.Enqueue(Encoding.UTF8.GetBytes(text), arrivalMs);
            }

            public int Count(EventAcceptance result)
            {
                int n = 0;
                foreach (PacketOutcome o in Log)
                    if (o.eventResult == result)
                        n++;
                return n;
            }

            public int Count(PacketRejectCode code)
            {
                int n = 0;
                foreach (PacketOutcome o in Log)
                    if (!o.accepted && o.code == code)
                        n++;
                return n;
            }

            public void Settle(int frames = 6)
            {
                for (int i = 0; i < frames; i++)
                    Frame();
            }
        }

        // Fix F1, end to end: a knock while the run is paused is accepted and
        // deduplicated by the pipeline as usual but starts no pulse, so after a
        // minute-long pause the ghost does not react to it on resume. The next
        // knock in play gets the normal single INVESTIGATE.
        private static void Pause05()
        {
            var rig = new Rig(gameClock: true);
            rig.Station.SetPresence(PresenceLevel.NEAR);
            rig.Settle();
            AssertEq(GhostAction.FOLLOW, rig.Frame(), "NEAR/HIGH without an event follows");

            rig.Paused = true;
            long gameAtPause = rig.Game;
            rig.Station.TriggerDisturbance(rig.Now);
            for (int i = 0; i < 1200; i++) // 60 s of real time
                rig.Frame();
            AssertEq(gameAtPause, rig.Game, "game clock stopped while paused");
            AssertEq(1, rig.Count(EventAcceptance.ACCEPTED), "the pipeline accepts the paused knock once");
            AssertEq(1, rig.EventsDropped, "and the world drops it");
            AssertEq(0, rig.PulsesStarted, "no pulse while paused");

            rig.Paused = false;
            for (int i = 0; i < 100; i++) // 5 s of play
            {
                GhostAction a = rig.Frame();
                AssertTrue(a == GhostAction.FOLLOW, "reaction to the paused knock after resume at frame " + i + ": " + a);
            }

            rig.Station.TriggerDisturbance(rig.Now);
            int investigateFrames = 0, episodes = 0;
            GhostAction previous = GhostAction.FOLLOW;
            for (int i = 0; i < 120; i++)
            {
                GhostAction a = rig.Frame();
                if (a == GhostAction.INVESTIGATE)
                {
                    investigateFrames++;
                    if (previous != GhostAction.INVESTIGATE)
                        episodes++;
                }
                previous = a;
            }
            AssertEq(1, episodes, "one INVESTIGATE episode for the knock in play");
            AssertTrue(investigateFrames * 50 >= 3000, "INVESTIGATE lasts the 3 s pulse: " + investigateFrames * 50 + " ms");
            AssertEq(1, rig.PulsesStarted, "one pulse in play");
            AssertEq(2, rig.Count(EventAcceptance.ACCEPTED), "two knocks accepted in all");
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        private static void AssertEq<T>(T expected, T actual, string context)
        {
            if (!Equals(expected, actual))
                throw new Exception(context + ": expected " + expected + " but got " + actual);
        }

        private static string Line(long boot, long seq, long ts, string presence = "NONE", long eventId = 0, long tEvent = 0, string extra = "")
        {
            return "v=1;dev=phys-xr-01;boot=" + boot + ";seq=" + seq + ";ts=" + ts + ";presence=" + presence
                + ";confidence=HIGH;disturbance=" + (eventId != 0 ? "true" : "false")
                + ";eventType=" + (eventId != 0 ? "DISTURBANCE" : "") + ";eventId=" + eventId + ";tEvent=" + tEvent + extra;
        }

        private static void T01()
        {
            var rig = new Rig(masked: true);
            rig.Settle();
            var produced = new HashSet<GhostAction>();
            foreach (PresenceLevel presence in new[] { PresenceLevel.NEAR, PresenceLevel.VERY_NEAR })
            {
                rig.Station.SetPresence(presence);
                rig.World.UpdatePlayerState(new PlayerState { distanceToGhost = 2f, inThreatZone = presence == PresenceLevel.VERY_NEAR });
                rig.Station.TriggerDisturbance(rig.Now);
                for (int i = 0; i < 40; i++)
                {
                    produced.Add(rig.Frame());
                    string rule = rig.Layer.Inner.LastDecisionReason;
                    AssertTrue(!rule.StartsWith("PROTECT") && !rule.StartsWith("INVESTIGATE") && !rule.StartsWith("WARN: physical")
                        && !rule.StartsWith("WARN: disturbance"), "physical rule fired in VIRTUAL_ONLY: " + rule);
                }
            }
            AssertTrue(rig.Count(EventAcceptance.MASKED) >= 2 && rig.Count(EventAcceptance.ACCEPTED) == 0, "events logged MASKED");
            AssertEq(0, rig.PulsesStarted, "no pulse");
            AssertTrue(rig.Pipeline.PacketsAccepted > 0, "packets still received and counted");
        }

        private static void T02a()
        {
            var rig = new Rig();
            rig.Settle();
            rig.World.UpdatePlayerState(new PlayerState { distanceToGhost = 3f, inThreatZone = true });
            rig.Station.SetPresence(PresenceLevel.VERY_NEAR);
            AssertEq(GhostAction.PROTECT, rig.Frame(), "PROTECT in the frame VERY_NEAR arrives");
            AssertTrue(rig.Layer.LastDecisionReason.StartsWith("PROTECT"), "reason logged: " + rig.Layer.LastDecisionReason);
        }

        private static void T02b()
        {
            var rig = new Rig();
            rig.Station.SetPresence(PresenceLevel.NEAR);
            rig.Settle();
            rig.Station.TriggerDisturbance(rig.Now);
            long start = -1, end = -1;
            for (int i = 0; i < 200; i++)
            {
                GhostAction a = rig.Frame();
                if (a == GhostAction.INVESTIGATE && start < 0) start = rig.Now;
                if (a != GhostAction.INVESTIGATE && start >= 0 && end < 0) end = rig.Now;
            }
            AssertTrue(start > 0 && end > 0, "INVESTIGATE episode observed");
            long duration = end - start;
            AssertTrue(duration >= 3000 && duration <= 3000 + 1500 + 200, "INVESTIGATE for pulse + dwell: " + duration + " ms");
            AssertEq(1, rig.PulsesStarted, "one pulse for one event (repeats deduplicated)");
        }

        private static void T04()
        {
            var rig = new Rig();
            rig.Settle();
            long boot = rig.Station.Boot;
            long seq = rig.Station.LastSequence;
            long ts = 1000;
            for (int i = 0; i < 5; i++)
            {
                long arrival = rig.Now + (i == 4 ? 2000 : i * 50);
                ts += i == 4 ? 2000 : 50;
                rig.Raw(Line(boot, ++seq + 100, ts + 400, "NEAR", 17, ts + 395), arrival + 400);
            }
            rig.Now += 2500;
            rig.Pump();
            AssertEq(1, rig.Count(EventAcceptance.ACCEPTED), "accepted");
            AssertEq(4, rig.Count(EventAcceptance.DUPLICATE), "duplicates");
            AssertEq(1, rig.PulsesStarted, "exactly one pulse");
        }

        private static void T05()
        {
            var rig = new Rig();
            rig.Settle();
            long boot = rig.Station.Boot;
            rig.Raw(Line(boot, 1000, 5000, "NEAR", 17, 4990), rig.Now + 1000);
            rig.Raw(Line(boot, 1001, 5050, "NEAR", 16, 5040), rig.Now + 1050);
            rig.Now += 1100;
            rig.Pump();
            AssertEq(1, rig.Count(EventAcceptance.ACCEPTED), "17 accepted");
            AssertEq(1, rig.Count(EventAcceptance.OLD_EVENT), "16 is OLD_EVENT");
        }

        private static void T06()
        {
            var rig = new Rig();
            rig.Settle(30);
            rig.Station.SendStaleLine(rig.Now, 1000);
            rig.Frame(0);
            AssertTrue(rig.Count(PacketRejectCode.STALE_PACKET) == 1, "stale line rejected");

            long boot = rig.Station.Boot;
            long seq = rig.Station.LastSequence + 10;
            long ts = rig.Now - 100000 + 1 + 50;   // station clock: ms since its boot
            rig.Raw(Line(boot, seq, ts, "NEAR", 40, ts - 400), rig.Now + 50);
            rig.Now += 50;
            rig.Pump();
            AssertEq(1, rig.Count(EventAcceptance.EVENT_TOO_OLD), "ts - tEvent = 400 ms > 300 ms");
            AssertEq(0, rig.PulsesStarted, "no pulse from stale data");
        }

        private static void T07()
        {
            var rig = new Rig();
            rig.Settle();
            long boot = rig.Station.Boot;
            rig.Raw("{\"presence\":\"NEAR\"}", rig.Now);
            rig.Raw(Line(boot, 900, 5000).Replace("v=1", "v=9"), rig.Now);
            rig.Raw(Line(boot, 901, 5000).Replace("phys-xr-01", "intruder"), rig.Now);
            rig.Raw(Line(boot, 902, 5000, "SOMEWHERE"), rig.Now);
            rig.Receiver.Enqueue(new byte[600], rig.Now);
            rig.Pump();
            AssertEq(1, rig.Count(PacketRejectCode.MALFORMED), "malformed JSON-like text");
            AssertEq(1, rig.Count(PacketRejectCode.VERSION_UNSUPPORTED), "wrong v");
            AssertEq(1, rig.Count(PacketRejectCode.FOREIGN_DEVICE), "foreign dev");
            AssertEq(1, rig.Count(PacketRejectCode.INVALID_FIELD), "bad enum");
            AssertEq(1, rig.Count(PacketRejectCode.BAD_SIZE), "600-byte datagram");
        }

        private static void T08()
        {
            var rig = new Rig();
            rig.Settle();
            long boot = rig.Station.Boot;
            long b = rig.Station.LastSequence;
            long t = rig.Now - 100000 + 1;
            rig.Raw(Line(boot, b + 10, t + 10, "NONE"), rig.Now + 10);
            rig.Raw(Line(boot, b + 12, t + 20, "VERY_NEAR"), rig.Now + 20);
            rig.Raw(Line(boot, b + 11, t + 30, "NEAR"), rig.Now + 30);
            rig.Now += 40;
            rig.Pump();
            AssertEq(1, rig.Count(PacketRejectCode.OUT_OF_ORDER), "11 rejected");
            AssertEq(PresenceLevel.VERY_NEAR, rig.Physical.GetCurrentState().presence, "state is from 12");
            AssertTrue(rig.Pipeline.PacketsLost >= 1, "gap counted as loss");
        }

        private static void T09()
        {
            var rig = new Rig();
            rig.Station.SetPresence(PresenceLevel.NEAR);
            rig.Settle();
            AssertEq(PhysicalSourceStatus.FRESH, rig.Feed.Status, "fresh before the outage");

            rig.Station.SetSending(false);
            long lastLine = rig.Now;
            PhysicalSourceStatus at550 = PhysicalSourceStatus.FRESH, at3050 = PhysicalSourceStatus.FRESH;
            for (int i = 0; i < 100; i++)
            {
                rig.Frame();
                if (rig.Now - lastLine == 550) at550 = rig.Feed.Status;
                if (rig.Now - lastLine == 3050) at3050 = rig.Feed.Status;
                AssertTrue(rig.World.GetCurrentWorldState().physical.presence == PresenceLevel.NONE || rig.Now - lastLine <= 500,
                    "physical rules off once not FRESH");
            }
            AssertEq(PhysicalSourceStatus.STALE, at550, "STALE after 0.5 s");
            AssertEq(PhysicalSourceStatus.UNAVAILABLE, at3050, "UNAVAILABLE after 3 s");

            rig.Station.SetSending(true);
            rig.Station.TriggerDisturbance(rig.Now);
            rig.Frame();
            AssertEq(EventAcceptance.RESYNCING, rig.Log[rig.Log.Count - 1].eventResult, "first line after the outage: RESYNCING");
            AssertEq(PresenceLevel.NEAR, rig.World.GetCurrentWorldState().physical.presence, "state is used at once");
            // The event rides on 3 lines (100 ms apart). The first two arrive
            // during resync; the third follows 2 in-order lines and is accepted.
            for (int i = 0; i < 4; i++)
                rig.Frame();
            AssertEq(2, rig.Count(EventAcceptance.RESYNCING), "two copies arrived while resynchronising");
            AssertEq(1, rig.Count(EventAcceptance.ACCEPTED), "third copy, after 2 in-order lines, accepted");
            AssertEq(1, rig.PulsesStarted, "one pulse");
        }

        private static void T10()
        {
            var rig = new Rig();
            rig.Settle();
            long firstBoot = rig.Station.Boot;
            rig.Station.TriggerDisturbance(rig.Now);
            rig.Settle();
            AssertEq(1, rig.PulsesStarted, "event 1 of the first boot");

            rig.Station.Reboot(rig.Now);
            AssertTrue(rig.Station.Boot != firstBoot, "new boot id");
            rig.Station.TriggerDisturbance(rig.Now); // event 1 again, new boot
            rig.Frame();
            PacketOutcome firstOfBoot = rig.Log[rig.Log.Count - 1];
            AssertTrue(firstOfBoot.accepted && firstOfBoot.isNewBoot && firstOfBoot.sequence == 1, "seq 1 of the new boot accepted, ESP32_REBOOT flagged");
            AssertEq(EventAcceptance.RESYNCING, firstOfBoot.eventResult, "event waits for resync");
            AssertEq(1L, rig.Pipeline.Reboots, "reboot counted");

            rig.Settle(3);
            rig.Station.TriggerDisturbance(rig.Now); // event 2 of the new boot
            rig.Frame();
            AssertEq(2, rig.PulsesStarted, "first new-boot event after resync accepted (P3/P4 end to end)");
        }

        private static void Blackout()
        {
            var rig = new Rig();
            rig.Settle();
            rig.Pipeline.BeginEventBlackout(rig.Now, 1000);
            rig.Station.TriggerDisturbance(rig.Now);
            rig.Frame();
            AssertEq(EventAcceptance.BLACKOUT, rig.Log[rig.Log.Count - 1].eventResult, "blackout");
            for (int i = 0; i < 20; i++)
                rig.Frame();
            rig.Station.TriggerDisturbance(rig.Now);
            rig.Frame();
            AssertEq(EventAcceptance.ACCEPTED, rig.Log[rig.Log.Count - 1].eventResult, "accepted after 1 s");
        }
    }
}
