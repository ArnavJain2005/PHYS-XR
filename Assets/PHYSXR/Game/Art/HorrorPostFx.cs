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
    // URP post-processing for the house: ACES tonemapping and a split grade
    // (cool, blue-black shadows; warm, tungsten highlights, so practical
    // lights glow against the night), a small lift so darkness hides detail
    // without crushing the silhouettes the player navigates by, a restrained
    // vignette, fine grain, bloom for bulbs and candles. At rest the image is
    // clean: fringing and heavy grain arrive only with the ghost. The look
    // tightens as The Hollow gets close: the vignette closes in, colour
    // drains, grain and fringing rise, and during an attack the grade bleeds
    // toward red and the frame pulls in slightly. Death drains the colour. A heartbeat rises and
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
        private LensDistortion pull;
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
            grade.contrast.value = 15f;
            grade.saturation.value = -18f;
            grade.colorFilter.value = new Color(0.95f, 0.97f, 1.02f);

            WhiteBalance balance = profile.Add<WhiteBalance>(true);
            balance.temperature.value = -6f;
            balance.tint.value = 2f;

            ShadowsMidtonesHighlights split = profile.Add<ShadowsMidtonesHighlights>(true);
            split.shadows.value = new Vector4(0.9f, 0.97f, 1.12f, 0f);
            split.midtones.value = new Vector4(0.98f, 0.99f, 1.02f, 0f);
            split.highlights.value = new Vector4(1.08f, 1.0f, 0.9f, 0f);

            LiftGammaGain lift = profile.Add<LiftGammaGain>(true);
            lift.lift.value = new Vector4(0.98f, 1f, 1.04f, 0.004f);

            vignette = profile.Add<Vignette>(true);
            vignette.intensity.value = 0.3f;
            vignette.smoothness.value = 0.45f;
            vignette.color.value = Color.black;

            // The lightweight (headset) profile leaves out grain, fringing and
            // lens distortion: each is a full-screen pass, and the last two
            // are uncomfortable in a headset.
            bool full = !PresentationProfile.Lightweight;
            if (full)
            {
                grain = profile.Add<FilmGrain>(true);
                grain.type.value = FilmGrainLookup.Thin2;
                grain.intensity.value = 0.22f;
                grain.response.value = 0.8f;

                pull = profile.Add<LensDistortion>(true);
                pull.intensity.value = 0f;
            }

            bloom = profile.Add<Bloom>(true);
            bloom.threshold.value = 0.95f;
            bloom.intensity.value = 0.7f;
            bloom.scatter.value = 0.65f;

            if (full)
            {
                fringe = profile.Add<ChromaticAberration>(true);
                fringe.intensity.value = 0.02f;
            }

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

            vignette.intensity.value = 0.3f + 0.2f * threat + 0.1f * attack + 0.2f * death;
            vignette.color.value = Color.Lerp(Color.black, new Color(0.35f, 0f, 0f), attack * 0.8f + death * 0.5f);
            if (grain != null)
                grain.intensity.value = 0.22f + 0.3f * threat + 0.35f * death;
            if (fringe != null)
                fringe.intensity.value = 0.02f + 0.28f * threat + 0.3f * attack;
            grade.saturation.value = -18f - 24f * threat - 60f * death;
            grade.colorFilter.value = Color.Lerp(new Color(0.95f, 0.97f, 1.02f), new Color(1.1f, 0.75f, 0.72f), attack * 0.7f);
            if (pull != null)
                pull.intensity.value = -0.14f * attack;
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
