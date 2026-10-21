using UnityEngine;

namespace PHYSXR.Game.Art
{
    // Gives a light an unsteady, failing-bulb character. Steady() suppresses
    // it (the ghost's PROTECT behaviour: "steady lights"); Pulse() gives one
    // short burst of failure (the ghost's WARN entry flickers the nearest
    // light). Optional emissive renderer follows the light.
    public class FlickerLight : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private Light target;
        private Renderer bulb;
        private MaterialPropertyBlock block;
        private Color bulbEmission;
        private float baseIntensity;
        private float roughness;
        private float seed;
        private float burstUntil;
        private bool steady;

        public float BaseIntensity
        {
            get => baseIntensity;
            set => baseIntensity = value;
        }

        public void Initialize(Light light, float roughness, Renderer bulbRenderer = null, Color bulbEmission = default)
        {
            target = light;
            baseIntensity = light.intensity;
            this.roughness = roughness;
            bulb = bulbRenderer;
            this.bulbEmission = bulbEmission;
            seed = (transform.position.x * 13.1f + transform.position.z * 7.7f) % 100f;
            if (bulb != null)
                block = new MaterialPropertyBlock();
        }

        public void SetSteady(bool value)
        {
            steady = value;
        }

        public void Pulse(float seconds = 0.9f)
        {
            burstUntil = Time.time + seconds;
        }

        private void Update()
        {
            if (target == null)
                return;

            if (!target.enabled)
            {
                // Hand the bulb back to its material while the light is off.
                if (bulb != null && bulb.HasPropertyBlock())
                    bulb.SetPropertyBlock(null);
                return;
            }

            float t = Time.time;
            float k = 1f;
            if (t < burstUntil)
            {
                k = Mathf.PerlinNoise(t * 31f, seed) > 0.45f ? 0.05f : 1f;
            }
            else if (!steady && roughness > 0f)
            {
                float n = Mathf.PerlinNoise(t * 9f, seed);
                float dropout = Mathf.PerlinNoise(t * 1.3f, seed + 4f) > 0.82f && n > 0.5f ? 0.15f : 1f;
                k = Mathf.Lerp(1f, 0.75f + n * 0.4f, roughness) * dropout;
            }

            target.intensity = baseIntensity * k;
            if (bulb != null)
            {
                block.SetColor(EmissionColorId, bulbEmission * k);
                bulb.SetPropertyBlock(block);
            }
        }
    }
}
