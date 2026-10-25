using System;
using System.Collections.Generic;
using System.Text;
using PHYSXR.Communication;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.Decision;
using PHYSXR.Ghost;
using PHYSXR.PhysicalWorld;
using PHYSXR.Telemetry;
using PHYSXR.WorldStateSystem;

namespace PHYSXR.Testing
{
    // The sensor-response profile (SensorResponseProfile, X1-X3 in
    // EncounterDecisionLayer) and the in-game proximity attack (E6), driven
    // end to end: contract v1 lines exactly as
    // the firmware prints them -> UDPReceiver queue -> SensorFeed ->
    // PacketValidator (serial-link rules) -> pipeline -> gated
    // WorldStateManager -> EncounterDecisionLayer(R1-R8). One 50 ms frame per
    // step on an injected clock. The distance classes themselves (30/35 and
    // 80/90 cm hysteresis) are firmware behaviour, covered by
    // Firmware/tests/semantics_test.cpp; these tests start at the semantic line.
    public static class SensorResponseProfileTests
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

            Run("EXH-01_ProfileOff_VeryNearLow_UnchangedFollow", ProfileOff);
            Run("EXH-02_ToFOnlyVeryNear_WarnX2_AfterConfirm_ConfidenceStaysLow", VeryNearWarn);
            Run("EXH-03_BlipShorterThanConfirm_NoReaction", Blip);
            Run("EXH-04_HoldThenRelease_NoFlicker", HoldAndRelease);
            Run("EXH-05_CrossingsBothWays_NearVeryNearNone", Crossings);
            Run("EXH-06_ToFOnlyNear_R4Unchanged", NearUnchanged);
            Run("EXH-07_ThreatZone_ProtectX1_CappedByE2", ProtectCapped);
            Run("EXH-08_HighConfidence_R1Wins_X2OutsideZone", HighConfidence);
            Run("EXH-09_StaleAndUnavailable_NoNewReaction", Stale);
            Run("EXH-10_VirtualOnlyMasked_NoReaction", Masked);
            Run("EXH-11_Shake_R5Warn_DuplicatesIgnored", Shake);
            Run("EXH-12_Reboot_StateUsedAtOnce_EventsResync", Reboot);
            Run("EXH-13_NeverCausesAttack", NoAttack);
            Run("EXH-14_DefaultProfile_NoProtectInZone_Warns", ProtectDisabled);
            Run("EXH-15_Reset_ClearsSensorResponseState", ResetClears);
            Run("SR-16_ShakeNear_InvestigateX3_Once_ThenWarnR4", ShakeNearInvestigates);
            Run("SR-17_ShakeVeryNear_InvestigateX3_HighConfidenceKeepsR3", ShakeVeryNearInvestigates);
            Run("SR-18_InvestigateDisabledOrStandard_ShakeNearWarns", InvestigateDisabled);
            Run("SR-19_ShakeMaskedOrNothingNear_NoX3", ShakeMasked);
            Run("SR-20_SensorsNeverAttack_ProximityOn", SensorsNeverAttack);
            Run("SR-21_X3HeldForPulse_ToFFlickerAndHysteresis", ShakeFlickerHeld);
            Run("SR-22_X3Held_R1R2R3StillWin", ShakeLatchOutranked);
            Run("SR-23_X3Held_EndsWhenLinkGoesStale", ShakeLatchStale);
            Run("SR-24_X3Held_SecondShakeExtendsSearch", ShakeLatchExtended);
            Run("SR-25_NearHeldThenVeryNear_NoFollowGap_EscalatesToX2", NearThenVeryNearNoGap);
            Run("SR-26_InThreatZone_NearToVeryNearHigh_ProtectR1NextLine", NearToVeryNearInZone);
            Run("SR-27_LinkLostAndBack_NotFrozen_ReactsAgain_EventAfterResync", LinkLostAndBack);
            Run("SR-28_VeryNearInsideInnerDwell_X2AtOnce", VeryNearInsideDwell);
            Run("SR-29_ShakeInsideInnerDwell_X3AtOnce", ShakeInsideDwell);
            Run("SR-30_InnerDwellHoldingSafety_NotReplacedByX2OrX3", DwellHoldingSafety);
            Run("SR-31_SecondShakeDuringSearch_LastCausedEventIdAdvances_NoNewDecision", SecondShakeAttributed);
            Run("SR-32_VeryNearEscalationInterruptsSearch", VeryNearEscalationInterruptsSearch);
            Run("SR-33_ThreatZoneVeryNearInterruptsSearch", ThreatZoneVeryNearInterruptsSearch);
            Run("SR-34_RepeatedShakesMaintainSearch", RepeatedShakesMaintainSearch);
            Run("E6-01_OutsideZone_NoProximityReaction", ProximityOutside);
            Run("E6-02_ThreatZone_WarnE6_NoAttackOutOfReach", ProximityWarn);
            Run("E6-03_TooClose_AttackAfterTelegraph", ProximityAttackTelegraphed);
            Run("E6-04_Cooldown_BlocksAttack_NoPerFrameRepeat", ProximityCooldown);
            Run("E6-05_GraceBeatTerminalInterrupted_NoAttack", ProximityBlocked);
            Run("E6-06_ProximityOff_LegacyFollow", ProximityOff);
            Run("E6-07_VirtualOnlyMasked_StillAttacks", ProximityMasked);
            Run("E6-08_ShakeInZone_InvestigateDelaysAttack", ShakeDelaysAttack);
            Run("E6-09_Committed_E3NotE6", ProximityCommitted);

            Console.WriteLine("SensorResponseProfileTests: " + passed + " passed, " + failed + " failed");
            return failed;
        }

        private sealed class Rig
        {
            public long Now = 100000;
            private long bootAt;
            private long seq;
            private long boot = 2189617609;
            public readonly UDPReceiver Queue;
            public readonly PhysicalStateManager Physical = new PhysicalStateManager();
            public readonly PhysicalCommunicationPipeline Pipeline;
            public readonly SensorFeed Feed;
            public readonly WorldStateManager World;
            public readonly EncounterDecisionLayer Layer;
            public readonly List<PacketOutcome> Log = new List<PacketOutcome>();
            public int Pulses;

            // Optional: the game's pipeline around Layer (change-only
            // telemetry), so attribution can be checked. Frame then runs it.
            public GhostRuntimePipeline GhostPipeline;
            public readonly InMemoryTelemetrySink Telemetry = new InMemoryTelemetrySink();

            public void UsePipeline()
            {
                GhostPipeline = new GhostRuntimePipeline(Layer, new GhostActionExecutor(), new GhostBehaviourController())
                {
                    ChangeOnlyTelemetry = true,
                    TelemetrySink = Telemetry
                };
            }

            public int CountRecords(string type, string detailContains = null)
            {
                int n = 0;
                foreach (TelemetryRecord r in Telemetry.Records)
                    if (r.type == type && (detailContains == null || (r.detail ?? string.Empty).Contains(detailContains)))
                        n++;
                return n;
            }

            public Rig(SensorResponseProfile profile, bool masked = false, bool proximity = false)
            {
                bootAt = Now - 1;
                Queue = new UDPReceiver(0, () => Now);
                Pipeline = new PhysicalCommunicationPipeline(
                    new PacketValidator("phys-xr-01") { RequireVersion = true, RequireDeviceId = true },
                    new PhysicalStateParser(), Physical, new PhysicalEventManager()) { EventsMasked = masked };
                Feed = new SensorFeed(Queue, Pipeline);
                World = new WorldStateManager(Physical, () => Now) { PhysicalInputMasked = masked };
                Layer = new EncounterDecisionLayer(new RuleBasedDecisionProvider(decisionClock: () => Now), () => Now)
                {
                    SensorResponse = profile,
                    ProximityAttack = proximity
                };
                Player(8f, false);
                World.UpdateVirtualState(new VirtualState { threatLevel = 0.2f });
                World.UpdateGhostState(new GhostState());
            }

            public void Player(float distance, bool inThreatZone)
            {
                World.UpdatePlayerState(new PlayerState { distanceToGhost = distance, inThreatZone = inThreatZone });
            }

            // physxr::formatStateLine output for the current device time.
            public string Line(string presence, string confidence = "LOW", long eventId = 0)
            {
                seq++;
                long ts = Now - bootAt;
                bool e = eventId != 0;
                return "v=1;dev=phys-xr-01;boot=" + boot + ";seq=" + seq + ";ts=" + ts + ";presence=" + presence
                       + ";confidence=" + confidence + ";disturbance=" + (e ? "true" : "false") + ";eventType=" + (e ? "DISTURBANCE" : "")
                       + ";eventId=" + eventId + ";tEvent=" + (e ? ts : 0);
            }

            public void Reboot(long newBoot)
            {
                boot = newBoot;
                seq = 0;
                bootAt = Now - 1;
            }

            // One 50 ms frame; a line is sent every other frame (10 Hz) unless text is null.
            public GhostAction Frame(string text)
            {
                Now += 50;
                if (text != null)
                    Queue.Enqueue(Encoding.UTF8.GetBytes(text), Now);
                foreach (PacketOutcome o in Feed.Pump(Now))
                {
                    Log.Add(o);
                    if (o.acceptedEvent != null && World.NotifyAcceptedEvent(o.acceptedEvent))
                        Pulses++;
                }
                World.UpdateSourceStatus(Feed.Status);
                if (GhostPipeline == null)
                    return Layer.Decide(World.GetCurrentWorldState());
                GhostPipeline.Run(World.GetCurrentWorldState(), Now);
                return GhostPipeline.LastAction ?? GhostAction.FOLLOW;
            }

            // Holds a physical state for ms (10 Hz lines, 20 Hz decisions); returns every decision.
            public List<GhostAction> Hold(string presence, long ms, string confidence = "LOW")
            {
                var actions = new List<GhostAction>();
                for (long t = 0; t < ms; t += 50)
                    actions.Add(Frame(t % 100 == 0 ? Line(presence, confidence) : null));
                return actions;
            }

            public int Count(EventAcceptance result)
            {
                int n = 0;
                foreach (PacketOutcome o in Log)
                    if (o.eventResult == result)
                        n++;
                return n;
            }
        }

        private static SensorResponseProfile On() => new SensorResponseProfile();

        // -physxr-exhibition: X1 on as well.
        private static SensorResponseProfile Exhibition() => new SensorResponseProfile(protectInThreatZone: true);

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

        private static int Transitions(List<GhostAction> actions)
        {
            int n = 0;
            for (int i = 1; i < actions.Count; i++)
                if (actions[i] != actions[i - 1])
                    n++;
            return n;
        }

        // Settles the link (FRESH, resync done) with nothing in range.
        private static Rig Ready(SensorResponseProfile profile, bool masked = false, bool proximity = false)
        {
            var rig = new Rig(profile, masked, proximity);
            rig.Hold("NONE", 600);
            if (!masked)
                AssertEq(PhysicalSourceStatus.FRESH, rig.Feed.Status, "link fresh before the test");
            return rig;
        }

        // ------------------------------------------------------------ tests

        private static void ProfileOff()
        {
            Rig rig = Ready(null);
            AssertTrue(rig.Layer.SensorResponse == null, "standard rules: no profile");
            foreach (GhostAction a in rig.Hold("VERY_NEAR", 3000))
                AssertEq(GhostAction.FOLLOW, a, "baseline: single-sensor VERY_NEAR matches no rule");
            rig.Player(3f, true);
            foreach (GhostAction a in rig.Hold("VERY_NEAR", 3000))
                AssertEq(GhostAction.FOLLOW, a, "baseline: R1 still needs HIGH confidence");
        }

        private static void VeryNearWarn()
        {
            Rig rig = Ready(On());
            List<GhostAction> actions = rig.Hold("VERY_NEAR", 1000);
            int first = actions.IndexOf(GhostAction.WARN);
            AssertTrue(first >= 0, "X2 reacts: " + string.Join(",", actions));
            long reactMs = (first + 1) * 50;
            AssertTrue(reactMs >= SensorResponseProfile.DefaultConfirmMs && reactMs <= SensorResponseProfile.DefaultConfirmMs + 150,
                "reaction after the 250 ms confirmation (+ one line interval): " + reactMs + " ms");
            AssertEq("X2", rig.Layer.LastRuleId, "rule id");
            AssertTrue(rig.Layer.LastDecisionReason.StartsWith("WARN[X2]") && rig.Layer.LastDecisionReason.Contains("confidence LOW"),
                "reason: " + rig.Layer.LastDecisionReason);
            AssertEq(ConfidenceLevel.LOW, rig.World.GetCurrentWorldState().physical.confidence, "confidence never raised");
            AssertEq(1, Transitions(actions), "one FOLLOW -> WARN transition, no flicker");
        }

        private static void Blip()
        {
            Rig rig = Ready(On());
            var actions = new List<GhostAction>();
            actions.AddRange(rig.Hold("VERY_NEAR", 200));
            actions.AddRange(rig.Hold("NONE", 3000));
            AssertTrue(!actions.Contains(GhostAction.WARN), "a 200 ms reading is debounced: " + string.Join(",", actions));
        }

        private static void HoldAndRelease()
        {
            Rig rig = Ready(On());
            rig.Hold("VERY_NEAR", 600);
            AssertEq(GhostAction.WARN, rig.Frame(null), "reacting");
            List<GhostAction> after = rig.Hold("NONE", 3000);
            int released = after.IndexOf(GhostAction.FOLLOW);
            AssertTrue(released > 0, "released eventually");
            long heldMs = released * 50;
            AssertTrue(heldMs >= SensorResponseProfile.DefaultHoldMs - 100 && heldMs <= SensorResponseProfile.DefaultHoldMs + 200,
                "held about 1.5 s after the last VERY_NEAR: " + heldMs + " ms");
            AssertEq(1, Transitions(after), "WARN -> FOLLOW once, no flicker");
        }

        private static void Crossings()
        {
            Rig rig = Ready(On());
            var actions = new List<GhostAction>();
            actions.AddRange(rig.Hold("NEAR", 1000));       // R4 WARN
            actions.AddRange(rig.Hold("VERY_NEAR", 1500));  // X2 WARN takes over
            actions.AddRange(rig.Hold("NEAR", 1000));       // R4 WARN again
            int firstWarn = actions.IndexOf(GhostAction.WARN);
            for (int i = firstWarn; i < actions.Count; i++)
                AssertEq(GhostAction.WARN, actions[i], "continuous WARN across NEAR <-> VERY_NEAR, frame " + i);
            List<GhostAction> leaving = rig.Hold("NONE", 3000);
            AssertEq(GhostAction.FOLLOW, leaving[leaving.Count - 1], "back to FOLLOW after leaving");
            AssertEq(1, Transitions(leaving), "single release");
        }

        private static void NearUnchanged()
        {
            Rig on = Ready(On());
            Rig off = Ready(null);
            List<GhostAction> a = on.Hold("NEAR", 1500);
            List<GhostAction> b = off.Hold("NEAR", 1500);
            for (int i = 0; i < a.Count; i++)
                AssertEq(b[i], a[i], "NEAR decisions identical with and without the profile, frame " + i);
            AssertTrue(on.Layer.Inner.LastDecisionReason.StartsWith("WARN: physical condition"), "R4: " + on.Layer.Inner.LastDecisionReason);
            AssertEq("INNER", on.Layer.LastRuleId, "the inner rule, not X");
        }

        private static void ProtectCapped()
        {
            Rig rig = Ready(Exhibition());
            rig.Player(3f, true);
            List<GhostAction> actions = rig.Hold("VERY_NEAR", 20000);
            int first = actions.IndexOf(GhostAction.PROTECT);
            AssertTrue(first >= 0 && (first + 1) * 50 <= 400, "X1 PROTECT within 400 ms: index " + first);
            AssertTrue(rig.Layer.LastDecisionReason.Length > 0, "reason recorded");
            int protectFrames = 0;
            for (int i = first; i < actions.Count && actions[i] == GhostAction.PROTECT; i++)
                protectFrames++;
            long protectMs = protectFrames * 50;
            AssertTrue(protectMs >= EncounterDecisionLayer.DefaultProtectCapMs - 100 && protectMs <= EncounterDecisionLayer.DefaultProtectCapMs + 100,
                "capped by E2 at 6 s: " + protectMs + " ms");
            AssertEq(GhostAction.FOLLOW, actions[first + protectFrames], "FOLLOW[E2] after the cap");
            AssertTrue(actions.LastIndexOf(GhostAction.PROTECT) > first + protectFrames, "PROTECT again after the 10 s refractory");
            AssertEq(ConfidenceLevel.LOW, rig.World.GetCurrentWorldState().physical.confidence, "still LOW");
        }

        private static void HighConfidence()
        {
            Rig rig = Ready(Exhibition());
            rig.Player(3f, true);
            rig.Hold("VERY_NEAR", 500, "HIGH");
            AssertEq(GhostAction.PROTECT, rig.Frame(null), "R1 PROTECT");
            AssertTrue(rig.Layer.LastDecisionReason.StartsWith("PROTECT: player at risk"), "R1, not X1: " + rig.Layer.LastDecisionReason);

            Rig outside = Ready(On());
            List<GhostAction> actions = outside.Hold("VERY_NEAR", 1000, "HIGH");
            AssertEq(GhostAction.WARN, actions[actions.Count - 1], "X2 also covers corroborated VERY_NEAR outside the zone");
            AssertTrue(outside.Layer.LastDecisionReason.Contains("confidence HIGH"), "labelled HIGH: " + outside.Layer.LastDecisionReason);
        }

        private static void Stale()
        {
            // The last VERY_NEAR line stays FRESH for 500 ms after it arrives,
            // so a 1 s confirmation makes the data go STALE first: a reaction
            // must never start from it.
            Rig rig = Ready(new SensorResponseProfile(confirmMs: 1000));
            rig.Hold("VERY_NEAR", 200);
            var silent = new List<GhostAction>();
            for (int i = 0; i < 100; i++)
                silent.Add(rig.Frame(null));  // link silent for 5 s
            AssertTrue(!silent.Contains(GhostAction.WARN), "no reaction from a state that went stale before confirming");
            AssertEq(PhysicalSourceStatus.UNAVAILABLE, rig.Feed.Status, "unavailable");
            AssertEq(PresenceLevel.NONE, rig.World.GetCurrentWorldState().physical.presence, "neutral physical state");

            Rig held = Ready(On());
            held.Hold("VERY_NEAR", 1000);
            var gone = new List<GhostAction>();
            for (int i = 0; i < 100; i++)
                gone.Add(held.Frame(null));
            int released = gone.IndexOf(GhostAction.FOLLOW);
            AssertTrue(released >= 0 && released * 50 <= SensorResponseProfile.DefaultHoldMs + 600,
                "a running reaction ends within hold + staleness after the link stops: " + released * 50 + " ms");
            for (int i = released; i < gone.Count; i++)
                AssertEq(GhostAction.FOLLOW, gone[i], "no reaction while the source is stale/unavailable, frame " + i);
        }

        private static void Masked()
        {
            Rig rig = Ready(On(), masked: true);
            foreach (GhostAction a in rig.Hold("VERY_NEAR", 3000))
                AssertEq(GhostAction.FOLLOW, a, "virtual-only trials ignore the station");
        }

        private static void Shake()
        {
            Rig rig = Ready(On());
            // The firmware repeats an event on three lines.
            GhostAction first = rig.Frame(rig.Line("NONE", "LOW", 3));
            rig.Frame(null);
            rig.Frame(rig.Line("NONE", "LOW", 3));
            rig.Frame(null);
            rig.Frame(rig.Line("NONE", "LOW", 3));
            AssertEq(GhostAction.WARN, first, "shake -> R5 WARN in the frame the event arrives");
            AssertTrue(rig.Layer.Inner.LastDecisionReason.StartsWith("WARN: disturbance"), "R5: " + rig.Layer.Inner.LastDecisionReason);
            AssertEq(1, rig.Count(EventAcceptance.ACCEPTED), "accepted once");
            AssertEq(2, rig.Count(EventAcceptance.DUPLICATE), "repeats ignored");
            AssertEq(1, rig.Pulses, "one 3 s disturbance pulse");
            rig.Frame(rig.Line("NONE", "LOW", 3));
            AssertEq(1, rig.Pulses, "a resent event never starts a second pulse");
        }

        private static void Reboot()
        {
            Rig rig = Ready(On());
            rig.Reboot(77);
            rig.Frame(rig.Line("NONE", "LOW", 1));
            AssertEq(EventAcceptance.RESYNCING, rig.Log[rig.Log.Count - 1].eventResult, "an event right after a reboot waits for resync");
            AssertEq(1L, rig.Pipeline.Reboots, "reboot recognised");
            AssertEq(0, rig.Pulses, "no pulse from the resyncing event");
            List<GhostAction> after = rig.Hold("VERY_NEAR", 1000);
            AssertTrue(after.Contains(GhostAction.WARN), "state after a reboot is used at once: " + string.Join(",", after));
        }

        private static void NoAttack()
        {
            Rig rig = Ready(On());
            rig.Player(1.2f, true);
            foreach (GhostAction a in rig.Hold("VERY_NEAR", 20000))
                AssertTrue(a != GhostAction.ATTACK, "the profile never causes an attack: " + rig.Layer.LastDecisionReason);

            Rig committed = Ready(Exhibition());
            committed.Player(1.2f, true);
            committed.World.UpdateVirtualState(new VirtualState { threatLevel = 0.2f, encounterCommitted = true });
            foreach (GhostAction a in committed.Hold("VERY_NEAR", 8000))
                AssertTrue(a != GhostAction.ATTACK, "X1 PROTECT blocks E3 like any PROTECT: " + committed.Layer.LastDecisionReason);
        }

        private static void ProtectDisabled()
        {
            Rig rig = Ready(On());
            rig.Player(3f, true);
            List<GhostAction> actions = rig.Hold("VERY_NEAR", 1000);
            AssertTrue(!actions.Contains(GhostAction.PROTECT), "X1 is off unless -physxr-exhibition");
            AssertEq(GhostAction.WARN, actions[actions.Count - 1], "WARN instead");
        }

        private static void ResetClears()
        {
            Rig rig = Ready(On());
            rig.Hold("VERY_NEAR", 600);
            rig.Layer.Reset();
            List<GhostAction> next = rig.Hold("NONE", 300);
            AssertTrue(!next.Contains(GhostAction.WARN), "no hold survives a checkpoint reset");
        }

        // ------------------------------------------------- X3 (disturbance)

        // The firmware repeats an event on three lines, at 10 Hz.
        private static List<GhostAction> Shake(Rig rig, string presence, long eventId, string confidence = "LOW")
        {
            var actions = new List<GhostAction>();
            actions.Add(rig.Frame(rig.Line(presence, confidence, eventId)));
            actions.Add(rig.Frame(null));
            actions.Add(rig.Frame(rig.Line(presence, confidence, eventId)));
            actions.Add(rig.Frame(null));
            actions.Add(rig.Frame(rig.Line(presence, confidence, eventId)));
            return actions;
        }

        private static void ShakeNearInvestigates()
        {
            Rig rig = Ready(On());
            rig.Hold("NEAR", 1000);
            AssertEq(GhostAction.WARN, rig.Frame(null), "NEAR -> R4 WARN first");
            List<GhostAction> shake = Shake(rig, "NEAR", 5);
            AssertEq(GhostAction.INVESTIGATE, shake[0], "INVESTIGATE in the frame the event arrives");
            AssertEq("X3", rig.Layer.LastRuleId, "rule id");
            AssertTrue(rig.Layer.LastDecisionReason.StartsWith("INVESTIGATE[X3]") && rig.Layer.LastDecisionReason.Contains("confidence LOW"),
                "reason: " + rig.Layer.LastDecisionReason);
            AssertEq(1, rig.Count(EventAcceptance.ACCEPTED), "accepted once");
            AssertEq(2, rig.Count(EventAcceptance.DUPLICATE), "repeats ignored");
            AssertEq(1, rig.Pulses, "one pulse");
            AssertEq(ConfidenceLevel.LOW, rig.World.GetCurrentWorldState().physical.confidence, "confidence never raised");
            List<GhostAction> after = rig.Hold("NEAR", 4000);
            int back = after.IndexOf(GhostAction.WARN);
            AssertTrue(back > 0, "back to WARN after the pulse: " + string.Join(",", after));
            for (int i = 0; i < back; i++)
                AssertEq(GhostAction.INVESTIGATE, after[i], "INVESTIGATE held for the pulse, frame " + i);
            long pulseMs = (back + shake.Count) * 50;
            AssertTrue(pulseMs >= 2900 && pulseMs <= 3200, "investigates for the 3 s pulse: " + pulseMs + " ms");
            for (int i = back; i < after.Count; i++)
                AssertEq(GhostAction.WARN, after[i], "steady WARN after, frame " + i);
            AssertEq("INNER", rig.Layer.LastRuleId, "R4 again");
        }

        private static void ShakeVeryNearInvestigates()
        {
            Rig rig = Ready(On());
            rig.Hold("VERY_NEAR", 1000);
            AssertEq("X2", rig.Layer.LastRuleId, "X2 WARN first");
            List<GhostAction> shake = Shake(rig, "VERY_NEAR", 6);
            AssertEq(GhostAction.INVESTIGATE, shake[0], "shake at VERY_NEAR -> INVESTIGATE");
            AssertEq("X3", rig.Layer.LastRuleId, "X3");

            Rig high = Ready(On());
            high.Hold("NEAR", 600, "HIGH");
            List<GhostAction> corroborated = Shake(high, "NEAR", 7, "HIGH");
            AssertEq(GhostAction.INVESTIGATE, corroborated[0], "R3 INVESTIGATE");
            AssertEq("INNER", high.Layer.LastRuleId, "the specified rule R3, not X3");
        }

        private static void InvestigateDisabled()
        {
            foreach (SensorResponseProfile profile in new[] { new SensorResponseProfile(investigateDisturbance: false), null })
            {
                Rig rig = Ready(profile);
                rig.Hold("NEAR", 1000);
                foreach (GhostAction a in Shake(rig, "NEAR", 8))
                    AssertEq(GhostAction.WARN, a, "R4 WARN unchanged (" + (profile == null ? "standard rules" : "X3 off") + ")");
                AssertEq(1, rig.Pulses, "the event is still accepted");
            }
        }

        private static void ShakeMasked()
        {
            Rig masked = Ready(On(), masked: true);
            masked.Hold("NEAR", 1000);
            foreach (GhostAction a in Shake(masked, "NEAR", 9))
                AssertEq(GhostAction.FOLLOW, a, "a virtual-only trial ignores the station");
            AssertEq(0, masked.Pulses, "no pulse while masked");

            Rig none = Ready(On());
            List<GhostAction> shake = Shake(none, "NONE", 10);
            AssertEq(GhostAction.WARN, shake[0], "nothing near: R5 WARN, not X3");
            AssertEq("INNER", none.Layer.LastRuleId, "R5");
        }

        private static void SensorsNeverAttack()
        {
            Rig rig = Ready(Exhibition(), proximity: true);
            var all = new List<GhostAction>();
            all.AddRange(rig.Hold("NEAR", 3000));
            all.AddRange(Shake(rig, "NEAR", 11));
            all.AddRange(rig.Hold("VERY_NEAR", 6000));
            all.AddRange(Shake(rig, "VERY_NEAR", 12));
            all.AddRange(rig.Hold("VERY_NEAR", 6000, "HIGH"));
            foreach (GhostAction a in all)
                AssertTrue(a != GhostAction.ATTACK, "the player is far away in the game: no sensor reading attacks");
            AssertTrue(all.Contains(GhostAction.WARN) && all.Contains(GhostAction.INVESTIGATE), "but the ghost does react");
        }

        // Lines the way a shaken station sends them: the ToF swings, so the
        // presence flickers NONE / NEAR / VERY_NEAR from line to line.
        private static readonly string[] Swing = { "NONE", "NEAR", "VERY_NEAR", "NONE", "NEAR", "NONE" };

        private static List<GhostAction> Swinging(Rig rig, long ms, int offset = 0)
        {
            var actions = new List<GhostAction>();
            for (long t = 0; t < ms; t += 50)
                actions.Add(rig.Frame(t % 100 == 0 ? rig.Line(Swing[(offset + (int)(t / 100)) % Swing.Length]) : null));
            return actions;
        }

        private static void ShakeFlickerHeld()
        {
            Rig rig = Ready(On());
            rig.Hold("NEAR", 1000);
            List<GhostAction> shake = Shake(rig, "NEAR", 21);
            AssertEq(GhostAction.INVESTIGATE, shake[0], "X3 starts");
            List<GhostAction> pulse = Swinging(rig, 2600);
            for (int i = 0; i < pulse.Count; i++)
                AssertEq(GhostAction.INVESTIGATE, pulse[i], "held through the ToF flicker, frame " + i + " (" + rig.Layer.LastDecisionReason + ")");
            AssertEq("X3", rig.Layer.LastRuleId, "still X3");
            AssertEq(1, rig.Pulses, "one pulse");

            List<GhostAction> after = Swinging(rig, 1500);
            int ended = after.FindIndex(a => a != GhostAction.INVESTIGATE);
            AssertTrue(ended >= 0, "the search ends with the pulse");
            long pulseMs = (shake.Count + pulse.Count + ended) * 50;
            AssertTrue(pulseMs >= 2900 && pulseMs <= 3200, "INVESTIGATE for the 3 s pulse: " + pulseMs + " ms");
            AssertTrue(!after.GetRange(ended, after.Count - ended).Contains(GhostAction.INVESTIGATE), "and does not come back without a new shake");

            // Nothing near for the rest of the pulse (inner R5, then hysteresis).
            Rig gone = Ready(On());
            gone.Hold("NEAR", 1000);
            Shake(gone, "NEAR", 22);
            foreach (GhostAction a in gone.Hold("NONE", 2600))
                AssertEq(GhostAction.INVESTIGATE, a, "held when the ToF loses the hand: " + gone.Layer.LastDecisionReason);
        }

        private static void ShakeLatchOutranked()
        {
            // R3: the second sensor corroborates mid-pulse; the specified rule takes over.
            Rig r3 = Ready(On());
            r3.Hold("NEAR", 1000);
            Shake(r3, "NEAR", 23);
            AssertEq("X3", r3.Layer.LastRuleId, "X3 first");
            r3.Hold("NEAR", 500, "HIGH");
            AssertEq(GhostAction.INVESTIGATE, r3.Frame(null), "R3 INVESTIGATE");
            AssertEq("INNER", r3.Layer.LastRuleId, "R3, not X3");

            // R1: player at risk and a corroborated VERY_NEAR.
            Rig r1 = Ready(On());
            r1.Player(3f, true);
            r1.Hold("NEAR", 1000);
            Shake(r1, "NEAR", 24);
            AssertEq(GhostAction.INVESTIGATE, r1.Frame(null), "X3 in the zone (inner R8)");
            r1.Hold("VERY_NEAR", 500, "HIGH");
            AssertEq(GhostAction.PROTECT, r1.Frame(null), "R1 PROTECT wins over the held search");

            // R2: critical virtual threat.
            Rig r2 = Ready(On());
            r2.Hold("NEAR", 1000);
            Shake(r2, "NEAR", 25);
            r2.World.UpdateVirtualState(new VirtualState { threatLevel = 0.95f });
            List<GhostAction> surge = Swinging(r2, 500);
            AssertEq(GhostAction.RETREAT, surge[surge.Count - 1], "R2 RETREAT wins over the held search");
            r2.World.UpdateVirtualState(new VirtualState { threatLevel = 0.2f });
        }

        private static void ShakeLatchStale()
        {
            Rig rig = Ready(On());
            rig.Hold("NEAR", 1000);
            Shake(rig, "NEAR", 26);
            var silent = new List<GhostAction>();
            for (int i = 0; i < 40; i++)
                silent.Add(rig.Frame(null)); // the link stops for 2 s
            int ended = silent.FindIndex(a => a != GhostAction.INVESTIGATE);
            AssertTrue(ended >= 0 && ended * 50 <= 700, "the search ends once the data is no longer fresh: " + ended * 50 + " ms");
            for (int i = ended; i < silent.Count; i++)
                AssertTrue(silent[i] != GhostAction.INVESTIGATE, "never held on stale data, frame " + i);
            AssertTrue(rig.Feed.Status != PhysicalSourceStatus.FRESH, "stale");
        }

        private static void ShakeLatchExtended()
        {
            Rig rig = Ready(On());
            rig.Hold("NEAR", 1000);
            var actions = new List<GhostAction>();
            actions.AddRange(Shake(rig, "NEAR", 27));
            actions.AddRange(Swinging(rig, 1800));
            // A second shake arrives on a NONE line 2 s into the first pulse.
            actions.AddRange(Shake(rig, "NONE", 28));
            List<GhostAction> rest = Swinging(rig, 4000, 1);
            actions.AddRange(rest);
            AssertEq(2, rig.Pulses, "two pulses");
            int ended = actions.FindIndex(a => a != GhostAction.INVESTIGATE);
            long heldMs = ended * 50;
            AssertTrue(heldMs >= 4900 && heldMs <= 5300, "one continuous search until 3 s after the second shake: " + heldMs + " ms");
            AssertEq(0, Transitions(actions.GetRange(0, ended)), "no flicker");
        }

        // A hand held at NEAR past the inner dwell, then moved closer. The
        // presence never lapsed, so VERY_NEAR needs no fresh confirmation:
        // the ghost escalates from R4 to X2 at once instead of dropping to
        // FOLLOW for ConfirmMs in between.
        private static void NearThenVeryNearNoGap()
        {
            Rig rig = Ready(On());
            rig.Hold("NEAR", 2500);
            AssertEq(GhostAction.WARN, rig.Frame(null), "NEAR -> R4 WARN, dwell long since elapsed");
            AssertEq("INNER", rig.Layer.LastRuleId, "R4");

            List<GhostAction> closer = rig.Hold("VERY_NEAR", 1500);
            for (int i = 0; i < closer.Count; i++)
                AssertEq(GhostAction.WARN, closer[i], "continuous WARN from NEAR into VERY_NEAR, frame " + i + " (" + rig.Layer.LastDecisionReason + ")");
            AssertEq("X2", rig.Layer.LastRuleId, "escalated to the VERY_NEAR rule");

            // The debounce still applies to a VERY_NEAR that appears from nothing.
            Rig fresh = Ready(On());
            List<GhostAction> blip = fresh.Hold("VERY_NEAR", 200);
            AssertTrue(!blip.Contains(GhostAction.WARN), "onset from NONE is still confirmed: " + string.Join(",", blip));
        }

        // Player at risk in the game: a corroborated NEAR is not a rule there,
        // a corroborated VERY_NEAR is the highest-priority physical response
        // (R1 PROTECT), applied on the first line that reports it.
        private static void NearToVeryNearInZone()
        {
            Rig rig = Ready(On());
            rig.Player(3f, true);
            foreach (GhostAction a in rig.Hold("NEAR", 1000, "HIGH"))
                AssertEq(GhostAction.FOLLOW, a, "NEAR/HIGH in the zone matches no physical rule");
            AssertEq(GhostAction.PROTECT, rig.Frame(rig.Line("VERY_NEAR", "HIGH")), "PROTECT on the first VERY_NEAR line");
            AssertEq("S", rig.Layer.LastRuleId, "the safety pass-through of R1");
            AssertTrue(rig.Layer.LastDecisionReason.StartsWith("PROTECT: player at risk"), "R1: " + rig.Layer.LastDecisionReason);
        }

        // The station is unplugged mid-reaction and comes back on the same
        // boot: the ghost lets go, reacts to the next NEAR at once, and a
        // shake sent as the very first lines after the gap is accepted once
        // the link has resynchronised, without restarting anything.
        private static void LinkLostAndBack()
        {
            Rig rig = Ready(On());
            rig.Hold("NEAR", 1000);
            AssertEq(GhostAction.WARN, rig.Frame(null), "reacting before the loss");

            var silent = new List<GhostAction>();
            for (int i = 0; i < 80; i++)
                silent.Add(rig.Frame(null)); // 4 s, no lines
            AssertEq(PhysicalSourceStatus.UNAVAILABLE, rig.Feed.Status, "unavailable after 3 s of silence");
            int released = silent.IndexOf(GhostAction.FOLLOW);
            AssertTrue(released >= 0 && released * 50 <= 2100, "released within freshness + dwell: " + released * 50 + " ms");
            AssertEq(-1, silent.IndexOf(GhostAction.WARN, released), "not stuck, no flicker back while silent");

            List<GhostAction> back = rig.Hold("NEAR", 1500);
            int again = back.IndexOf(GhostAction.WARN);
            AssertTrue(again >= 0 && again * 50 <= 300, "WARN again within one line of the link returning: " + again * 50 + " ms");

            for (int i = 0; i < 80; i++)
                rig.Frame(null);
            List<GhostAction> shake = Shake(rig, "NEAR", 31);
            AssertEq(2, rig.Count(EventAcceptance.RESYNCING), "the first two lines after the gap resynchronise");
            AssertEq(1, rig.Count(EventAcceptance.ACCEPTED), "the third copy is accepted");
            AssertEq(1, rig.Pulses, "one pulse");
            AssertEq(GhostAction.INVESTIGATE, shake[shake.Count - 1], "INVESTIGATE[X3] once accepted");
            AssertEq("X3", rig.Layer.LastRuleId, "X3");
        }

        // Live COM6 run 23 Oct 20:39: NEAR (R4 WARN), then VERY_NEAR 875 ms
        // later. The inner dwell held R4's WARN with candidate R8, so the
        // decision read HYST and X2 waited 625 ms for the dwell to expire.
        // A dwell holding a WARN over an idle (R8) candidate is the idle case
        // the sensor-response rules exist for, so X2 applies at once.
        private static void VeryNearInsideDwell()
        {
            Rig rig = Ready(On());
            rig.Hold("NEAR", 600);
            AssertEq(GhostAction.WARN, rig.Frame(null), "R4 WARN, inside the 1.5 s inner dwell");
            AssertEq(GhostAction.WARN, rig.Frame(rig.Line("VERY_NEAR")), "still WARN on the first VERY_NEAR line");
            AssertEq("X2", rig.Layer.LastRuleId, "X2 on that same line, not HYST until the dwell expires ("
                + rig.Layer.LastDecisionReason + ")");
        }

        // The same dwell must not hold back X3: a shake arriving on the
        // first VERY_NEAR line inside the dwell starts the search at once.
        private static void ShakeInsideDwell()
        {
            Rig rig = Ready(On());
            rig.Hold("NEAR", 600);
            List<GhostAction> shake = Shake(rig, "VERY_NEAR", 41);
            AssertEq(GhostAction.INVESTIGATE, shake[0], "INVESTIGATE in the frame the event arrives (" + rig.Layer.LastDecisionReason + ")");
            AssertEq("X3", rig.Layer.LastRuleId, "X3");
        }

        // The relaxed gate covers only a held WARN: a dwell still holding a
        // safety action (here R2's RETREAT after the surge ends) is never
        // replaced by a sensor-response rule.
        private static void DwellHoldingSafety()
        {
            Rig rig = Ready(On());
            rig.World.UpdateVirtualState(new VirtualState { threatLevel = 0.95f });
            AssertEq(GhostAction.RETREAT, rig.Frame(rig.Line("NONE")), "R2 RETREAT");
            rig.World.UpdateVirtualState(new VirtualState { threatLevel = 0.2f });
            var held = new List<GhostAction>();
            held.AddRange(rig.Hold("VERY_NEAR", 600));
            held.AddRange(Shake(rig, "VERY_NEAR", 42));
            foreach (GhostAction a in held)
                AssertEq(GhostAction.RETREAT, a, "the dwell keeps RETREAT (" + rig.Layer.LastDecisionReason + ")");
        }

        // Live COM6 run 23 Oct 20:57: shakes 111-117 each re-armed a running
        // search (outcome ACTED) but the action, rule and reason stayed the
        // same, so no DECISION record and nothing for presentation to see.
        // The pipeline now reports the event each caused decision answered.
        private static void SecondShakeAttributed()
        {
            Rig rig = Ready(On());
            rig.UsePipeline();
            rig.Hold("NEAR", 1000);
            Shake(rig, "NEAR", 51);
            AssertEq(GhostAction.INVESTIGATE, rig.Frame(null), "X3 search");
            AssertEq(51L, rig.GhostPipeline.LastCausedEventId, "the first shake");
            int decisions = rig.CountRecords("DECISION");
            Swinging(rig, 1500);
            List<GhostAction> second = Shake(rig, "NEAR", 52);
            AssertEq(GhostAction.INVESTIGATE, second[0], "still searching");
            AssertEq(52L, rig.GhostPipeline.LastCausedEventId, "the second shake, in the frame it arrives");
            AssertEq(decisions, rig.CountRecords("DECISION"), "no new DECISION record for the same search");
            AssertEq(1, rig.CountRecords("PHYS_EVENT_OUTCOME", "evt=52 outcome=ACTED"), "evt 52 recorded as acted once");

            Rig fresh = Ready(On());
            fresh.UsePipeline();
            fresh.Hold("NEAR", 1000);
            AssertEq(0L, fresh.GhostPipeline.LastCausedEventId, "0 before any caused decision");
        }

        private static void VeryNearEscalationInterruptsSearch()
        {
            Rig rig = Ready(On());
            rig.Hold("NEAR", 1000);
            List<GhostAction> shake = Shake(rig, "NEAR", 61);
            AssertEq(GhostAction.INVESTIGATE, shake[0], "X3 search begins");
            AssertEq("X3", rig.Layer.LastRuleId, "X3");

            // During the 3 s pulse, search is maintained
            List<GhostAction> pulse = Swinging(rig, 3050);
            AssertTrue(pulse.Contains(GhostAction.INVESTIGATE), "searched during pulse");

            // Once the pulse expires, steady VERY_NEAR immediately resumes X2 WARN
            List<GhostAction> escalates = rig.Hold("VERY_NEAR", 500);
            AssertEq(GhostAction.WARN, escalates[0], "VERY_NEAR takes over as WARN");
            AssertEq("X2", rig.Layer.LastRuleId, "X2 outranks lower-priority investigation once pulse ends");
        }

        private static void ThreatZoneVeryNearInterruptsSearch()
        {
            Rig rig = Ready(Exhibition());
            rig.Hold("NEAR", 1000);
            List<GhostAction> shake = Shake(rig, "NEAR", 62);
            AssertEq(GhostAction.INVESTIGATE, shake[0], "X3 search begins");

            // Player in threat zone + VERY_NEAR immediately supersedes with X1 PROTECT (S rule pass-through)
            rig.Player(3f, true);
            GhostAction action = rig.Frame(rig.Line("VERY_NEAR", "LOW"));
            AssertEq(GhostAction.PROTECT, action, "X1 PROTECT immediately interrupts search in threat zone");
            AssertEq("S", rig.Layer.LastRuleId, "S safety pass-through of X1");
            AssertTrue(rig.Layer.LastDecisionReason.Contains("threat zone"), "X1: " + rig.Layer.LastDecisionReason);
        }

        private static void RepeatedShakesMaintainSearch()
        {
            Rig rig = Ready(On());
            rig.UsePipeline();
            rig.Hold("NEAR", 1000);
            Shake(rig, "NEAR", 71);
            AssertEq(GhostAction.INVESTIGATE, rig.Frame(null), "first shake");
            AssertEq(71L, rig.GhostPipeline.LastCausedEventId, "event 71");

            Swinging(rig, 1000);
            Shake(rig, "NEAR", 72);
            AssertEq(GhostAction.INVESTIGATE, rig.Frame(null), "second shake");
            AssertEq(72L, rig.GhostPipeline.LastCausedEventId, "event 72");

            Swinging(rig, 1000);
            Shake(rig, "NEAR", 73);
            AssertEq(GhostAction.INVESTIGATE, rig.Frame(null), "third shake");
            AssertEq(73L, rig.GhostPipeline.LastCausedEventId, "event 73");
        }

        // --------------------------------------------- E6 (in-game proximity)

        private static void Ghost(Rig rig, GhostPhase phase = GhostPhase.NORMAL, long cooldownMs = 0)
        {
            rig.World.UpdateGhostState(new GhostState { phase = phase, attackCooldownMs = cooldownMs });
        }

        private static void ProximityOutside()
        {
            Rig rig = Ready(null, proximity: true);
            rig.Player(5f, false);
            foreach (GhostAction a in rig.Hold("NONE", 5000))
                AssertEq(GhostAction.FOLLOW, a, "outside the threat zone");
            AssertTrue(rig.Layer.LastRuleId != "E6", "no E6");
        }

        private static void ProximityWarn()
        {
            Rig rig = Ready(null, proximity: true);
            rig.Player(3f, true);
            AssertEq(GhostAction.WARN, rig.Frame(null), "WARN as soon as the player is in the zone");
            AssertEq("E6", rig.Layer.LastRuleId, "rule id");
            AssertTrue(rig.Layer.LastDecisionReason.Contains("in-game proximity"), "reason: " + rig.Layer.LastDecisionReason);
            foreach (GhostAction a in rig.Hold("NONE", 10000))
                AssertEq(GhostAction.WARN, a, "telegraph only while out of reach (3 m)");
            rig.Player(5f, false);
            List<GhostAction> leaving = rig.Hold("NONE", 3000);
            int released = leaving.IndexOf(GhostAction.FOLLOW);
            AssertTrue(released >= 0 && released * 50 <= EncounterDecisionLayer.DefaultOuterDwellMs + 100,
                "released after at most the outer dwell: " + released * 50 + " ms");
            AssertEq(-1, leaving.IndexOf(GhostAction.WARN, released), "no flicker back to WARN after the release");
        }

        private static void ProximityAttackTelegraphed()
        {
            Rig rig = Ready(null, proximity: true);
            rig.Player(1.6f, true);
            List<GhostAction> actions = rig.Hold("NONE", 3000);
            int warn = actions.IndexOf(GhostAction.WARN);
            int attack = actions.IndexOf(GhostAction.ATTACK);
            AssertEq(0, warn, "WARN first");
            AssertTrue(attack > warn, "then ATTACK: " + string.Join(",", actions));
            long telegraph = (attack - warn) * 50;
            AssertTrue(telegraph >= EncounterDecisionLayer.DefaultWarnTelegraphMs && telegraph <= EncounterDecisionLayer.DefaultWarnTelegraphMs + 50,
                "WARN for the 1.5 s telegraph: " + telegraph + " ms");

            Rig again = Ready(null, proximity: true);
            again.Player(1.6f, true);
            List<GhostAction> once = again.Hold("NONE", 1550);
            AssertEq(GhostAction.ATTACK, once[once.Count - 1], "attack at the end of the telegraph");
            AssertEq("E6", again.Layer.LastRuleId, "rule id");
            AssertTrue(again.Layer.LastDecisionReason.StartsWith("ATTACK[E6]") && again.Layer.LastDecisionReason.Contains("in-game proximity"),
                "reason: " + again.Layer.LastDecisionReason);
        }

        private static void ProximityCooldown()
        {
            Rig rig = Ready(null, proximity: true);
            rig.Player(1.6f, true);
            List<GhostAction> warnUp = rig.Hold("NONE", 1550);
            AssertEq(GhostAction.ATTACK, warnUp[warnUp.Count - 1], "attack decided");
            AssertEq(GhostAction.WARN, rig.Frame(null), "no repeat on the next frame (the telegraph restarts)");
            // The controller plays the attack: E1 holds ATTACK through the phases.
            Ghost(rig, GhostPhase.WINDUP);
            foreach (GhostAction a in rig.Hold("NONE", 1200)) AssertEq(GhostAction.ATTACK, a, "E1 windup");
            Ghost(rig, GhostPhase.RECOVER);
            foreach (GhostAction a in rig.Hold("NONE", 2000)) AssertEq(GhostAction.ATTACK, a, "E1 recover");
            Ghost(rig, GhostPhase.NORMAL, 6000);
            List<GhostAction> cooling = rig.Hold("NONE", 6000);
            foreach (GhostAction a in cooling)
                AssertTrue(a != GhostAction.ATTACK, "no attack during the 6 s cooldown");
            AssertEq(GhostAction.WARN, cooling[cooling.Count - 1], "still telegraphing");
            Ghost(rig, GhostPhase.NORMAL, 0);
            AssertEq(GhostAction.ATTACK, rig.Frame(null), "the next attack once the cooldown ends (warned throughout)");

            // Jitter at the reach boundary never re-triggers inside the cooldown.
            Rig jitter = Ready(null, proximity: true);
            Ghost(jitter, GhostPhase.NORMAL, 60000);
            var actions = new List<GhostAction>();
            for (int i = 0; i < 200; i++)
            {
                jitter.Player(i % 2 == 0 ? 1.9f : 2.1f, true);
                actions.Add(jitter.Frame(i % 2 == 0 ? jitter.Line("NONE") : null));
            }
            AssertTrue(!actions.Contains(GhostAction.ATTACK), "no attack while cooling down");
            AssertEq(0, Transitions(actions), "steady WARN through the jitter");
        }

        private static void ProximityBlocked()
        {
            Rig grace = Ready(null, proximity: true);
            grace.World.UpdateVirtualState(new VirtualState { threatLevel = 0.2f, attackGraceActive = true });
            grace.Player(1.2f, true);
            foreach (GhostAction a in grace.Hold("NONE", 3000))
                AssertTrue(a != GhostAction.ATTACK, "no attack during the restart grace");
            AssertEq(GhostAction.WARN, grace.Frame(null), "the telegraph still shows");

            Rig beat = Ready(null, proximity: true);
            beat.World.UpdateVirtualState(new VirtualState { threatLevel = 0.2f, mandatoryWarningBeat = true });
            beat.Player(1.2f, true);
            foreach (GhostAction a in beat.Hold("NONE", 3000))
                AssertEq(GhostAction.WARN, a, "a warning beat is never an attack");
            AssertEq("W", beat.Layer.LastRuleId, "W");

            Rig terminal = Ready(null, proximity: true);
            terminal.World.UpdateVirtualState(new VirtualState { threatLevel = 0.2f, gameplayTerminal = true });
            terminal.Player(1.2f, true);
            foreach (GhostAction a in terminal.Hold("NONE", 3000))
                AssertTrue(a != GhostAction.ATTACK && a != GhostAction.WARN, "terminal: no game-level override");
            AssertEq("G0", terminal.Layer.LastRuleId, "G0");

            Rig stunned = Ready(null, proximity: true);
            stunned.Player(1.2f, true);
            stunned.Hold("NONE", 1000);
            Ghost(stunned, GhostPhase.INTERRUPTED);
            foreach (GhostAction a in stunned.Hold("NONE", 3000))
                AssertEq(GhostAction.RETREAT, a, "lantern-interrupted: E0 RETREAT");
        }

        private static void ProximityOff()
        {
            Rig rig = Ready(null);
            rig.Player(1.2f, true);
            foreach (GhostAction a in rig.Hold("NONE", 5000))
                AssertEq(GhostAction.FOLLOW, a, "E6 off (M2 route, encounter disabled)");
        }

        private static void ProximityMasked()
        {
            Rig rig = Ready(On(), masked: true, proximity: true);
            rig.Player(1.6f, true);
            List<GhostAction> actions = rig.Hold("VERY_NEAR", 2000);
            AssertTrue(actions.Contains(GhostAction.ATTACK), "VIRTUAL_ONLY: in-game proximity alone attacks: " + string.Join(",", actions));
            AssertEq(GhostAction.WARN, actions[0], "telegraphed first");
        }

        private static void ShakeDelaysAttack()
        {
            Rig rig = Ready(On(), proximity: true);
            rig.Player(3f, true);
            rig.Hold("NEAR", 1000);
            AssertEq("E6", rig.Layer.LastRuleId, "E6 WARN over R4 in the zone");
            List<GhostAction> shake = Shake(rig, "NEAR", 13);
            AssertEq(GhostAction.INVESTIGATE, shake[0], "X3 INVESTIGATE is never replaced by E6");
            rig.Player(1.6f, true);
            List<GhostAction> after = rig.Hold("NEAR", 6000);
            int warn = after.IndexOf(GhostAction.WARN);
            int attack = after.IndexOf(GhostAction.ATTACK);
            AssertTrue(warn > 0 && attack > warn, "INVESTIGATE, then WARN, then ATTACK: " + string.Join(",", after));
            AssertTrue((attack - warn) * 50 >= EncounterDecisionLayer.DefaultWarnTelegraphMs, "a full telegraph after the shake");
        }

        private static void ProximityCommitted()
        {
            Rig rig = Ready(null, proximity: true);
            rig.World.UpdateVirtualState(new VirtualState { threatLevel = 0.2f, encounterCommitted = true });
            rig.World.UpdatePlayerState(new PlayerState { distanceToGhost = 1.6f, inThreatZone = true, inWarnBand = true });
            string rule = "none";
            for (int i = 0; i < 60 && rule == "none"; i++)
                if (rig.Frame(i % 2 == 0 ? rig.Line("NONE") : null) == GhostAction.ATTACK)
                    rule = rig.Layer.LastRuleId;
            AssertEq("E3", rule, "a committed attack is E3, never E6");
        }
    }
}
