using System.Collections.Generic;
using UnityEngine;

namespace PHYSXR.Game.Audio
{
    // The game scene's sound player. Gameplay and presentation code ask for a
    // cue; this picks the clip (an optional local override, otherwise the
    // procedural one), a pooled AudioSource and the mix level.
    //
    //  - 2D one-shots (UI, stings): one dedicated source, PlayOneShot.
    //  - 3D one-shots: a fixed pool of sources, reused round-robin, so no
    //    GameObject is created per sound.
    //  - Loops: CreateLoop returns a source the caller owns (ambience beds,
    //    hums, the ghost's breathing), parented wherever it should sound from.
    //
    // Nothing here affects game rules. Master volume is AudioListener.volume.
    public class GameAudio : MonoBehaviour
    {
        private const int PoolSize = 12;

        private static AudioOverrideLibrary overrides;
        private static bool overridesLoaded;

        private readonly List<AudioSource> pool = new List<AudioSource>(PoolSize);
        private AudioSource ui;
        private int next;

        private static readonly Dictionary<AudioCue, float> Mix = new Dictionary<AudioCue, float>
        {
            { AudioCue.AmbienceHouse, 0.55f }, { AudioCue.AmbienceStorm, 0.45f }, { AudioCue.Thunder, 0.8f },
            { AudioCue.MenuMusic, 0.5f }, { AudioCue.FootstepWood, 0.32f }, { AudioCue.FootstepTile, 0.3f },
            { AudioCue.ClockTick, 0.35f }, { AudioCue.HouseCreak, 0.4f }, { AudioCue.UiHover, 0.15f },
            { AudioCue.UiSelect, 0.3f }, { AudioCue.ObjectiveUpdate, 0.35f }, { AudioCue.FusePickup, 0.6f },
            { AudioCue.FuseInsert, 0.8f }, { AudioCue.Invalid, 0.5f }, { AudioCue.DoorLocked, 0.6f },
            { AudioCue.DoorOpen, 0.8f }, { AudioCue.PowerSurge, 0.9f }, { AudioCue.PowerHum, 0.18f },
            { AudioCue.LightBuzz, 0.2f }, { AudioCue.Flicker, 0.5f }, { AudioCue.GhostBreath, 0.6f },
            { AudioCue.GhostWhisper, 0.5f }, { AudioCue.GhostScreech, 0.55f }, { AudioCue.GhostThud, 0.75f },
            { AudioCue.GhostHiss, 0.6f }, { AudioCue.GhostManifest, 0.6f }, { AudioCue.AttackRise, 0.7f },
            { AudioCue.StrikeImpact, 0.95f }, { AudioCue.StrikeWhiff, 0.6f }, { AudioCue.ProtectHum, 0.4f },
            { AudioCue.LanternPulse, 0.7f }, { AudioCue.LanternMiss, 0.45f }, { AudioCue.LanternReady, 0.25f },
            { AudioCue.Heartbeat, 0.6f }, { AudioCue.DeathSting, 0.85f }, { AudioCue.WinSting, 0.6f },
            { AudioCue.RadioStatic, 0.35f }, { AudioCue.NoteRustle, 0.5f }, { AudioCue.CandleCrackle, 0.3f },
            { AudioCue.GhostPresence, 0.42f }, { AudioCue.GhostDrag, 0.35f }, { AudioCue.ScoreDrone, 0.34f },
            { AudioCue.ScoreStrings, 0.27f }, { AudioCue.ScorePulse, 0.36f }, { AudioCue.ScoreSwell, 0.42f },
            { AudioCue.DistantKnock, 0.45f }, { AudioCue.PipeGroan, 0.32f }, { AudioCue.WaterDrip, 0.3f }
        };

        public static float Level(AudioCue cue) => Mix.TryGetValue(cue, out float v) ? v : 0.6f;

        public static AudioClip Clip(AudioCue cue, int variant, out float volume)
        {
            if (!overridesLoaded)
            {
                overrides = Resources.Load<AudioOverrideLibrary>(AudioOverrideLibrary.ResourceName);
                overridesLoaded = true;
            }

            if (overrides != null && overrides.TryGet(cue, out AudioClip clip, out float overrideVolume))
            {
                volume = Level(cue) * overrideVolume;
                return clip;
            }

            volume = Level(cue);
            return ProceduralAudio.Get(cue, variant);
        }

        public static bool UsingOverrides
        {
            get
            {
                if (!overridesLoaded)
                    Clip(AudioCue.UiHover, 0, out _);
                return overrides != null;
            }
        }

        private void Awake()
        {
            ui = gameObject.AddComponent<AudioSource>();
            ui.playOnAwake = false;
            ui.spatialBlend = 0f;
            ui.ignoreListenerPause = true;

            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("Sfx" + i);
                go.transform.SetParent(transform, false);
                AudioSource s = go.AddComponent<AudioSource>();
                Configure3D(s, 1.5f, 22f);
                pool.Add(s);
            }
        }

        public static void Configure3D(AudioSource s, float minDistance, float maxDistance)
        {
            s.playOnAwake = false;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = minDistance;
            s.maxDistance = maxDistance;
            s.dopplerLevel = 0f;
            s.spread = 40f;
        }

        public void Play2D(AudioCue cue, float volumeScale = 1f, int variant = 0)
        {
            AudioClip clip = Clip(cue, variant, out float volume);
            ui.PlayOneShot(clip, volume * volumeScale);
        }

        public void PlayAt(AudioCue cue, Vector3 position, float volumeScale = 1f, float pitch = 1f, int variant = 0,
            float minDistance = 1.5f, float maxDistance = 22f)
        {
            AudioClip clip = Clip(cue, variant, out float volume);
            AudioSource s = pool[next];
            next = (next + 1) % pool.Count;
            s.transform.position = position;
            s.minDistance = minDistance;
            s.maxDistance = maxDistance;
            s.pitch = pitch;
            s.clip = clip;
            s.volume = volume * volumeScale;
            s.Play();
        }

        public void PlayRandomAt(AudioCue cue, Vector3 position, float volumeScale, float pitchJitter)
        {
            int variant = Random.Range(0, ProceduralAudio.VariantCount(cue));
            PlayAt(cue, position, volumeScale, 1f + Random.Range(-pitchJitter, pitchJitter), variant);
        }

        // A looping source the caller owns (it is destroyed with its parent).
        public static AudioSource CreateLoop(AudioCue cue, Transform parent, Vector3 localPosition, bool spatial,
            float volumeScale = 1f, float minDistance = 1.5f, float maxDistance = 18f)
        {
            var go = new GameObject("Loop_" + cue);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            AudioSource s = go.AddComponent<AudioSource>();
            if (spatial)
                Configure3D(s, minDistance, maxDistance);
            else
                s.spatialBlend = 0f;
            s.loop = true;
            s.playOnAwake = false;
            s.clip = Clip(cue, 0, out float volume);
            s.volume = volume * volumeScale;
            // Randomised start so identical loops in different rooms never phase.
            if (s.clip != null && s.clip.samples > 0)
                s.timeSamples = Random.Range(0, s.clip.samples);
            s.Play();
            return s;
        }
    }
}
