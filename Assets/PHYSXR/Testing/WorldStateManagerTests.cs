using System;
using System.Reflection;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.PhysicalWorld;
using PHYSXR.WorldStateSystem;

namespace PHYSXR.Testing
{
    // Dependency-light tests for WorldStateManager (WORLD-01..09).
    // Same style as SimulatedPhysicalStateSourceTests/CommunicationTests:
    // plain static checks, no NUnit/UnityEngine.TestRunner asmdef required.
    public static class WorldStateManagerTests
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

            Run("WORLD-01_PhysicalState_IsIncorporated", Test_WORLD01);
            Run("WORLD-02_PlayerState_IsIncorporated", Test_WORLD02);
            Run("WORLD-03_VirtualState_IsIncorporated", Test_WORLD03);
            Run("WORLD-04_GhostState_IsIncorporated", Test_WORLD04);
            Run("WORLD-05_SnapshotContainsAllFourDomains", Test_WORLD05);
            Run("WORLD-06_UpdatingPhysicalState_ChangesSubsequentSnapshot", Test_WORLD06);
            Run("WORLD-07_NoDecisionLogicOrGhostActionSelection", Test_WORLD07);
            Run("WORLD-08_ConstructionIsDeterministicForIdenticalInputs", Test_WORLD08);
            Run("WORLD-09_ReturnedSnapshotCannotCorruptManagerState", Test_WORLD09);
            Run("WSM-PAUSE-01_SuspendedEvent_StartsNoPulse", Test_PAUSE01);
            Run("WSM-PAUSE-02_EventDuringLongPause_NoPulseAfterResume", Test_PAUSE02);
            Run("WSM-PAUSE-03_PulseFromPlay_KeepsRemainingGameTimeAcrossPause", Test_PAUSE03);
            Run("WSM-PAUSE-04_NotSuspended_PulseUnchanged_LegacyIgnores", Test_PAUSE04);

            Console.WriteLine(string.Format("Total: {0}, Passed: {1}, Failed: {2}", passed + failed, passed, failed));
            return failed;
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        private static void Test_WORLD01()
        {
            var physicalStateManager = new PhysicalStateManager();
            physicalStateManager.UpdateState(new PhysicalState
            {
                presence = PresenceLevel.VERY_NEAR,
                confidence = ConfidenceLevel.HIGH,
                disturbance = true,
                timestamp = 4242L
            });

            var worldStateManager = new WorldStateManager(physicalStateManager);
            PHYSXR.Core.Data.WorldState snapshot = worldStateManager.GetCurrentWorldState();

            AssertTrue(snapshot.physical != null, "expected physical state to be present");
            AssertTrue(snapshot.physical.presence == PresenceLevel.VERY_NEAR, "presence mismatch");
            AssertTrue(snapshot.physical.confidence == ConfidenceLevel.HIGH, "confidence mismatch");
            AssertTrue(snapshot.physical.disturbance == true, "disturbance mismatch");
            AssertTrue(snapshot.physical.timestamp == 4242L, "timestamp mismatch");
        }

        private static void Test_WORLD02()
        {
            var worldStateManager = new WorldStateManager(new PhysicalStateManager());
            worldStateManager.UpdatePlayerState(new PlayerState
            {
                distanceToGhost = 3.5f,
                movement = true,
                inThreatZone = true
            });

            PHYSXR.Core.Data.WorldState snapshot = worldStateManager.GetCurrentWorldState();

            AssertTrue(snapshot.player != null, "expected player state to be present");
            AssertTrue(snapshot.player.distanceToGhost == 3.5f, "distanceToGhost mismatch");
            AssertTrue(snapshot.player.movement == true, "movement mismatch");
            AssertTrue(snapshot.player.inThreatZone == true, "inThreatZone mismatch");
        }

        private static void Test_WORLD03()
        {
            var worldStateManager = new WorldStateManager(new PhysicalStateManager());
            worldStateManager.UpdateVirtualState(new VirtualState
            {
                threatLevel = 0.75f,
                eventActive = true
            });

            PHYSXR.Core.Data.WorldState snapshot = worldStateManager.GetCurrentWorldState();

            AssertTrue(snapshot.virtualWorld != null, "expected virtual state to be present");
            AssertTrue(snapshot.virtualWorld.threatLevel == 0.75f, "threatLevel mismatch");
            AssertTrue(snapshot.virtualWorld.eventActive == true, "eventActive mismatch");
        }

        private static void Test_WORLD04()
        {
            var worldStateManager = new WorldStateManager(new PhysicalStateManager());
            worldStateManager.UpdateGhostState(new GhostState
            {
                currentState = GhostAction.INVESTIGATE
            });

            PHYSXR.Core.Data.WorldState snapshot = worldStateManager.GetCurrentWorldState();

            AssertTrue(snapshot.ghost != null, "expected ghost state to be present");
            AssertTrue(snapshot.ghost.currentState == GhostAction.INVESTIGATE, "ghost currentState mismatch");
        }

        private static void Test_WORLD05()
        {
            var physicalStateManager = new PhysicalStateManager();
            physicalStateManager.UpdateState(new PhysicalState { presence = PresenceLevel.NEAR, confidence = ConfidenceLevel.HIGH, disturbance = false, timestamp = 1 });

            var worldStateManager = new WorldStateManager(physicalStateManager);
            worldStateManager.UpdatePlayerState(new PlayerState { distanceToGhost = 1f, movement = false, inThreatZone = false });
            worldStateManager.UpdateVirtualState(new VirtualState { threatLevel = 0.1f, eventActive = false });
            worldStateManager.UpdateGhostState(new GhostState { currentState = GhostAction.FOLLOW });

            PHYSXR.Core.Data.WorldState snapshot = worldStateManager.GetCurrentWorldState();

            AssertTrue(snapshot.physical != null, "physical domain missing");
            AssertTrue(snapshot.player != null, "player domain missing");
            AssertTrue(snapshot.virtualWorld != null, "virtual domain missing");
            AssertTrue(snapshot.ghost != null, "ghost domain missing");
        }

        private static void Test_WORLD06()
        {
            var physicalStateManager = new PhysicalStateManager();
            physicalStateManager.UpdateState(new PhysicalState { presence = PresenceLevel.NONE, confidence = ConfidenceLevel.LOW, disturbance = false, timestamp = 1 });

            var worldStateManager = new WorldStateManager(physicalStateManager);
            PHYSXR.Core.Data.WorldState before = worldStateManager.GetCurrentWorldState();
            AssertTrue(before.physical.presence == PresenceLevel.NONE, "expected initial presence NONE");

            physicalStateManager.UpdateState(new PhysicalState { presence = PresenceLevel.VERY_NEAR, confidence = ConfidenceLevel.HIGH, disturbance = true, timestamp = 2 });
            PHYSXR.Core.Data.WorldState after = worldStateManager.GetCurrentWorldState();

            AssertTrue(after.physical.presence == PresenceLevel.VERY_NEAR, "expected updated presence VERY_NEAR");
            AssertTrue(before.physical.presence == PresenceLevel.NONE, "earlier snapshot must not change retroactively");
        }

        private static void Test_WORLD07()
        {
            Type type = typeof(WorldStateManager);

            const BindingFlags allMembers = BindingFlags.Public | BindingFlags.NonPublic
                                             | BindingFlags.Instance | BindingFlags.Static
                                             | BindingFlags.DeclaredOnly;

            foreach (FieldInfo field in type.GetFields(allMembers))
                AssertTrue(field.FieldType.Name != "GhostAction", type.Name + "." + field.Name + " must not reference GhostAction directly");

            foreach (MethodInfo method in type.GetMethods(allMembers))
            {
                AssertTrue(method.ReturnType.Name != "GhostAction", type.Name + "." + method.Name + " must not return GhostAction");
                foreach (ParameterInfo parameter in method.GetParameters())
                    AssertTrue(parameter.ParameterType.Name != "GhostAction", type.Name + "." + method.Name + " must not accept GhostAction");

                AssertTrue(!method.Name.ToLowerInvariant().Contains("decide"), type.Name + "." + method.Name + " looks like decision logic");
            }
        }

        private static void Test_WORLD08()
        {
            var physicalStateManager = new PhysicalStateManager();
            physicalStateManager.UpdateState(new PhysicalState { presence = PresenceLevel.NEAR, confidence = ConfidenceLevel.LOW, disturbance = true, timestamp = 99 });

            var worldStateManager = new WorldStateManager(physicalStateManager);
            worldStateManager.UpdatePlayerState(new PlayerState { distanceToGhost = 2f, movement = true, inThreatZone = false });
            worldStateManager.UpdateVirtualState(new VirtualState { threatLevel = 0.4f, eventActive = true });
            worldStateManager.UpdateGhostState(new GhostState { currentState = GhostAction.WARN });

            PHYSXR.Core.Data.WorldState first = worldStateManager.GetCurrentWorldState();
            PHYSXR.Core.Data.WorldState second = worldStateManager.GetCurrentWorldState();

            AssertTrue(first.physical.presence == second.physical.presence, "physical.presence should be identical across calls");
            AssertTrue(first.physical.timestamp == second.physical.timestamp, "physical.timestamp should be identical across calls");
            AssertTrue(first.player.distanceToGhost == second.player.distanceToGhost, "player.distanceToGhost should be identical across calls");
            AssertTrue(first.virtualWorld.threatLevel == second.virtualWorld.threatLevel, "virtualWorld.threatLevel should be identical across calls");
            AssertTrue(first.ghost.currentState == second.ghost.currentState, "ghost.currentState should be identical across calls");
        }

        private static void Test_WORLD09()
        {
            var physicalStateManager = new PhysicalStateManager();
            physicalStateManager.UpdateState(new PhysicalState { presence = PresenceLevel.NEAR, confidence = ConfidenceLevel.HIGH, disturbance = false, timestamp = 10 });

            var worldStateManager = new WorldStateManager(physicalStateManager);
            worldStateManager.UpdatePlayerState(new PlayerState { distanceToGhost = 5f, movement = false, inThreatZone = false });

            PHYSXR.Core.Data.WorldState snapshot = worldStateManager.GetCurrentWorldState();

            // Mutate everything reachable on the returned snapshot.
            snapshot.physical.presence = PresenceLevel.VERY_NEAR;
            snapshot.physical.timestamp = 999999L;
            snapshot.player.distanceToGhost = -1f;
            snapshot.player.movement = true;

            PHYSXR.Core.Data.WorldState freshSnapshot = worldStateManager.GetCurrentWorldState();

            AssertTrue(freshSnapshot.physical.presence == PresenceLevel.NEAR, "mutating a snapshot must not affect the manager's physical state");
            AssertTrue(freshSnapshot.physical.timestamp == 10, "mutating a snapshot must not affect the manager's physical timestamp");
            AssertTrue(freshSnapshot.player.distanceToGhost == 5f, "mutating a snapshot must not affect the manager's player state");
            AssertTrue(freshSnapshot.player.movement == false, "mutating a snapshot must not affect the manager's player state");
        }

        // ------------------------------------------------------------ WSM-PAUSE (fix F1)

        // Gated manager on a game clock the test controls, with a FRESH
        // NEAR/HIGH source: the R3 INVESTIGATE inputs, apart from the pulse.
        private static WorldStateManager PauseWorld(Func<long> gameClock)
        {
            var physical = new PhysicalStateManager();
            physical.UpdateState(new PhysicalState { presence = PresenceLevel.NEAR, confidence = ConfidenceLevel.HIGH, timestamp = 1 });
            var world = new WorldStateManager(physical, gameClock);
            world.UpdateSourceStatus(PhysicalSourceStatus.FRESH);
            return world;
        }

        private static PhysicalEvent Event(long id) => new PhysicalEvent { eventType = "DISTURBANCE", eventId = id, boot = 1 };

        private static void Test_PAUSE01()
        {
            long game = 10000;
            WorldStateManager world = PauseWorld(() => game);
            world.EventsSuspended = true;

            AssertTrue(!world.NotifyAcceptedEvent(Event(7)), "a suspended event must not start a pulse");
            PHYSXR.Core.Data.WorldState snapshot = world.GetCurrentWorldState();
            AssertTrue(!snapshot.physical.disturbance, "no disturbance from a suspended event");
            AssertTrue(snapshot.physical.causeEventId == 0L, "no cause event from a suspended event");
            AssertTrue(!world.DisturbancePulseActive, "pulse inactive");
        }

        private static void Test_PAUSE02()
        {
            long game = 10000;
            WorldStateManager world = PauseWorld(() => game);

            // Paused: the game clock stands still however long the pause is.
            world.EventsSuspended = true;
            world.NotifyAcceptedEvent(Event(7));

            world.EventsSuspended = false;
            for (int i = 0; i < 60; i++)
            {
                game += 50;
                AssertTrue(!world.GetCurrentWorldState().physical.disturbance, "late pulse at +" + (i + 1) * 50 + " ms after resume");
            }

            // An event after resume behaves exactly as before.
            AssertTrue(world.NotifyAcceptedEvent(Event(8)), "event after resume starts a pulse");
            PHYSXR.Core.Data.WorldState snapshot = world.GetCurrentWorldState();
            AssertTrue(snapshot.physical.disturbance && snapshot.physical.causeEventId == 8L, "pulse carries the new event");
            game += WorldStateManager.DefaultDisturbancePulseMs;
            AssertTrue(!world.GetCurrentWorldState().physical.disturbance, "pulse ends after 3 s of game time");
        }

        private static void Test_PAUSE03()
        {
            long game = 10000;
            WorldStateManager world = PauseWorld(() => game);
            AssertTrue(world.NotifyAcceptedEvent(Event(7)), "event in play starts a pulse");
            game += 1000;

            // Pause 1 s into the pulse; another event arrives meanwhile.
            world.EventsSuspended = true;
            world.NotifyAcceptedEvent(Event(8));
            world.EventsSuspended = false;

            PHYSXR.Core.Data.WorldState snapshot = world.GetCurrentWorldState();
            AssertTrue(snapshot.physical.disturbance && snapshot.physical.causeEventId == 7L,
                "the running pulse keeps its event; the paused one is not chained on");
            game += 1999;
            AssertTrue(world.GetCurrentWorldState().physical.disturbance, "pulse still active at 2.999 s of game time");
            game += 1;
            AssertTrue(!world.GetCurrentWorldState().physical.disturbance, "pulse ends at 3 s of game time, not extended by the pause");
        }

        private static void Test_PAUSE04()
        {
            long game = 10000;
            WorldStateManager world = PauseWorld(() => game);
            AssertTrue(!world.EventsSuspended, "not suspended by default");
            AssertTrue(world.NotifyAcceptedEvent(Event(7)), "default: event starts a pulse");
            AssertTrue(world.GetCurrentWorldState().physical.disturbance, "default: pulse visible");

            var legacy = new WorldStateManager(new PhysicalStateManager());
            AssertTrue(!legacy.NotifyAcceptedEvent(Event(7)), "legacy mode still ignores events");
        }
    }
}
