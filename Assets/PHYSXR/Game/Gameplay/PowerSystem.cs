using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PHYSXR.Game.Gameplay
{
    // Owns the house's two lighting states. Placeholder greybox lighting
    // only; the horror lighting pass is M10.
    //
    //   Unpowered: dark flat ambient, a few dim red emergency lights, ceiling
    //              lights off, bulb meshes dark.
    //   Powered:   brighter ambient, warm ceiling lights on, emergency lights
    //              off, bulb meshes lit.
    //
    // RestorePower() plays a short flicker and then raises PowerRestored
    // once; the GameFlowController turns that into the objective's power
    // confirmation. The flicker uses scaled time, so it waits while paused.
    public class PowerSystem : MonoBehaviour
    {
        [SerializeField] private float surgeSeconds = 0.8f;
        [SerializeField] private Color unpoweredAmbient = new Color(0.035f, 0.04f, 0.055f);
        [SerializeField] private Color poweredAmbient = new Color(0.2f, 0.19f, 0.17f);
        [SerializeField] private Color bulbOffColor = new Color(0.12f, 0.12f, 0.1f);
        [SerializeField] private Color bulbOnColor = new Color(1f, 0.92f, 0.7f);

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private readonly List<Light> mainLights = new List<Light>();
        private readonly List<Light> emergencyLights = new List<Light>();
        private readonly List<Renderer> bulbs = new List<Renderer>();
        private Coroutine restoring;

        public bool IsPowered { get; private set; }

        public bool IsRestoring => restoring != null;

        public IReadOnlyList<Light> MainLights => mainLights;

        public IReadOnlyList<Light> EmergencyLights => emergencyLights;

        public event Action PowerRestored;

        public void Register(IEnumerable<Light> main, IEnumerable<Light> emergency, IEnumerable<Renderer> bulbRenderers)
        {
            mainLights.AddRange(main);
            emergencyLights.AddRange(emergency);
            bulbs.AddRange(bulbRenderers);
        }

        public void ApplyUnpowered()
        {
            IsPowered = false;
            ApplyLighting(false);
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

        private IEnumerator Surge()
        {
            float[] pattern = { 0.12f, 0.08f, 0.15f, 0.1f };
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
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = powered ? poweredAmbient : unpoweredAmbient;

            foreach (Light light in mainLights)
                if (light != null) light.enabled = powered;
            foreach (Light light in emergencyLights)
                if (light != null) light.enabled = !powered;

            Color bulb = powered ? bulbOnColor : bulbOffColor;
            foreach (Renderer renderer in bulbs)
            {
                if (renderer == null) continue;
                Material material = renderer.material;
                material.color = bulb;
                // Emission is best-effort: the variant may be stripped from
                // player builds, so the light change above is the real cue.
                material.EnableKeyword("_EMISSION");
                material.SetColor(EmissionColorId, powered ? bulbOnColor * 2f : Color.black);
            }
        }
    }
}
