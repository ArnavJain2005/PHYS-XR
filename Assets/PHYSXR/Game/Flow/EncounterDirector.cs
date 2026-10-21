using PHYSXR.Core.Data;

namespace PHYSXR.Game.Flow
{
    // Computes the game-side WorldState inputs the decision rules read
    // (Technical Spec section D, "WorldState inputs the rules read"). It
    // decides nothing about the ghost; it only turns game facts into the
    // PlayerState / VirtualState fields, every frame, on the game clock:
    //
    //   threatLevel   0.2 baseline; 0.5 while the ghost is within 6 m (until
    //                 beyond 7.5 m) once the fuse has been picked up; 0.8 for
    //                 3 s from the power surge. With the repo thresholds 0.5
    //                 gives R7 WARN and 0.8 gives R2 RETREAT.
    //   eventActive / mandatoryWarningBeat
    //                 true for 4 s at scripted beats (first sighting, fuse
    //                 pickup): R6 WARN, and the W rule so a NEAR/HIGH physical
    //                 presence cannot turn the beat into FOLLOW (I-6).
    //   encounterCommitted  from GameFlow.
    //   attackGraceActive   3 s after a checkpoint restart.
    //   inThreatZone  enter <= 3.5 m, exit > 4.5 m.
    //   inWarnBand    enter <= 6.0 m, exit > 7.5 m.
    //   movement      player speed > 0.2 m/s.
    //
    // Plain C#: the caller passes the clock and the facts, so it is tested in
    // the standalone harness.
    public sealed class EncounterDirector
    {
        public const float BaselineThreat = 0.2f;
        public const float NearThreat = 0.5f;
        public const float SurgeThreat = 0.8f;
        public const long BeatMs = 4000L;
        public const long SurgeMs = 3000L;
        public const long RestartGraceMs = 3000L;
        public const float ThreatZoneEnter = 3.5f, ThreatZoneExit = 4.5f;
        public const float WarnBandEnter = 6.0f, WarnBandExit = 7.5f;
        public const float NearThreatEnter = 6.0f, NearThreatExit = 7.5f;
        public const float MovingSpeed = 0.2f;

        private long beatUntil = long.MinValue;
        private long surgeUntil = long.MinValue;
        private long graceUntil = long.MinValue;
        private bool inThreatZone;
        private bool inWarnBand;
        private bool nearThreat;

        public string LastBeat { get; private set; } = string.Empty;

        public bool BeatActive(long now) => now < beatUntil;
        public bool SurgeActive(long now) => now < surgeUntil;
        public bool GraceActive(long now) => now < graceUntil;

        public void BeginBeat(long now, string id)
        {
            beatUntil = now + BeatMs;
            LastBeat = id ?? string.Empty;
        }

        public void BeginSurge(long now)
        {
            surgeUntil = now + SurgeMs;
        }

        public void BeginRestartGrace(long now)
        {
            graceUntil = now + RestartGraceMs;
        }

        // Fills player and virtualWorld in place (the caller reuses them).
        public void Update(long now, float distanceToGhost, float playerSpeed, bool fuseCollected,
            bool encounterCommitted, PlayerState player, VirtualState virtualWorld)
        {
            inThreatZone = Band(inThreatZone, distanceToGhost, ThreatZoneEnter, ThreatZoneExit);
            inWarnBand = Band(inWarnBand, distanceToGhost, WarnBandEnter, WarnBandExit);
            nearThreat = Band(nearThreat, distanceToGhost, NearThreatEnter, NearThreatExit);

            player.distanceToGhost = distanceToGhost;
            player.movement = playerSpeed > MovingSpeed;
            player.inThreatZone = inThreatZone;
            player.inWarnBand = inWarnBand;

            float threat = BaselineThreat;
            if (fuseCollected && nearThreat)
                threat = NearThreat;
            if (SurgeActive(now))
                threat = SurgeThreat;

            bool beat = BeatActive(now);
            virtualWorld.threatLevel = threat;
            virtualWorld.eventActive = beat;
            virtualWorld.mandatoryWarningBeat = beat;
            virtualWorld.encounterCommitted = encounterCommitted;
            virtualWorld.attackGraceActive = GraceActive(now);
            virtualWorld.gameplayTerminal = false;
        }

        private static bool Band(bool inside, float distance, float enter, float exit)
        {
            return inside ? distance <= exit : distance <= enter;
        }
    }
}
