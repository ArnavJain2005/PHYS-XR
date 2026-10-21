using System.Collections.Generic;
using UnityEngine;

namespace PHYSXR.Ghost
{
    // The Hollow's body: an original, code-built apparition (GDD 9.1, "an
    // unnaturally distorted humanoid") - a tall, gaunt figure in a tattered
    // shroud with an elongated pale head, arms too long for its body and two
    // pinpoint eyes. It floats; the hem drags on the floor.
    //
    // Animation is procedural: every frame the current pose blends toward a
    // target pose chosen by the motion controller (one per behaviour), with
    // walk bob, sway, a cloth-like hem and occasional "glitches" (a head snap
    // or a brief vertical stretch) that make the movement abrupt but readable.
    // Glitch timing comes from a seeded generator, so a replay looks the same.
    //
    // Presentation only: it knows nothing about decisions, WorldState or
    // sensors. Everything it creates (meshes, material instances) it destroys.
    public class HollowVisual : MonoBehaviour
    {
        public enum Pose
        {
            Drift,
            Warn,
            Investigate,
            Protect,
            Recoil,
            Windup,
            Strike,
            Recover,
            Stagger,
            Watch
        }

        private struct PoseValues
        {
            public float lean, hunch, headTilt, armRaise, armReach, armSpread, eye, agitation, stretch, shake;

            public static PoseValues For(Pose pose)
            {
                switch (pose)
                {
                    case Pose.Warn: return new PoseValues { lean = 16, hunch = 0.25f, headTilt = 18, armRaise = 0.25f, armReach = 0.55f, armSpread = 0.15f, eye = 1.4f, agitation = 0.8f, stretch = 1.05f };
                    case Pose.Investigate: return new PoseValues { lean = 10, hunch = 0.3f, headTilt = 38, armRaise = 0.1f, armReach = 0.3f, eye = 0.9f, agitation = 0.4f, stretch = 1f };
                    case Pose.Protect: return new PoseValues { lean = -3, hunch = 0f, headTilt = 0, armRaise = 0.35f, armReach = 0.05f, armSpread = 0.9f, eye = 0.45f, agitation = 0.15f, stretch = 1.02f };
                    case Pose.Recoil: return new PoseValues { lean = -12, hunch = 0.8f, headTilt = -12, armRaise = 0.95f, armReach = 0.2f, armSpread = -0.3f, eye = 0.35f, agitation = 0.6f, stretch = 0.95f };
                    case Pose.Windup: return new PoseValues { lean = -14, hunch = 0.1f, headTilt = 6, armRaise = 1.1f, armReach = 0.1f, armSpread = 0.6f, eye = 2.2f, agitation = 1f, stretch = 1.1f };
                    case Pose.Strike: return new PoseValues { lean = 38, hunch = 0.2f, headTilt = 0, armRaise = 0.35f, armReach = 1.2f, armSpread = -0.2f, eye = 2.6f, agitation = 1f, stretch = 1.08f };
                    case Pose.Recover: return new PoseValues { lean = 22, hunch = 0.7f, headTilt = 24, armRaise = 0f, armReach = 0.1f, eye = 0.8f, agitation = 0.5f, stretch = 0.98f };
                    case Pose.Stagger: return new PoseValues { lean = -18, hunch = 0.9f, headTilt = -25, armRaise = 1f, armReach = 0.15f, armSpread = -0.4f, eye = 0.2f, agitation = 1f, stretch = 0.92f, shake = 1f };
                    case Pose.Watch: return new PoseValues { lean = 4, hunch = 0.15f, headTilt = 10, armRaise = 0f, armReach = 0f, eye = 0.75f, agitation = 0.25f, stretch = 1f };
                    default: return new PoseValues { lean = 8, hunch = 0.2f, headTilt = 6, armRaise = 0.04f, armReach = 0.05f, eye = 0.6f, agitation = 0.35f, stretch = 1f };
                }
            }

            public static PoseValues Lerp(PoseValues a, PoseValues b, float t)
            {
                return new PoseValues
                {
                    lean = Mathf.Lerp(a.lean, b.lean, t), hunch = Mathf.Lerp(a.hunch, b.hunch, t),
                    headTilt = Mathf.Lerp(a.headTilt, b.headTilt, t), armRaise = Mathf.Lerp(a.armRaise, b.armRaise, t),
                    armReach = Mathf.Lerp(a.armReach, b.armReach, t), armSpread = Mathf.Lerp(a.armSpread, b.armSpread, t),
                    eye = Mathf.Lerp(a.eye, b.eye, t), agitation = Mathf.Lerp(a.agitation, b.agitation, t),
                    stretch = Mathf.Lerp(a.stretch, b.stretch, t), shake = Mathf.Lerp(a.shake, b.shake, t)
                };
            }
        }

        private const int ShroudSegments = 18;
        private const int ShroudRings = 11;
        private const float ShroudTop = 1.55f;

        private readonly List<Object> owned = new List<Object>();
        private readonly List<Renderer> renderers = new List<Renderer>();

        private Transform body, chest, neck, head, armL, armR, foreL, foreR;
        private Mesh shroud;
        private Vector3[] shroudBase, shroudVerts;
        private float[] hemJag;
        private Material shroudMat, skinMat, eyeMat, wispMat;
        private Light glow;
        private ParticleSystem wisps, mist;

        private PoseValues current = PoseValues.For(Pose.Drift);
        private Pose target = Pose.Drift;
        private float blendSpeed = 4f;
        private float speed;
        private float walkPhase;
        private float time;
        private uint glitchSeed = 0x9E3779B9u;
        private float nextGlitch = 3f;
        private float glitchUntil;
        private float glitchYaw;
        private float glitchStretch = 1f;
        private float flash;
        private float visibility = 1f;
        private float visibilityTarget = 1f;
        private Color eyeBase0;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public Pose CurrentPose => target;

        public float Visibility => visibility;

        public Transform Head => head;

        public Vector3 ChestPosition => chest != null ? chest.position : transform.position + Vector3.up * 1.4f;

        public void Build(IGhostPresentationAssets assets)
        {
            shroudMat = Own(assets.CreateShroudMaterial());
            skinMat = Own(assets.CreateSkinMaterial());
            eyeMat = Own(assets.CreateEyeMaterial());
            wispMat = Own(assets.CreateWispMaterial());
            eyeBase0 = eyeMat.HasProperty(EmissionColorId) ? eyeMat.GetColor(EmissionColorId) : Color.white;

            body = Node("Body", transform, new Vector3(0f, 0f, 0f));
            chest = Node("Chest", body, new Vector3(0f, 1.45f, 0f));
            Part("Torso", chest, PrimitiveType.Capsule, new Vector3(0f, -0.2f, 0f), new Vector3(0.3f, 0.36f, 0.2f), shroudMat, true);
            Part("Ribs", chest, PrimitiveType.Capsule, new Vector3(0f, -0.05f, 0.03f), new Vector3(0.3f, 0.3f, 0.18f), shroudMat, false);

            neck = Node("Neck", chest, new Vector3(0f, 0.3f, 0.02f));
            Part("NeckBone", neck, PrimitiveType.Capsule, new Vector3(0f, 0.08f, 0f), new Vector3(0.055f, 0.14f, 0.055f), shroudMat, false);
            head = Node("Head", neck, new Vector3(0f, 0.27f, 0.02f));
            Part("Skull", head, PrimitiveType.Sphere, new Vector3(0f, 0.04f, 0f), new Vector3(0.21f, 0.34f, 0.24f), skinMat, true);
            Part("Hood", head, PrimitiveType.Sphere, new Vector3(0f, 0.09f, -0.055f), new Vector3(0.3f, 0.43f, 0.29f), shroudMat, false);
            Part("SocketL", head, PrimitiveType.Sphere, new Vector3(-0.045f, 0.06f, 0.095f), new Vector3(0.06f, 0.075f, 0.04f), shroudMat, false);
            Part("SocketR", head, PrimitiveType.Sphere, new Vector3(0.045f, 0.06f, 0.095f), new Vector3(0.06f, 0.075f, 0.04f), shroudMat, false);
            Part("EyeL", head, PrimitiveType.Sphere, new Vector3(-0.045f, 0.058f, 0.112f), Vector3.one * 0.018f, eyeMat, false);
            Part("EyeR", head, PrimitiveType.Sphere, new Vector3(0.045f, 0.058f, 0.112f), Vector3.one * 0.018f, eyeMat, false);
            Part("Mouth", head, PrimitiveType.Sphere, new Vector3(0f, -0.085f, 0.098f), new Vector3(0.035f, 0.13f, 0.03f), shroudMat, false);

            armL = Arm("ArmL", -1f, out foreL);
            armR = Arm("ArmR", 1f, out foreR);

            BuildShroud();
            BuildParticles();

            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(head, false);
            lightGo.transform.localPosition = new Vector3(0f, -0.3f, 0.32f);
            glow = lightGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(0.6f, 0.72f, 1f);
            glow.range = 2.6f;
            glow.intensity = 0.2f;
            glow.shadows = LightShadows.None;
        }

        private T Own<T>(T obj) where T : Object
        {
            owned.Add(obj);
            return obj;
        }

        private Transform Node(string name, Transform parent, Vector3 localPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            return t;
        }

        private void Part(string name, Transform parent, PrimitiveType type, Vector3 localPosition, Vector3 scale, Material material, bool castShadows)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            renderers.Add(r);
        }

        private Transform Arm(string name, float side, out Transform forearm)
        {
            Transform shoulder = Node(name, chest, new Vector3(0.2f * side, 0.12f, 0f));
            Part("Upper", shoulder, PrimitiveType.Capsule, new Vector3(0f, -0.25f, 0f), new Vector3(0.07f, 0.27f, 0.07f), shroudMat, true);
            forearm = Node("Fore", shoulder, new Vector3(0f, -0.52f, 0f));
            Part("Lower", forearm, PrimitiveType.Capsule, new Vector3(0f, -0.25f, 0f), new Vector3(0.055f, 0.27f, 0.055f), skinMat, true);
            Transform hand = Node("Hand", forearm, new Vector3(0f, -0.52f, 0f));
            for (int f = 0; f < 4; f++)
            {
                float spread = (f - 1.5f) * 0.02f;
                Part("Finger" + f, hand, PrimitiveType.Capsule, new Vector3(spread, -0.1f, 0.01f), new Vector3(0.016f, 0.11f + f % 2 * 0.02f, 0.016f), skinMat, false);
            }
            return shoulder;
        }

        private void BuildShroud()
        {
            var go = new GameObject("Shroud");
            go.transform.SetParent(body, false);
            shroud = Own(new Mesh { name = "HollowShroud" });
            shroud.MarkDynamic();

            int ringVerts = ShroudSegments + 1;
            shroudBase = new Vector3[ringVerts * ShroudRings];
            hemJag = new float[ringVerts];
            var uvs = new Vector2[shroudBase.Length];
            uint seed = 12345u;
            for (int s = 0; s < ringVerts; s++)
                hemJag[s] = s == ShroudSegments ? hemJag[0] : 0.05f + Rand(ref seed) * 0.22f;

            for (int r = 0; r < ShroudRings; r++)
            {
                float k = (float)r / (ShroudRings - 1);
                float y = Mathf.Lerp(ShroudTop, 0.02f, k);
                float radius = Mathf.Lerp(0.17f, 0.46f, Mathf.Pow(k, 0.8f));
                for (int s = 0; s < ringVerts; s++)
                {
                    float a = (float)s / ShroudSegments * Mathf.PI * 2f;
                    float jag = r == ShroudRings - 1 ? hemJag[s] : 0f;
                    shroudBase[r * ringVerts + s] = new Vector3(Mathf.Sin(a) * radius, y + jag, Mathf.Cos(a) * radius * 0.85f);
                    uvs[r * ringVerts + s] = new Vector2((float)s / ShroudSegments, 1f - k);
                }
            }

            var tris = new List<int>();
            for (int r = 0; r < ShroudRings - 1; r++)
            for (int s = 0; s < ShroudSegments; s++)
            {
                int a = r * ringVerts + s, b = a + 1, c = a + ringVerts, d = c + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }

            shroudVerts = (Vector3[])shroudBase.Clone();
            shroud.vertices = shroudVerts;
            shroud.uv = uvs;
            shroud.SetTriangles(tris, 0);
            shroud.RecalculateNormals();
            shroud.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = shroud;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = shroudMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderers.Add(mr);
        }

        private void BuildParticles()
        {
            wisps = MakeParticles("Wisps", new Vector3(0f, 0.9f, 0f), 14f, 1.8f, 0.35f, 0.9f, new Color(0.05f, 0.05f, 0.06f, 0.35f), 0.25f, 0.5f);
            mist = MakeParticles("Mist", new Vector3(0f, 0.05f, 0f), 6f, 2.6f, 0.8f, 1.6f, new Color(0.25f, 0.28f, 0.32f, 0.12f), 0.05f, 0.6f);
        }

        private ParticleSystem MakeParticles(string name, Vector3 at, float rate, float life, float minSize, float maxSize, Color color, float upward, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(body, false);
            go.transform.localPosition = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = life;
            main.startSpeed = 0.05f;
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            main.gravityModifier = -upward * 0.1f;
            var emission = ps.emission;
            emission.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.6f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = wispMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        public void SetPose(Pose pose, float blend = 4f)
        {
            target = pose;
            blendSpeed = blend;
        }

        public void SetSpeed(float metresPerSecond)
        {
            speed = metresPerSecond;
        }

        // Lantern hit or other shock: brief bright flare.
        public void Flash()
        {
            flash = 1f;
        }

        // 0 hides the figure (dormant), 1 fully visible. Fades over ~0.5 s.
        public void SetVisible(bool visible, bool instant = false)
        {
            visibilityTarget = visible ? 1f : 0f;
            if (instant)
                ApplyVisibility(visibilityTarget);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;

            time += dt;
            PoseValues goal = PoseValues.For(target);
            current = PoseValues.Lerp(current, goal, 1f - Mathf.Exp(-blendSpeed * dt));

            UpdateGlitch(goal);
            walkPhase += dt * Mathf.Lerp(1.2f, 7f, Mathf.Clamp01(speed / 3.5f));
            if (visibility != visibilityTarget)
                ApplyVisibility(Mathf.MoveTowards(visibility, visibilityTarget, dt * 2f));

            float bob = Mathf.Sin(walkPhase * 2f) * 0.045f * Mathf.Clamp01(speed) + Mathf.Sin(time * 1.3f) * 0.02f;
            float sway = Mathf.Sin(time * 0.9f) * 3f * current.agitation + Mathf.Sin(walkPhase) * 4f * Mathf.Clamp01(speed);
            float shake = current.shake > 0.01f ? (Mathf.PerlinNoise(time * 23f, 0.3f) - 0.5f) * 14f * current.shake : 0f;

            body.localPosition = new Vector3(0f, 0.04f + bob, 0f);
            body.localRotation = Quaternion.Euler(current.lean * 0.35f + shake, 0f, sway * 0.4f);
            body.localScale = new Vector3(1f, current.stretch * glitchStretch, 1f);

            chest.localRotation = Quaternion.Euler(current.lean * 0.65f + current.hunch * 25f, 0f, sway * 0.6f + shake * 0.5f);
            neck.localRotation = Quaternion.Euler(current.hunch * 18f, 0f, 0f);
            float twitch = (Mathf.PerlinNoise(time * 2.1f, 7.7f) - 0.5f) * 10f * current.agitation;
            head.localRotation = Quaternion.Euler(-current.hunch * 10f, glitchYaw + twitch, current.headTilt + shake);

            float swing = Mathf.Sin(walkPhase) * 18f * Mathf.Clamp01(speed) * (1f - current.armRaise);
            PoseArm(armL, foreL, -1f, swing);
            PoseArm(armR, foreR, 1f, -swing);

            AnimateShroud(dt);

            float eye = Mathf.Max(0f, current.eye) * (1f + flash * 3f) * visibility;
            if (glitchUntil > time)
                eye *= 1.8f;
            eyeMat.SetColor(EmissionColorId, eyeBase0 * eye);
            glow.intensity = (0.12f + 0.16f * eye) * visibility;
            glow.color = Color.Lerp(new Color(0.6f, 0.72f, 1f), new Color(1f, 0.35f, 0.3f), Mathf.Clamp01((current.eye - 1.2f) / 1.2f));
            flash = Mathf.MoveTowards(flash, 0f, dt * 2.5f);
        }

        private void PoseArm(Transform shoulder, Transform fore, float side, float swing)
        {
            float raise = current.armRaise * 150f;
            float reach = current.armReach * 70f;
            float spread = current.armSpread * 40f * side;
            shoulder.localRotation = Quaternion.Euler(-(raise * 0.6f + reach) + swing, 0f, spread + 6f * side);
            fore.localRotation = Quaternion.Euler(-(current.armRaise * 70f) - current.armReach * 15f - 10f, 0f, current.armRaise * -25f * side);
        }

        private void AnimateShroud(float dt)
        {
            int ringVerts = ShroudSegments + 1;
            float drag = Mathf.Clamp(speed, 0f, 3.5f) * 0.05f;
            for (int r = 0; r < ShroudRings; r++)
            {
                float k = (float)r / (ShroudRings - 1);
                float amp = k * k * (0.03f + 0.05f * current.agitation);
                for (int s = 0; s < ringVerts; s++)
                {
                    Vector3 b = shroudBase[r * ringVerts + s];
                    float a = (float)s / ShroudSegments * Mathf.PI * 2f;
                    float wave = Mathf.Sin(time * 2.3f + a * 3f + r * 0.6f) * amp;
                    float wave2 = Mathf.Sin(time * 3.7f - a * 5f + r) * amp * 0.5f;
                    shroudVerts[r * ringVerts + s] = new Vector3(
                        b.x * (1f + wave) + wave2 * 0.5f,
                        b.y,
                        b.z * (1f + wave) - drag * k * k);
                }
            }
            shroud.vertices = shroudVerts;
            shroud.RecalculateNormals();
        }

        private void UpdateGlitch(PoseValues goal)
        {
            if (time >= nextGlitch)
            {
                float interval = Mathf.Lerp(6f, 1.6f, Mathf.Clamp01(goal.agitation));
                nextGlitch = time + interval * (0.5f + Rand(ref glitchSeed));
                glitchUntil = time + 0.12f + Rand(ref glitchSeed) * 0.1f;
                if (Rand(ref glitchSeed) > 0.5f)
                    glitchYaw = (Rand(ref glitchSeed) > 0.5f ? 1f : -1f) * (25f + Rand(ref glitchSeed) * 25f);
                else
                    glitchStretch = 1.1f + Rand(ref glitchSeed) * 0.08f;
            }

            if (time > glitchUntil)
            {
                glitchYaw = Mathf.MoveTowards(glitchYaw, 0f, Time.deltaTime * 400f);
                glitchStretch = Mathf.MoveTowards(glitchStretch, 1f, Time.deltaTime * 3f);
            }
        }

        // Fading is a flicker, not alpha: while between hidden and visible the
        // figure blinks in and out with a duty cycle equal to the visibility.
        // Opaque materials avoid transparent self-sorting artefacts.
        private void ApplyVisibility(float v)
        {
            visibility = v;
            bool show = v >= 0.999f || (v > 0.01f && Rand(ref glitchSeed) < v);
            foreach (Renderer r in renderers)
                if (r != null && r.enabled != show)
                    r.enabled = show;

            if (glow != null)
                glow.enabled = show;
            SetEmitting(wisps, v > 0.3f);
            SetEmitting(mist, v > 0.3f);
        }

        private static void SetEmitting(ParticleSystem ps, bool on)
        {
            if (ps == null)
                return;
            var emission = ps.emission;
            emission.enabled = on;
        }

        private static float Rand(ref uint s)
        {
            s ^= s << 13;
            s ^= s >> 17;
            s ^= s << 5;
            return (s & 0xFFFFFF) / 16777215f;
        }

        private void OnDestroy()
        {
            foreach (Object o in owned)
                if (o != null)
                    Destroy(o);
            owned.Clear();
        }
    }
}
