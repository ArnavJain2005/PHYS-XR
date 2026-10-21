using System;
using System.Collections.Generic;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.PhysicalWorld;
using PHYSXR.WorldStateSystem;

namespace PHYSXR.Game.Flow
{
    // What the game needs from the sensing side, and nothing more: freshness,
    // the masking flag, this frame's already-validated and deduplicated
    // events, a restart blackout, and a gated WorldStateManager over the
    // shared PhysicalStateManager. Game code never sees packets, sockets or
    // raw readings (v2.0 section 15). The app's SensorService implements it;
    // NoPhysicalInput stands in when the game scene is played on its own.
    public interface IPhysicalInputSource
    {
        PhysicalSourceStatus Status { get; }

        bool PhysicalInputMasked { get; }

        IReadOnlyList<PhysicalEvent> FrameAcceptedEvents { get; }

        RunCondition Condition { get; }

        WorldStateManager CreateWorldStateManager(Func<long> clock);

        void BeginEventBlackout(long durationMs);
    }

    public sealed class NoPhysicalInput : IPhysicalInputSource
    {
        private static readonly PhysicalEvent[] None = new PhysicalEvent[0];
        private readonly PhysicalStateManager physical = new PhysicalStateManager();

        public PhysicalSourceStatus Status => PhysicalSourceStatus.UNAVAILABLE;

        public bool PhysicalInputMasked => true;

        public IReadOnlyList<PhysicalEvent> FrameAcceptedEvents => None;

        public RunCondition Condition => RunCondition.VIRTUAL_ONLY;

        public WorldStateManager CreateWorldStateManager(Func<long> clock) => new WorldStateManager(physical, clock);

        public void BeginEventBlackout(long durationMs)
        {
        }
    }
}
