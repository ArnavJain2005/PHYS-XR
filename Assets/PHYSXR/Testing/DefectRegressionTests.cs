using System;
using System.Text;
using PHYSXR.Communication;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.Decision;
using PHYSXR.PhysicalWorld;
using PHYSXR.WorldStateSystem;

namespace PHYSXR.Testing
{
    // Regression tests for the five pipeline defects found in the Technical
    // Spec review (P1-P5, test T29) and the socket-thread hand-off. Each fix
    // has a "legacy" case that still shows the original behaviour through the
    // unchanged legacy API (so the defect is demonstrably what the probe
    // found) and a "fixed" case through the API the game now uses.
    public static class DefectRegressionTests
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

            Run("P1-01_Legacy_FrozenSensorTimestamp_StaysInWarn", P1_01);
            Run("P1-02_Clock_FrozenSensorTimestamp_DeescalatesAfterDwell", P1_02);
            Run("P1-03_Clock_ProtectExitsAfterDwellDespiteFrozenTimestamp", P1_03);
            Run("P1-04_Clock_DwellNotElapsed_StillRetains", P1_04);
            Run("P1-05_Clock_EncounterLayer_UsesSameClock", P1_05);
            Run("P2-01_Legacy_SilentSensor_KeepsVeryNearInWorldState", P2_01);
            Run("P2-02_Gated_StaleSource_NeutralPhysicalState", P2_02);
            Run("P2-03_Gated_FreshSource_LivePresencePassesThrough", P2_03);
            Run("P2-04_Gated_VirtualOnlyMask_NeutralEvenWhenFresh", P2_04);
            Run("P2-05_Gated_RawDisturbanceFlagIgnored_PulseOnlyFromEvents", P2_05);
            Run("P2-06_Gated_PulseEndsAfter3s_AndCarriesCauseEvent", P2_06);
            Run("P2-07_Gated_StaleSource_NoProtectFromOldVeryNear", P2_07);
            Run("P3-01_Reboot_NewBootSequenceOneAccepted", P3_01);
            Run("P3-02_LegacyPackets_SingleSequenceUnchanged", P3_02);
            Run("P3-03_PacketFromEarlierBoot_RejectedOldBoot", P3_03);
            Run("P3-04_VersionAndForeignDevice_Rejected", P3_04);
            Run("P4-01_SameEventIdNewBoot_Accepted", P4_01);
            Run("P4-02_SameEventIdSameBoot_Duplicate", P4_02);
            Run("P4-03_LowerEventIdSameBoot_OldEvent", P4_03);
            Run("P5-01_DeviceMillis_AcceptedOnArrivalTime", P5_01);
            Run("P5-02_Legacy_DeviceMillis_JudgedStale", P5_02);
            Run("P5-03_FreshStaleUnavailable_FromArrivalTime", P5_03);
            Run("P5-04_LateTransit_RejectedStalePacket_NotCommitted", P5_04);
            Run("Q-01_QueueMode_SocketPathDoesNotInvokeHandlers", Q_01);
            Run("Q-02_QueueMode_BoundedCapacity_DropsOldest", Q_02);
            Run("Q-03_OversizedDatagram_BadSize", Q_03);

            Console.WriteLine("DefectRegressionTests: " + passed + " passed, " + failed + " failed");
            return failed;
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

        private static WorldState WarnThenCalm(float threat, long frozenTimestamp)
        {
            return new WorldState
            {
                physical = new PhysicalState { presence = PresenceLevel.NONE, confidence = ConfidenceLevel.LOW, timestamp = frozenTimestamp },
                player = new PlayerState { distanceToGhost = 8f },
                virtualWorld = new VirtualState { threatLevel = threat },
                ghost = new GhostState()
            };
        }

        // --------------------------------------------------------------- P1

        private static void P1_01()
        {
            // The T29 probe: WARN at threat 0.5, then 10,000 calm ticks with a
            // sensor timestamp that never moves.
            var provider = new RuleBasedDecisionProvider();
            AssertEq(GhostAction.WARN, provider.Decide(WarnThenCalm(0.5f, 5000)), "R7 WARN");
            GhostAction last = GhostAction.WARN;
            for (int i = 0; i < 10000; i++)
                last = provider.Decide(WarnThenCalm(0f, 5000));
            AssertEq(GhostAction.WARN, last, "legacy mode is still frozen (documents P1)");
            AssertTrue(provider.LastDecisionReason.Contains("elapsed=0ms"), "reason shows the frozen dwell: " + provider.LastDecisionReason);
        }

        private static void P1_02()
        {
            long now = 0;
            var provider = new RuleBasedDecisionProvider(decisionClock: () => now);
            AssertEq(GhostAction.WARN, provider.Decide(WarnThenCalm(0.5f, 5000)), "R7 WARN");
            GhostAction last = GhostAction.WARN;
            for (int i = 0; i < 40; i++)
            {
                now += 50;
                last = provider.Decide(WarnThenCalm(0f, 5000));
            }
            AssertEq(GhostAction.FOLLOW, last, "2 s of decision-clock time de-escalates despite the frozen sensor timestamp");
        }

        private static void P1_03()
        {
            long now = 0;
            var provider = new RuleBasedDecisionProvider(decisionClock: () => now);
            var protect = new WorldState
            {
                physical = new PhysicalState { presence = PresenceLevel.VERY_NEAR, confidence = ConfidenceLevel.HIGH, timestamp = 7 },
                player = new PlayerState { inThreatZone = true },
                virtualWorld = new VirtualState(),
                ghost = new GhostState()
            };
            AssertEq(GhostAction.PROTECT, provider.Decide(protect), "R1 PROTECT");
            now = 1600;
            AssertEq(GhostAction.FOLLOW, provider.Decide(WarnThenCalm(0f, 7)), "PROTECT exits after the 1.5 s dwell");
        }

        private static void P1_04()
        {
            long now = 0;
            var provider = new RuleBasedDecisionProvider(decisionClock: () => now);
            provider.Decide(WarnThenCalm(0.5f, 99999));
            now = 1000;
            AssertEq(GhostAction.WARN, provider.Decide(WarnThenCalm(0f, 99999)), "1.0 s < dwell: WARN retained");
            AssertTrue(provider.LastDecisionReason.Contains("elapsed=1000ms"), "reason uses the clock: " + provider.LastDecisionReason);
        }

        private static void P1_05()
        {
            long now = 0;
            var layer = new EncounterDecisionLayer(new RuleBasedDecisionProvider(decisionClock: () => now), () => now);
            layer.Decide(WarnThenCalm(0.5f, 1));
            now = 2000;
            AssertEq(GhostAction.FOLLOW, layer.Decide(WarnThenCalm(0f, 1)), "outer layer de-escalates on the shared clock");
        }

        // --------------------------------------------------------------- P2

        private static PhysicalStateManager VeryNear(long ts = 1000)
        {
            var physical = new PhysicalStateManager();
            physical.UpdateState(new PhysicalState { presence = PresenceLevel.VERY_NEAR, confidence = ConfidenceLevel.HIGH, disturbance = true, timestamp = ts });
            return physical;
        }

        private static void P2_01()
        {
            var world = new WorldStateManager(VeryNear());
            AssertEq(PresenceLevel.VERY_NEAR, world.GetCurrentWorldState().physical.presence, "legacy pass-through (documents P2)");
        }

        private static void P2_02()
        {
            long now = 0;
            var world = new WorldStateManager(VeryNear(), () => now);
            foreach (PhysicalSourceStatus status in new[] { PhysicalSourceStatus.STALE, PhysicalSourceStatus.UNAVAILABLE })
            {
                world.UpdateSourceStatus(status);
                PhysicalState p = world.GetCurrentWorldState().physical;
                AssertEq(PresenceLevel.NONE, p.presence, status + " presence");
                AssertEq(ConfidenceLevel.LOW, p.confidence, status + " confidence");
                AssertTrue(!p.disturbance, status + " disturbance");
            }
        }

        private static void P2_03()
        {
            long now = 0;
            var world = new WorldStateManager(VeryNear(), () => now);
            world.UpdateSourceStatus(PhysicalSourceStatus.FRESH);
            PhysicalState p = world.GetCurrentWorldState().physical;
            AssertEq(PresenceLevel.VERY_NEAR, p.presence, "fresh presence");
            AssertEq(ConfidenceLevel.HIGH, p.confidence, "fresh confidence");
            AssertEq(1000L, p.timestamp, "device timestamp kept for logging");
        }

        private static void P2_04()
        {
            long now = 0;
            var world = new WorldStateManager(VeryNear(), () => now) { PhysicalInputMasked = true };
            world.UpdateSourceStatus(PhysicalSourceStatus.FRESH);
            world.NotifyAcceptedEvent(new PhysicalEvent { eventId = 3 });
            PhysicalState p = world.GetCurrentWorldState().physical;
            AssertEq(PresenceLevel.NONE, p.presence, "masked presence");
            AssertTrue(!p.disturbance, "masked pulse");
        }

        private static void P2_05()
        {
            long now = 0;
            var world = new WorldStateManager(VeryNear(), () => now);
            world.UpdateSourceStatus(PhysicalSourceStatus.FRESH);
            AssertTrue(!world.GetCurrentWorldState().physical.disturbance, "raw per-line disturbance=true is not a pulse");
            world.NotifyAcceptedEvent(new PhysicalEvent { eventId = 9 });
            AssertTrue(world.GetCurrentWorldState().physical.disturbance, "accepted event starts the pulse");
        }

        private static void P2_06()
        {
            long now = 10000;
            var world = new WorldStateManager(VeryNear(), () => now);
            world.UpdateSourceStatus(PhysicalSourceStatus.FRESH);
            world.NotifyAcceptedEvent(new PhysicalEvent { eventId = 17 });
            now = 12999;
            PhysicalState during = world.GetCurrentWorldState().physical;
            AssertTrue(during.disturbance && during.causeEventId == 17, "pulse active at 2.999 s with cause 17");
            now = 13000;
            PhysicalState after = world.GetCurrentWorldState().physical;
            AssertTrue(!after.disturbance && after.causeEventId == 0, "pulse over at 3.0 s");
        }

        private static void P2_07()
        {
            long now = 0;
            var world = new WorldStateManager(VeryNear(), () => now);
            world.UpdatePlayerState(new PlayerState { inThreatZone = true, distanceToGhost = 2f });
            world.UpdateSourceStatus(PhysicalSourceStatus.STALE);
            var provider = new RuleBasedDecisionProvider(decisionClock: () => now);
            AssertTrue(provider.Decide(world.GetCurrentWorldState()) != GhostAction.PROTECT, "stale VERY_NEAR cannot trigger R1");
        }

        // ------------------------------------------------------------ P3/P4

        private static byte[] V1(long boot, long seq, long ts, string presence = "NEAR", long eventId = 0, long tEvent = 0, string dev = "phys-xr-01", string v = "1")
        {
            string text = "v=" + v + ";dev=" + dev + ";boot=" + boot + ";seq=" + seq + ";ts=" + ts + ";presence=" + presence
                + ";confidence=HIGH;disturbance=" + (eventId != 0 ? "true" : "false")
                + ";eventType=" + (eventId != 0 ? "DISTURBANCE" : "") + ";eventId=" + eventId + ";tEvent=" + tEvent;
            return Encoding.UTF8.GetBytes(text);
        }

        private static void P3_01()
        {
            var validator = new PacketValidator("phys-xr-01");
            AssertTrue(validator.Validate(V1(111, 500, 50000)).isValid, "boot A seq 500");
            ValidationResult reboot = validator.Validate(V1(222, 1, 20));
            AssertTrue(reboot.isValid, "boot B seq 1 accepted: " + reboot.failureReason);
            AssertTrue(reboot.isNewBoot, "flagged as a new boot");
            AssertTrue(validator.Validate(V1(222, 2, 120)).isValid, "boot B continues");
            AssertEq(PacketRejectCode.OUT_OF_ORDER, validator.Validate(V1(222, 2, 130)).code, "per-boot ordering still enforced");
        }

        private static void P3_02()
        {
            var validator = new PacketValidator();
            AssertTrue(validator.Validate(ProvisionalTextPacketFormat.Encode(5, 1000, "NONE", "LOW", false, "", 0)).isValid, "legacy seq 5");
            ValidationResult again = validator.Validate(ProvisionalTextPacketFormat.Encode(1, 1100, "NONE", "LOW", false, "", 0));
            AssertTrue(!again.isValid && again.failureReason == "stale or duplicate sequence number", "legacy behaviour and reason unchanged");
        }

        private static void P3_03()
        {
            var validator = new PacketValidator();
            validator.Validate(V1(111, 10, 1000));
            validator.Validate(V1(222, 1, 10));
            AssertEq(PacketRejectCode.OLD_BOOT, validator.Validate(V1(111, 11, 1100)).code, "late packet from boot A");
        }

        private static void P3_04()
        {
            var validator = new PacketValidator("phys-xr-01");
            AssertEq(PacketRejectCode.VERSION_UNSUPPORTED, validator.Validate(V1(1, 1, 10, v: "2")).code, "v=2");
            AssertEq(PacketRejectCode.FOREIGN_DEVICE, validator.Validate(V1(1, 1, 10, dev: "other-station")).code, "foreign dev");
            AssertTrue(validator.Validate(V1(1, 1, 10)).isValid, "own device accepted afterwards");
        }

        private static void P4_01()
        {
            var events = new PhysicalEventManager();
            AssertEq(PhysicalEventManager.Verdict.New, events.Accept(new PhysicalEvent { boot = 111, eventId = 1 }), "boot A event 1");
            AssertEq(PhysicalEventManager.Verdict.New, events.Accept(new PhysicalEvent { boot = 222, eventId = 1 }), "boot B event 1 is new (P4)");
            AssertTrue(events.HasProcessedEvent(222, 1), "keyed on (boot, id)");
        }

        private static void P4_02()
        {
            var events = new PhysicalEventManager();
            events.Accept(new PhysicalEvent { boot = 5, eventId = 17 });
            AssertEq(PhysicalEventManager.Verdict.Duplicate, events.Accept(new PhysicalEvent { boot = 5, eventId = 17 }), "same boot + id");
            AssertTrue(!events.RegisterEvent(new PhysicalEvent { boot = 5, eventId = 17 }), "legacy API agrees");
        }

        private static void P4_03()
        {
            var events = new PhysicalEventManager();
            events.Accept(new PhysicalEvent { boot = 5, eventId = 17 });
            AssertEq(PhysicalEventManager.Verdict.Old, events.Accept(new PhysicalEvent { boot = 5, eventId = 16 }), "16 after 17");
        }

        // --------------------------------------------------------------- P5

        private static PhysicalCommunicationPipeline Live(out PhysicalStateManager state, out PhysicalEventManager events)
        {
            state = new PhysicalStateManager();
            events = new PhysicalEventManager();
            return new PhysicalCommunicationPipeline(new PacketValidator("phys-xr-01"), new PhysicalStateParser(), state, events);
        }

        private static void P5_01()
        {
            var pipeline = Live(out var state, out _);
            // ESP32 millis() (~1 s after boot) arriving at Unity Stopwatch 987654 ms.
            PacketOutcome outcome = pipeline.Process(V1(7, 1, 1043), 987654);
            AssertTrue(outcome.accepted, "accepted: " + outcome.code + " " + outcome.reason);
            AssertTrue(state.GetCurrentState() != null, "committed");
            AssertEq(PhysicalSourceStatus.FRESH, pipeline.GetSourceStatus(987700), "fresh");
        }

        private static void P5_02()
        {
            var state = new PhysicalStateManager();
            var pipeline = new PhysicalCommunicationPipeline(new PacketValidator(), new PhysicalStateParser(), state, new PhysicalEventManager());
            pipeline.HandleRawPacket(V1(7, 1, 1043), 987654);
            AssertTrue(state.GetCurrentState() == null, "legacy path rejects device millis as stale (documents P5)");
        }

        private static void P5_03()
        {
            var pipeline = Live(out _, out _);
            AssertEq(PhysicalSourceStatus.UNAVAILABLE, pipeline.GetSourceStatus(0), "never connected");
            pipeline.Process(V1(7, 1, 100), 10000);
            AssertEq(PhysicalSourceStatus.FRESH, pipeline.GetSourceStatus(10500), "500 ms");
            AssertEq(PhysicalSourceStatus.STALE, pipeline.GetSourceStatus(10501), "501 ms");
            AssertEq(PhysicalSourceStatus.STALE, pipeline.GetSourceStatus(13000), "3 s");
            AssertEq(PhysicalSourceStatus.UNAVAILABLE, pipeline.GetSourceStatus(13001), "> 3 s");
        }

        private static void P5_04()
        {
            var pipeline = Live(out var state, out _);
            pipeline.Process(V1(7, 1, 1000, presence: "NONE"), 50000);
            pipeline.Process(V1(7, 2, 1100, presence: "NONE"), 50100);
            // Built at device 1200 but arrives 400 ms later than the envelope says.
            PacketOutcome late = pipeline.Process(V1(7, 3, 1200, presence: "VERY_NEAR", eventId: 4, tEvent: 1190), 50600);
            AssertEq(PacketRejectCode.STALE_PACKET, late.code, "late line");
            AssertEq(400L, late.transitAgeMs, "transit age");
            AssertEq(PresenceLevel.NONE, state.GetCurrentState().presence, "state not overwritten");
            AssertTrue(late.acceptedEvent == null, "no event from a stale line");
            // Freshness still counts from the last good line (50100), not 50600.
            AssertEq(PhysicalSourceStatus.STALE, pipeline.GetSourceStatus(50601), "stale line does not refresh freshness");
        }

        // ------------------------------------------------------- thread hand-off

        private static void Q_01()
        {
            var receiver = new UDPReceiver(0, () => 42) { QueueOnReceive = true };
            int handlerCalls = 0;
            receiver.PacketReceived += _ => handlerCalls++;
            receiver.Dispatch(new byte[] { 1, 2, 3 });
            AssertEq(0, handlerCalls, "socket path does not run handlers");
            AssertTrue(receiver.TryDequeue(out UDPReceiver.Datagram d) && d.arrivalMs == 42 && d.data.Length == 3, "stamped and queued");
            AssertTrue(!receiver.TryDequeue(out _), "queue drained");
        }

        private static void Q_02()
        {
            var receiver = new UDPReceiver(0, () => 0, queueCapacity: 4) { QueueOnReceive = true };
            for (byte i = 0; i < 6; i++)
                receiver.Enqueue(new[] { i }, i);
            AssertEq(4, receiver.QueuedCount, "bounded");
            AssertEq(2L, receiver.DroppedDatagrams, "drops counted");
            receiver.TryDequeue(out UDPReceiver.Datagram first);
            AssertEq((byte)2, first.data[0], "oldest dropped first");
        }

        private static void Q_03()
        {
            var validator = new PacketValidator();
            AssertEq(PacketRejectCode.BAD_SIZE, validator.Validate(new byte[600]).code, "600-byte datagram");
        }
    }
}
