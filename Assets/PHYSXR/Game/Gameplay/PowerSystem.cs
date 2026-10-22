using System;
using System.Collections;
using System.Collections.Generic;
using PHYSXR.Game.Art;
using PHYSXR.Game.Audio;
using UnityEngine;

namespace PHYSXR.Game.Gameplay
{
    // Owns the house's two lighting states and the power-restoration beat.
    //
    //   Unpowered: near-black ambient, dim red emergency lights, candles in the
    //              dining room, the player's lantern; ceiling lights off,
    //              bulbs dark.
    //   Powered:   warm pendants and sconces on (some failing), emergency
    //              lights off, bulbs lit, a mains hum at the fuse box and a
    //              buzzing tube in the utility room, ambient raised a little.
    //
    // RestorePower() plays the surge (crackle, flicker, the hum starting) and
    // then raises PowerRestored once; GameFlowController turns that into the
    // objective's power confirmation. ApplyPowered(instant) is the checkpoint
    // path: power already on, no beat. The surge uses scaled time, so it waits
    // while paused. Idempotent: only the first RestorePower() does anything.
    public class PowerSystem : MonoBehaviour
    {
        [SerializeField] private float surgeSeconds = 1.6f;
        // Ambient is a three-colour gradient: a cool fill from above (the
        // night through the windows), a darker band at eye level and almost
        // nothing from the floor, which keeps shapes readable in the dark
        // without the flat, grey look of a single ambient colour.
        [SerializeField] private Color unpoweredAmbient = new Color(0.018f, 0.021f, 0.03f);
        [SerializeField] private Color poweredAmbient = new Color(0.06f, 0.055f, 0.05f);

        private readonly List<Light> mainLights = new List<Light>();
        private readonly List<Light> emergencyLights = new List<Light>();
        private readonly List<Renderer> bulbs = new List<Renderer>();
        private readonly List<Renderer> emergencyBulbs = new List<Renderer>();
        private readonly List<FlickerLight> flickers = new List<FlickerLight>();
        private readonly List<AudioSource> poweredLoops = new List<AudioSource>();
        private Coroutine restoring;
        private GameAudio audioPlayer;
        private Vector3 fuseBoxPosition;

        public bool IsPowered { get; private set; }

        public bool IsRestoring => restoring != null;

        public IReadOnlyList<Light> MainLights => mainLights;

        public IReadOnlyList<Light> EmergencyLights => emergencyLights;

        public event Action PowerRestored;

        // Fired when the surge starts (sound, lights) - the encounter director
        // raises the threat level for the surge window from here.
        public event Action SurgeStarted;

        public void Register(IEnumerable<Light> main, IEnumerable<Light> emergency, IEnumerable<Renderer> bulbRenderers)
        {
            mainLights.AddRange(main);
            emergencyLights.AddRange(emergency);
            bulbs.AddRange(bulbRenderers);
            foreach (Light l in mainLights)
            {
                FlickerLight f = l.GetComponent<FlickerLight>();
                if (f != null)
                    flickers.Add(f);
            }
        }

        public void RegisterEmergencyBulbs(IEnumerable<Renderer> renderers)
        {
            emergencyBulbs.AddRange(renderers);
        }

        // Loops that only play with power (mains hum, buzzing tube).
        public void RegisterPoweredLoop(AudioSource source)
        {
            if (source == null)
                return;
            source.Stop();
            poweredLoops.Add(source);
        }

        public void SetAudio(GameAudio audioPlayer, Vector3 fuseBoxPosition)
        {
            this.audioPlayer = audioPlayer;
            this.fuseBoxPosition = fuseBoxPosition;
        }

        public void ApplyUnpowered()
        {
            IsPowered = false;
            ApplyLighting(false);
        }

        // Checkpoint restore: power already on, no surge, no event.
        public void ApplyPowered()
        {
            IsPowered = true;
            ApplyLighting(true);
        }

        // Idempotent: only the first call while unpowered starts a surge.
        public bool RestorePower()
        {
            if (IsPowered || restoring != null)
                return false;

            restoring = StartCoroutine(Surge());
            return true;
        }

        public int CountEnabled(IReadOnlyList<Light> lights)
        {
            int count = 0;
            for (int i = 0; i < lights.Count; i++)
                if (lights[i] != null && lights[i].enabled)
                    count++;
            return count;
        }

        // PROTECT: "steady lights" while the ghost guards.
        public void SetSteady(bool steady)
        {
            foreach (FlickerLight f in flickers)
                if (f != null)
                    f.SetSteady(steady);
        }

        // WARN entry: the light nearest the ghost fails briefly.
        public void PulseNearest(Vector3 position)
        {
            FlickerLight best = null;
            float bestD = float.MaxValue;
            foreach (FlickerLight f in flickers)
            {
                if (f == null) continue;
                float d = (f.transform.position - position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = f; }
            }

            if (best != null && bestD < 100f && IsPowered)
            {
                best.Pulse();
                audioPlayer?.PlayAt(AudioCue.Flicker, best.transform.position, 0.8f);
            }
        }

        private IEnumerator Surge()
        {
            SurgeStarted?.Invoke();
            audioPlayer?.PlayAt(AudioCue.PowerSurge, fuseBoxPosition, 1f, 1f, 0, 3f, 40f);

            float[] pattern = { 0.12f, 0.08f, 0.2f, 0.06f, 0.1f, 0.25f, 0.07f, 0.15f };
            float spent = 0f;
            bool on = false;
            int step = 0;
            while (spent < surgeSeconds)
            {
                on = !on;
                ApplyLighting(on);
                float wait = pattern[step++ % pattern.Length];
                spent += wait;
                yield return new WaitForSeconds(wait);
            }

            IsPowered = true;
            ApplyLighting(true);
            restoring = null;
            PowerRestored?.Invoke();
        }

        private void ApplyLighting(bool powered)
        {
            Color ambient = powered ? poweredAmbient : unpoweredAmbient;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambient * 1.3f + new Color(0f, 0.003f, 0.012f);
            RenderSettings.ambientEquatorColor = ambient;
            RenderSettings.ambientGroundColor = ambient * 0.35f;

            foreach (Light light in mainLights)
                if (light != null) light.enabled = powered;
            foreach (Light light in emergencyLights)
                if (light != null) light.enabled = !powered;

            Material bulb = HouseMaterials.Get(powered ? HouseMaterials.Surface.BulbOn : HouseMaterials.Surface.BulbOff);
            foreach (Renderer renderer in bulbs)
                if (renderer != null) renderer.sharedMaterial = bulb;

            Material emergency = HouseMaterials.Get(powered ? HouseMaterials.Surface.BulbOff : HouseMaterials.Surface.EmergencyOn);
            foreach (Renderer renderer in emergencyBulbs)
                if (renderer != null) renderer.sharedMaterial = emergency;

            foreach (AudioSource loop in poweredLoops)
            {
                if (loop == null) continue;
                if (powered && !loop.isPlaying) loop.Play();
                else if (!powered && loop.isPlaying) loop.Stop();
            }
        }
    }
}
