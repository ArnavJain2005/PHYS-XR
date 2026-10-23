using PHYSXR.Core.Enums;
using PHYSXR.Game.Flow;
using PHYSXR.Game.Gameplay;
using PHYSXR.Ghost;
using UnityEngine;

namespace PHYSXR.Game.Audio
{
    // The adaptive score: three procedural layers whose levels follow one
    // "intensity" value read from what the ghost is visibly doing.
    //
    //   drone    (ScoreDrone)    the tension bed, from the first moment
    //   strings  (ScoreStrings)  unease: the ghost is in sight and closing
    //   pulse    (ScorePulse)    escalation: it is near, warning or attacking
    //
    // Pacing rules keep it from becoming wallpaper:
    //  - rises quickly, falls slowly, so it never flutters;
    //  - after a climax there is a release: for a while the score cannot climb
    //    back past "unease" unless an attack actually starts;
    //  - while nothing is happening it withdraws almost completely now and
    //    then, so silence is part of the score;
    //  - a swell marks the ghost closing in after a long calm (rate-limited);
    //  - near the top, the house ambience ducks and the storm is muffled more,
    //    the brief hush before an attack.
    //
    // Presentation only, like HorrorPostFx: it reads the ghost's distance,
    // visibility, behaviour and phase and never feeds anything back.
    public class TensionScore : MonoBehaviour
    {
        private const float RiseRate = 0.8f, FallRate = 0.12f;
        private const float ReleaseSeconds = 9f, SwellCooldown = 30f;

        private GameFlowController flow;
        private GameAudio audioPlayer;
        private AudioSource drone, strings, pulse;
        private AudioSource houseBed;
        private AudioLowPassFilter stormMuffle;
        private float houseBedVolume;
        private float muffleCutoff;

        private float intensity;
        private float releaseUntil;
        private float withdrawUntil;
        private float nextWithdraw;
        private float calmSeconds;
        private float lastSwell = -100f;
        private bool peaked;

        // 0 silent .. 1 climax (read by AmbientEvents to stay out of the way).
        public float Intensity => intensity;

        public void Initialize(GameFlowController flow, GameAudio audioPlayer, AudioSource houseBed, AudioLowPassFilter stormMuffle)
        {
            this.flow = flow;
            this.audioPlayer = audioPlayer;
            this.houseBed = houseBed;
            this.stormMuffle = stormMuffle;
            houseBedVolume = houseBed != null ? houseBed.volume : 0f;
            muffleCutoff = stormMuffle != null ? stormMuffle.cutoffFrequency : 750f;

            drone = Layer(AudioCue.ScoreDrone);
            strings = Layer(AudioCue.ScoreStrings);
            pulse = Layer(AudioCue.ScorePulse);
            nextWithdraw = Time.time + Random.Range(45f, 75f);
        }

        private AudioSource Layer(AudioCue cue)
        {
            AudioSource s = GameAudio.CreateLoop(cue, transform, Vector3.zero, false, 1f);
            s.volume = 0f;
            return s;
        }

        private void Update()
        {
            if (flow == null || drone == null)
                return;

            float dt = Time.deltaTime;
            float now = Time.time;
            RunState run = flow.Flow.RunState;
            float target = Target(now, out bool attacking);

            if (run == RunState.Dead || run == RunState.Succeeded)
            {
                target = 0f;
                intensity = Mathf.MoveTowards(intensity, 0f, dt * 1.5f);
            }
            else
            {
                if (peaked && target < 0.9f)
                {
                    peaked = false;
                    releaseUntil = now + ReleaseSeconds;
                }
                if (now < releaseUntil && !attacking)
                    target = Mathf.Min(target, 0.55f);

                // Withdraw: only when nothing is going on.
                if (target < 0.3f && now >= nextWithdraw)
                {
                    withdrawUntil = now + Random.Range(14f, 24f);
                    nextWithdraw = withdrawUntil + Random.Range(50f, 90f);
                }
                if (now < withdrawUntil && target < 0.3f)
                    target *= 0.12f;

                float before = intensity;
                intensity = target > intensity
                    ? Mathf.MoveTowards(intensity, target, dt * RiseRate)
                    : Mathf.MoveTowards(intensity, target, dt * FallRate);
                if (intensity >= 0.9f)
                    peaked = true;

                // The swell: closing in after at least 20 s of calm.
                if (intensity < 0.3f)
                    calmSeconds += dt;
                if (before < 0.6f && intensity >= 0.6f)
                {
                    if (calmSeconds > 20f && now - lastSwell > SwellCooldown)
                    {
                        lastSwell = now;
                        audioPlayer?.Play2D(AudioCue.ScoreSwell, 0.8f);
                    }
                    calmSeconds = 0f;
                }
            }

            ApplyLevels();
        }

        // What the score should be doing now, from the ghost's visible state.
        private float Target(float now, out bool attacking)
        {
            attacking = false;
            GhostMotionController ghost = flow.Ghost;
            if (ghost == null || ghost.Dormant || ghost.Halted || ghost.Visual == null)
                return 0.14f;

            float seen = ghost.Visual.Visibility;
            float closeness = Mathf.InverseLerp(14f, 2.5f, ghost.StraightDistanceToPlayer);
            float t = Mathf.Lerp(0.3f, 0.85f, closeness) * seen;
            if (ghost.Behaviour == GhostExecutionState.Warning)
                t = Mathf.Max(t, 0.72f);
            if (ghost.Phase == GhostPhase.WINDUP || ghost.Phase == GhostPhase.STRIKE)
            {
                attacking = true;
                t = 1f;
            }
            else if (ghost.Phase == GhostPhase.INTERRUPTED || ghost.Behaviour == GhostExecutionState.Retreating)
            {
                t = Mathf.Min(t, 0.45f);
            }
            return Mathf.Max(0.14f, t);
        }

        private void ApplyLevels()
        {
            float i = intensity;
            float bed = Mathf.SmoothStep(0f, 1f, i / 0.25f) * (0.4f + 0.6f * Mathf.Clamp01(i / 0.6f));
            drone.volume = GameAudio.Level(AudioCue.ScoreDrone) * bed;
            strings.volume = GameAudio.Level(AudioCue.ScoreStrings) * Mathf.SmoothStep(0f, 1f, (i - 0.35f) / 0.4f);
            pulse.volume = GameAudio.Level(AudioCue.ScorePulse) * Mathf.SmoothStep(0f, 1f, (i - 0.65f) / 0.3f);

            float duck = Mathf.SmoothStep(0f, 1f, (i - 0.8f) / 0.2f);
            if (houseBed != null)
                houseBed.volume = houseBedVolume * (1f - 0.6f * duck);
            if (stormMuffle != null)
                stormMuffle.cutoffFrequency = Mathf.Lerp(muffleCutoff, 380f, duck);
        }
    }
}
