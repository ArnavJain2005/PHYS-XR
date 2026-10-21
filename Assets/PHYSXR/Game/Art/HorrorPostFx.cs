using PHYSXR.Core.Enums;
using PHYSXR.Game.Audio;
using PHYSXR.Game.Flow;
using PHYSXR.Game.Gameplay;
using PHYSXR.Ghost;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PHYSXR.Game.Art
{
    // URP post-processing for the house: ACES tonemapping, a cold desaturated
    // grade, vignette, film grain, bloom for bulbs and candles, and a little
    // chromatic aberration. The look tightens as The Hollow gets close: the
    // vignette closes in, grain and fringing rise, and during an attack the
    // grade bleeds toward red. Death drains the colour. A heartbeat rises and
    // quickens with the same closeness. Presentation only: it
    // reads the ghost's distance and phase, never the decision layer.
    //
    // The profile is created at runtime and destroyed with the scene, so a
    // restart never accumulates profiles. Brightness (exposure) comes from the
    // player's settings.
    public class HorrorPostFx : MonoBehaviour
    {
        private GameFlowController flow;
        private VolumeProfile profile;
        private Volume volume;
        private Vignette vignette;
        private ColorAdjustments grade;
        private FilmGrain grain;
        private ChromaticAberration fringe;
        private Bloom bloom;
        private float brightness;
        private float threat;
        private float attack;
        private float death;
        private AudioSource heartbeat;

        public void Initialize(GameFlowController flow)
        {
            this.flow = flow;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "PHYSXR_HorrorProfile";
            profile.hideFlags = HideFlags.DontSave;

            Tonemapping tone = profile.Add<Tonemapping>(true);
            tone.mode.value = TonemappingMode.ACES;

            grade = profile.Add<ColorAdjustments>(true);
            grade.contrast.value = 16f;
            grade.saturation.value = -24f;
            grade.colorFilter.value = new Color(0.93f, 0.97f, 1.04f);

            WhiteBalance balance = profile.Add<WhiteBalance>(true);
            balance.temperature.value = -9f;
            balance.tint.value = 3f;

            vignette = profile.Add<Vignette>(true);
            vignette.intensity.value = 0.36f;
            vignette.smoothness.value = 0.45f;
            vignette.color.value = Color.black;

            grain = profile.Add<FilmGrain>(true);
            grain.type.value = FilmGrainLookup.Medium2;
            grain.intensity.value = 0.3f;
            grain.response.value = 0.8f;

            bloom = profile.Add<Bloom>(true);
            bloom.threshold.value = 0.95f;
            bloom.intensity.value = 0.7f;
            bloom.scatter.value = 0.65f;

            fringe = profile.Add<ChromaticAberration>(true);
            fringe.intensity.value = 0.08f;

            volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;
            SetBrightness(0f);

            heartbeat = gameObject.AddComponent<AudioSource>();
            heartbeat.clip = GameAudio.Clip(AudioCue.Heartbeat, 0, out _);
            heartbeat.loop = true;
            heartbeat.spatialBlend = 0f;
            heartbeat.volume = 0f;
            heartbeat.Play();
        }

        // Exposure offset in stops (settings: -1..+1).
        public void SetBrightness(float stops)
        {
            brightness = Mathf.Clamp(stops, -1f, 1f);
        }

        private void Update()
        {
            if (profile == null || flow == null)
                return;

            float dt = Time.unscaledDeltaTime;
            GhostMotionController ghost = flow.Ghost;
            float targetThreat = 0f;
            float targetAttack = 0f;
            if (ghost != null && !ghost.Dormant && ghost.Visual != null && ghost.Visual.Visibility > 0.5f && !ghost.Halted)
            {
                targetThreat = Mathf.InverseLerp(9f, 1.5f, ghost.StraightDistanceToPlayer);
                if (ghost.Phase == GhostPhase.WINDUP || ghost.Phase == GhostPhase.STRIKE)
                    targetAttack = 1f;
            }

            bool dead = flow.Flow.RunState == RunState.Dead;
            threat = Mathf.MoveTowards(threat, targetThreat, dt * 1.5f);
            attack = Mathf.MoveTowards(attack, targetAttack, dt * 3f);
            death = Mathf.MoveTowards(death, dead ? 1f : 0f, dt * 0.8f);

            vignette.intensity.value = 0.36f + 0.2f * threat + 0.1f * attack + 0.2f * death;
            vignette.color.value = Color.Lerp(Color.black, new Color(0.35f, 0f, 0f), attack * 0.8f + death * 0.5f);
            grain.intensity.value = 0.3f + 0.3f * threat + 0.3f * death;
            fringe.intensity.value = 0.08f + 0.3f * threat + 0.3f * attack;
            grade.saturation.value = -24f - 20f * threat - 60f * death;
            grade.colorFilter.value = Color.Lerp(new Color(0.93f, 0.97f, 1.04f), new Color(1.1f, 0.75f, 0.72f), attack * 0.7f);
            grade.postExposure.value = brightness - 0.4f * death;

            float beat = Mathf.Clamp01((threat - 0.35f) / 0.65f);
            heartbeat.volume = GameAudio.Level(AudioCue.Heartbeat) * beat * (1f - death);
            heartbeat.pitch = 0.9f + 0.6f * beat + 0.3f * attack;
        }

        private void OnDestroy()
        {
            if (profile != null)
                Destroy(profile);
        }
    }
}
