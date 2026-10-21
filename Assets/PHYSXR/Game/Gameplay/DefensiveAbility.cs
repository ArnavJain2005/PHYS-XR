using System;
using System.Globalization;
using PHYSXR.Game.Art;
using PHYSXR.Game.Audio;
using PHYSXR.Ghost;
using PHYSXR.Telemetry;
using UnityEngine;

namespace PHYSXR.Game.Gameplay
{
    // The lantern: the player's light, and the one defensive interaction
    // (GDD 8, Technical Spec "Defensive interaction: the lantern pulse").
    //
    //  Hit test   ghost within 4.0 m, within 45 degrees of the aim direction
    //             (the camera), clear line of sight to the ghost's chest.
    //  Effect     GhostMotionController.TryInterrupt(): INTERRUPTED at once
    //             (3.0 s stagger, 2.5 s retreat). Never damages the ghost.
    //  Works in   NORMAL, WINDUP, RECOVER - not during the 0.3 s strike.
    //  Cooldown   10 s after a hit, 3 s after a miss, so the flash can never
    //             strobe faster than 3 Hz; its brightness is capped.
    //  Feedback   white flash, a sting, the ghost's recoil, the HUD ring.
    //
    // The hit is a game-physics event, like a collision; the decision layer
    // only sees its consequence (ghost phase INTERRUPTED -> E0 RETREAT).
    public class DefensiveAbility : MonoBehaviour
    {
        public const float Range = 4.0f;
        public const float HalfAngle = 45f;
        public const float HitCooldown = 10f;
        public const float MissCooldown = 3f;
        private const float BaseIntensity = 3.0f;
        private const float FlashIntensity = 9f;

        private Transform aim;
        private GhostMotionController ghost;
        private GameAudio audioPlayer;
        private TelemetryChannel telemetry = TelemetryChannel.Null;
        private Light lanternLight;
        private Light fill;
        private Transform viewModel;
        private Material flameMaterial;
        private float cooldownUntil;
        private float cooldownLength = 1f;
        private float flashUntil;
        private bool readyAnnounced = true;
        private float bob;

        public bool Ready => Time.time >= cooldownUntil;

        // 0 = just used, 1 = ready.
        public float Charge => Ready ? 1f : 1f - (cooldownUntil - Time.time) / cooldownLength;

        public int Uses { get; private set; }
        public int Hits { get; private set; }

        public string LastResult { get; private set; } = string.Empty;

        public event Action<bool> Pulsed;

        public void Initialize(Transform aim, GhostMotionController ghost, GameAudio audioPlayer, TelemetryChannel telemetry)
        {
            this.aim = aim;
            this.ghost = ghost;
            this.audioPlayer = audioPlayer;
            this.telemetry = telemetry ?? TelemetryChannel.Null;
            BuildViewModel();
        }

        public void SetTelemetry(TelemetryChannel channel)
        {
            telemetry = channel ?? TelemetryChannel.Null;
        }

        private void BuildViewModel()
        {
            viewModel = new GameObject("Lantern").transform;
            viewModel.SetParent(aim, false);
            viewModel.localPosition = new Vector3(0.3f, -0.36f, 0.62f);
            viewModel.localScale = Vector3.one * 0.7f;

            Material metal = HouseMaterials.Get(HouseMaterials.Surface.Metal);
            Material brass = HouseMaterials.Get(HouseMaterials.Surface.Brass);
            Material glass = HouseMaterials.Get(HouseMaterials.Surface.Glass);
            flameMaterial = new Material(HouseMaterials.Get(HouseMaterials.Surface.CandleFlame));

            Piece(PrimitiveType.Cube, new Vector3(0f, -0.09f, 0f), new Vector3(0.11f, 0.02f, 0.11f), brass);
            Piece(PrimitiveType.Cube, new Vector3(0f, 0.09f, 0f), new Vector3(0.12f, 0.02f, 0.12f), brass);
            Piece(PrimitiveType.Cylinder, new Vector3(0f, 0f, 0f), new Vector3(0.085f, 0.085f, 0.085f), glass);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                Piece(PrimitiveType.Cube, new Vector3(Mathf.Cos(a) * 0.05f, 0f, Mathf.Sin(a) * 0.05f), new Vector3(0.008f, 0.17f, 0.008f), metal);
            }
            Piece(PrimitiveType.Sphere, new Vector3(0f, -0.01f, 0f), new Vector3(0.025f, 0.05f, 0.025f), flameMaterial);
            Piece(PrimitiveType.Cylinder, new Vector3(0f, 0.14f, 0f), new Vector3(0.012f, 0.04f, 0.012f), metal);

            var lightGo = new GameObject("LanternLight");
            lightGo.transform.SetParent(viewModel, false);
            lightGo.transform.localPosition = new Vector3(-0.05f, 0.05f, 0.05f);
            lanternLight = lightGo.AddComponent<Light>();
            lanternLight.type = LightType.Point;
            lanternLight.color = new Color(1f, 0.72f, 0.42f);
            lanternLight.range = 9f;
            lanternLight.intensity = BaseIntensity;
            lanternLight.shadows = LightShadows.Soft;
            lanternLight.shadowStrength = 0.85f;
            lanternLight.shadowNearPlane = 0.25f;

            var fillGo = new GameObject("LanternFill");
            fillGo.transform.SetParent(aim, false);
            fillGo.transform.localPosition = new Vector3(0f, 0f, 0.8f);
            fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.color = new Color(1f, 0.8f, 0.6f);
            fill.range = 3.5f;
            fill.intensity = 0.6f;
            fill.shadows = LightShadows.None;
        }

        private void Piece(PrimitiveType type, Vector3 localPosition, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(viewModel, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // Called by PlayerController on the defend intent.
        public bool TryPulse()
        {
            if (!Ready)
            {
                audioPlayer?.Play2D(AudioCue.Invalid, 0.4f);
                LastResult = "recharging";
                return false;
            }

            Uses++;
            flashUntil = Time.time + 0.28f;
            audioPlayer?.Play2D(AudioCue.LanternPulse);

            string missReason;
            bool hit = HitTest(out float distance, out float angle, out missReason);
            GhostMotionController.InterruptResult result = GhostMotionController.InterruptResult.Inactive;
            if (hit)
            {
                result = ghost.TryInterrupt();
                hit = result == GhostMotionController.InterruptResult.Interrupted;
                if (!hit)
                    missReason = result.ToString();
            }

            if (hit)
                Hits++;
            else
                audioPlayer?.Play2D(AudioCue.LanternMiss, 0.6f);

            cooldownLength = hit ? HitCooldown : MissCooldown;
            cooldownUntil = Time.time + cooldownLength;
            readyAnnounced = false;
            LastResult = hit ? "hit" : missReason;

            telemetry.Emit("DEFENSE", string.Format(CultureInfo.InvariantCulture,
                "hit={0} reason={1} distance={2:F2} angle={3:F1} cooldown={4:F0} ghost_phase={5}",
                hit, hit ? "INTERRUPTED" : missReason, distance, angle, cooldownLength, ghost != null ? ghost.Phase.ToString() : "NONE"));
            Pulsed?.Invoke(hit);
            return hit;
        }

        // Range, aim cone and line of sight to the chest.
        public bool HitTest(out float distance, out float angle, out string missReason)
        {
            distance = 99f;
            angle = 180f;
            if (ghost == null || ghost.Dormant || ghost.Visual == null || ghost.Visual.Visibility < 0.5f)
            {
                missReason = "NO_TARGET";
                return false;
            }

            Vector3 chest = ghost.ChestPosition;
            Vector3 to = chest - aim.position;
            distance = to.magnitude;
            angle = Vector3.Angle(aim.forward, to);
            if (distance > Range)
            {
                missReason = "OUT_OF_RANGE";
                return false;
            }
            if (angle > HalfAngle)
            {
                missReason = "OUTSIDE_CONE";
                return false;
            }
            if (Physics.Linecast(aim.position, chest, out RaycastHit block, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !block.collider.transform.IsChildOf(ghost.transform) && !block.collider.transform.IsChildOf(transform))
            {
                missReason = "NO_LINE_OF_SIGHT";
                return false;
            }

            missReason = string.Empty;
            return true;
        }

        private void Update()
        {
            if (lanternLight == null)
                return;

            float t = Time.time;
            float flicker = 1f + (Mathf.PerlinNoise(t * 7f, 0.1f) - 0.5f) * 0.18f;
            float flash = t < flashUntil ? Mathf.Lerp(FlashIntensity, BaseIntensity, 1f - (flashUntil - t) / 0.28f) : BaseIntensity;
            float dim = Ready ? 1f : Mathf.Lerp(0.55f, 1f, Charge);
            lanternLight.intensity = Mathf.Min(FlashIntensity, flash * flicker * dim);
            lanternLight.color = t < flashUntil ? Color.Lerp(new Color(1f, 0.72f, 0.42f), new Color(0.9f, 0.95f, 1f), (flashUntil - t) / 0.28f)
                : new Color(1f, 0.72f, 0.42f);
            flameMaterial.SetColor("_EmissionColor", new Color(6f, 3.2f, 1f) * flicker * dim);

            if (!readyAnnounced && Ready)
            {
                readyAnnounced = true;
                audioPlayer?.Play2D(AudioCue.LanternReady);
            }

            bob += Time.deltaTime;
            viewModel.localPosition = new Vector3(0.3f, -0.36f + Mathf.Sin(bob * 1.7f) * 0.004f, 0.62f);
            viewModel.localRotation = Quaternion.Euler(Mathf.Sin(bob * 1.1f) * 1.2f, -8f, Mathf.Sin(bob * 0.8f) * 2f);
        }

        // Turns the lantern off for the ending and the death screen.
        public void SetLit(bool lit)
        {
            if (lanternLight != null) lanternLight.enabled = lit;
            if (fill != null) fill.enabled = lit;
            if (viewModel != null) viewModel.gameObject.SetActive(lit);
        }

        private void OnDestroy()
        {
            if (flameMaterial != null)
                Destroy(flameMaterial);
        }
    }
}
