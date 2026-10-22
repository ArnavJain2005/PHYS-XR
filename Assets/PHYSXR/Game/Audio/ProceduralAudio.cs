using System;
using System.Collections.Generic;
using UnityEngine;

namespace PHYSXR.Game.Audio
{
    public enum AudioCue
    {
        AmbienceHouse,
        AmbienceStorm,
        Thunder,
        MenuMusic,
        FootstepWood,
        FootstepTile,
        ClockTick,
        HouseCreak,
        UiHover,
        UiSelect,
        ObjectiveUpdate,
        FusePickup,
        FuseInsert,
        Invalid,
        DoorLocked,
        DoorOpen,
        PowerSurge,
        PowerHum,
        LightBuzz,
        Flicker,
        GhostBreath,
        GhostWhisper,
        GhostScreech,
        GhostThud,
        GhostHiss,
        GhostManifest,
        AttackRise,
        StrikeImpact,
        StrikeWhiff,
        ProtectHum,
        LanternPulse,
        LanternMiss,
        LanternReady,
        Heartbeat,
        DeathSting,
        WinSting,
        RadioStatic,
        NoteRustle,
        CandleCrackle,
        GhostPresence,
        GhostDrag,
        ScoreDrone,
        ScoreStrings,
        ScorePulse,
        ScoreSwell,
        DistantKnock,
        PipeGroan,
        WaterDrip
    }

    // Every sound in the game, synthesised in code: oscillators, filtered
    // noise and envelopes, with no recorded or third-party samples. Clips are
    // generated on first use (or all at once by WarmAll while the menu is up)
    // and cached for the process lifetime, so scene reloads never regenerate
    // or leak them. Loops are rendered slightly long and crossfaded end-into-
    // start so they repeat without a click.
    public static class ProceduralAudio
    {
        public const int Rate = 22050;

        private static readonly Dictionary<long, AudioClip> Cache = new Dictionary<long, AudioClip>();

        public static int VariantCount(AudioCue cue)
        {
            switch (cue)
            {
                case AudioCue.FootstepWood:
                case AudioCue.FootstepTile:
                case AudioCue.HouseCreak:
                    return 4;
                case AudioCue.Thunder:
                case AudioCue.DistantKnock:
                case AudioCue.WaterDrip:
                    return 3;
                case AudioCue.PipeGroan:
                    return 2;
                default:
                    return 1;
            }
        }

        public static bool IsLoop(AudioCue cue)
        {
            switch (cue)
            {
                case AudioCue.AmbienceHouse:
                case AudioCue.AmbienceStorm:
                case AudioCue.MenuMusic:
                case AudioCue.ClockTick:
                case AudioCue.PowerHum:
                case AudioCue.LightBuzz:
                case AudioCue.GhostBreath:
                case AudioCue.GhostWhisper:
                case AudioCue.ProtectHum:
                case AudioCue.Heartbeat:
                case AudioCue.RadioStatic:
                case AudioCue.CandleCrackle:
                case AudioCue.GhostPresence:
                case AudioCue.GhostDrag:
                case AudioCue.ScoreDrone:
                case AudioCue.ScoreStrings:
                case AudioCue.ScorePulse:
                    return true;
                default:
                    return false;
            }
        }

        public static AudioClip Get(AudioCue cue, int variant = 0)
        {
            variant = Mathf.Clamp(variant, 0, VariantCount(cue) - 1);
            long key = (long)cue * 16 + variant;
            if (Cache.TryGetValue(key, out AudioClip cached) && cached != null)
                return cached;

            float[] samples = Render(cue, variant);
            Normalize(samples, 0.89f);
            AudioClip clip = AudioClip.Create("PHYSXR_" + cue + "_" + variant, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            Cache[key] = clip;
            return clip;
        }

        public static void WarmAll()
        {
            foreach (AudioCue cue in Enum.GetValues(typeof(AudioCue)))
                for (int v = 0; v < VariantCount(cue); v++)
                    Get(cue, v);
        }

        // ------------------------------------------------------------ DSP kit

        private sealed class Rng
        {
            private uint state;
            public Rng(uint seed) { state = seed == 0 ? 1u : seed; }
            public float Next()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return (state & 0xFFFFFF) / 16777215f;
            }
            public float Bipolar() => Next() * 2f - 1f;
        }

        private struct OnePole
        {
            private float y;
            private float a;
            public OnePole(float cutoff) { y = 0f; a = Coef(cutoff); }
            public void Set(float cutoff) { a = Coef(cutoff); }
            private static float Coef(float cutoff) => 1f - Mathf.Exp(-2f * Mathf.PI * Mathf.Max(1f, cutoff) / Rate);
            public float Low(float x) { y += a * (x - y); return y; }
            public float High(float x) { y += a * (x - y); return x - y; }
        }

        private struct Biquad
        {
            private float b0, b1, b2, a1, a2, x1, x2, y1, y2;

            public static Biquad BandPass(float freq, float q)
            {
                var f = new Biquad();
                f.SetBandPass(freq, q);
                return f;
            }

            public void SetBandPass(float freq, float q)
            {
                float w = 2f * Mathf.PI * Mathf.Clamp(freq, 20f, Rate * 0.45f) / Rate;
                float alpha = Mathf.Sin(w) / (2f * q);
                float a0 = 1f + alpha;
                b0 = alpha / a0; b1 = 0f; b2 = -alpha / a0;
                a1 = -2f * Mathf.Cos(w) / a0; a2 = (1f - alpha) / a0;
            }

            public float Process(float x)
            {
                float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return y;
            }
        }

        private static float[] Buffer(float seconds) => new float[Mathf.Max(1, Mathf.RoundToInt(seconds * Rate))];

        private static float T(int i) => (float)i / Rate;

        private static float Sine(float phase) => Mathf.Sin(phase * 2f * Mathf.PI);

        private static float Saw(float phase) => 2f * (phase - Mathf.Floor(phase + 0.5f));

        private static float Env(float t, float attack, float decay)
        {
            if (t < 0f) return 0f;
            if (t < attack) return t / attack;
            return Mathf.Exp(-(t - attack) / Mathf.Max(0.0001f, decay));
        }

        private static void Normalize(float[] s, float peak)
        {
            float max = 0f;
            for (int i = 0; i < s.Length; i++) max = Mathf.Max(max, Mathf.Abs(s[i]));
            if (max < 1e-6f) return;
            float g = peak / max;
            for (int i = 0; i < s.Length; i++) s[i] *= g;
        }

        // Render `seconds + fade`, then fold the tail over the head.
        private static float[] Loop(float seconds, float fade, Func<int, float> sample)
        {
            int n = Mathf.RoundToInt(seconds * Rate);
            int f = Mathf.RoundToInt(fade * Rate);
            var raw = new float[n + f];
            for (int i = 0; i < raw.Length; i++) raw[i] = sample(i);
            var outp = new float[n];
            Array.Copy(raw, outp, n);
            for (int i = 0; i < f; i++)
            {
                float k = (float)i / f;
                outp[i] = outp[i] * k + raw[n + i] * (1f - k);
            }
            return outp;
        }

        // A few feedback combs: cheap "room" tail.
        private static void Room(float[] s, float wet, float size)
        {
            int[] delays = { (int)(0.0297f * Rate * size), (int)(0.0371f * Rate * size), (int)(0.0411f * Rate * size), (int)(0.0437f * Rate * size) };
            var buffers = new float[delays.Length][];
            var idx = new int[delays.Length];
            for (int d = 0; d < delays.Length; d++) buffers[d] = new float[Mathf.Max(1, delays[d])];
            for (int i = 0; i < s.Length; i++)
            {
                float acc = 0f;
                for (int d = 0; d < delays.Length; d++)
                {
                    float y = buffers[d][idx[d]];
                    buffers[d][idx[d]] = s[i] + y * 0.78f;
                    idx[d] = (idx[d] + 1) % buffers[d].Length;
                    acc += y;
                }
                s[i] = s[i] * (1f - wet) + acc * 0.25f * wet;
            }
        }

        // ------------------------------------------------------------ cues

        private static float[] Render(AudioCue cue, int v)
        {
            var rng = new Rng((uint)(1000 + (int)cue * 97 + v * 13));
            switch (cue)
            {
                case AudioCue.AmbienceHouse: return HouseAmbience(rng);
                case AudioCue.AmbienceStorm: return Storm(rng);
                case AudioCue.Thunder: return Thunder(rng, v);
                case AudioCue.MenuMusic: return MenuMusic(rng);
                case AudioCue.FootstepWood: return Footstep(rng, v, false);
                case AudioCue.FootstepTile: return Footstep(rng, v, true);
                case AudioCue.ClockTick: return ClockTick(rng);
                case AudioCue.HouseCreak: return Creak(rng, v);
                case AudioCue.UiHover: return Blip(880f, 0.05f, 0.6f);
                case AudioCue.UiSelect: return TwoNote(660f, 990f);
                case AudioCue.ObjectiveUpdate: return Bell(rng, 220f, 1.8f);
                case AudioCue.FusePickup: return Clink(rng);
                case AudioCue.FuseInsert: return Clunk(rng);
                case AudioCue.Invalid: return Knock(rng);
                case AudioCue.DoorLocked: return Rattle(rng);
                case AudioCue.DoorOpen: return DoorCreak(rng);
                case AudioCue.PowerSurge: return Surge(rng);
                case AudioCue.PowerHum: return Hum(rng, 50f, 0.15f);
                case AudioCue.LightBuzz: return Hum(rng, 100f, 0.6f);
                case AudioCue.Flicker: return Crackle(rng, 0.25f, 40f);
                case AudioCue.GhostBreath: return Breath(rng);
                case AudioCue.GhostWhisper: return Whisper(rng);
                case AudioCue.GhostScreech: return Screech(rng);
                case AudioCue.GhostThud: return Thud(rng);
                case AudioCue.GhostHiss: return Hiss(rng);
                case AudioCue.GhostManifest: return Manifest(rng);
                case AudioCue.AttackRise: return Rise(rng);
                case AudioCue.StrikeImpact: return Impact(rng);
                case AudioCue.StrikeWhiff: return Whoosh(rng, 0.5f, 2200f, 400f);
                case AudioCue.ProtectHum: return ProtectChord();
                case AudioCue.LanternPulse: return Shimmer(rng);
                case AudioCue.LanternMiss: return Crackle(rng, 0.6f, 25f);
                case AudioCue.LanternReady: return Blip(1320f, 0.35f, 1f);
                case AudioCue.Heartbeat: return Heartbeat();
                case AudioCue.DeathSting: return DeathHit(rng);
                case AudioCue.WinSting: return WinPad();
                case AudioCue.RadioStatic: return Static(rng);
                case AudioCue.NoteRustle: return Rustle(rng);
                case AudioCue.CandleCrackle: return Candle(rng);
                case AudioCue.GhostPresence: return Presence(rng);
                case AudioCue.GhostDrag: return Drag(rng);
                case AudioCue.ScoreDrone: return ScoreDrone(rng);
                case AudioCue.ScoreStrings: return ScoreStrings(rng);
                case AudioCue.ScorePulse: return ScorePulse(rng);
                case AudioCue.ScoreSwell: return ScoreSwell(rng);
                case AudioCue.DistantKnock: return DistantKnock(rng, v);
                case AudioCue.PipeGroan: return PipeGroan(rng, v);
                case AudioCue.WaterDrip: return Drip(rng, v);
                default: return Buffer(0.1f);
            }
        }

        private static float[] HouseAmbience(Rng rng)
        {
            var wind = new OnePole(260f);
            var wind2 = new OnePole(90f);
            float brown = 0f;
            return Loop(24f, 2f, i =>
            {
                float t = T(i);
                float drone = Sine(41.2f * t) * (0.5f + 0.3f * Mathf.Sin(t * 0.26f))
                            + Sine(55.1f * t) * (0.35f + 0.25f * Mathf.Sin(t * 0.17f + 1f))
                            + Sine(61.9f * t) * 0.18f * (0.5f + 0.5f * Mathf.Sin(t * 0.11f + 2f))
                            + Sine(110.4f * t) * 0.06f + Sine(164.3f * t) * 0.03f * Mathf.Sin(t * 0.3f);
                brown = (brown + rng.Bipolar() * 0.02f) * 0.998f;
                float gust = 0.4f + 0.6f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(t * 0.21f + Mathf.Sin(t * 0.07f) * 2f), 3f);
                float air = wind.Low(rng.Bipolar()) * gust * 1.4f + wind2.Low(brown) * 3f;
                return drone * 0.55f + air * 0.6f;
            });
        }

        private static float[] Storm(Rng rng)
        {
            var hp = new OnePole(900f);
            var lp = new OnePole(5200f);
            var rumble = new OnePole(70f);
            float b0 = 0f, b1 = 0f, b2 = 0f, brown = 0f;
            return Loop(16f, 2f, i =>
            {
                float t = T(i);
                float white = rng.Bipolar();
                b0 = 0.99765f * b0 + white * 0.0990460f;
                b1 = 0.96300f * b1 + white * 0.2965164f;
                b2 = 0.57000f * b2 + white * 1.0526913f;
                float pink = (b0 + b1 + b2 + white * 0.1848f) * 0.2f;
                float rain = lp.Low(hp.High(pink));
                float drops = rng.Next() > 0.9985f ? rng.Bipolar() * 0.6f : 0f;
                float gust = 0.65f + 0.35f * Mathf.Sin(t * 0.39f + Mathf.Sin(t * 0.13f) * 3f);
                brown = (brown + rng.Bipolar() * 0.02f) * 0.997f;
                return rain * gust + drops * 0.3f + rumble.Low(brown) * 2.5f;
            });
        }

        private static float[] Thunder(Rng rng, int v)
        {
            var s = Buffer(5f);
            var lp = new OnePole(v == 0 ? 500f : 260f);
            var crackHp = new OnePole(1500f);
            float brown = 0f, roll = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                brown = (brown + rng.Bipolar() * 0.05f) * 0.995f;
                if (i % 400 == 0) roll = 0.4f + rng.Next() * 0.6f;
                float env = Env(t - 0.05f * v, 0.08f + v * 0.15f, 1.3f + v * 0.3f);
                float body = lp.Low(brown * 6f + rng.Bipolar() * 0.3f) * env * (0.7f + 0.3f * roll);
                float crack = v == 0 ? crackHp.High(rng.Bipolar()) * Env(t, 0.003f, 0.05f) * 0.6f : 0f;
                s[i] = body + crack;
            }
            return s;
        }

        private static float[] MenuMusic(Rng rng)
        {
            // Am - F - Dm - E, 8 s each, low register pads with a sparse bell.
            float[][] chords =
            {
                new[] { 110f, 130.81f, 164.81f, 220f },
                new[] { 87.31f, 130.81f, 174.61f, 220f },
                new[] { 73.42f, 146.83f, 174.61f, 220f },
                new[] { 82.41f, 123.47f, 164.81f, 207.65f }
            };
            float[] bells = { 659.25f, 523.25f, 587.33f, 493.88f, 440f, 523.25f, 415.3f, 440f };
            var filter = new OnePole(600f);
            float[] s = Loop(32f, 3f, i =>
            {
                float t = T(i);
                int c = (int)(t / 8f) % 4;
                float local = t % 8f;
                float swell = Mathf.SmoothStep(0f, 1f, Mathf.Min(local / 2.5f, (8f - local) / 2.5f + 0.4f));
                float pad = 0f;
                foreach (float f in chords[c])
                    pad += Saw(f * t) * 0.5f + Saw(f * 1.004f * t) * 0.5f;
                filter.Set(350f + 250f * (0.5f + 0.5f * Mathf.Sin(t * 0.2f)));
                float bell = 0f;
                float bt = t % 4f;
                int bi = (int)(t / 4f) % bells.Length;
                bell = (Sine(bells[bi] * t) + 0.3f * Sine(bells[bi] * 2.76f * t)) * Env(bt, 0.01f, 1.4f);
                return filter.Low(pad) * 0.35f * swell + bell * 0.12f;
            });
            Room(s, 0.45f, 1.6f);
            return s;
        }

        private static float[] Footstep(Rng rng, int v, bool tile)
        {
            var s = Buffer(0.32f);
            var lp = new OnePole(tile ? 1800f : 650f);
            var hp = new OnePole(tile ? 1500f : 700f);
            float pitch = 1f + (v - 1.5f) * 0.06f;
            var creak = Biquad.BandPass(820f * pitch, 6f);
            bool hasCreak = !tile && v % 2 == 1;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float thump = Sine((tile ? 95f : 72f) * pitch * t) * Env(t, 0.002f, tile ? 0.03f : 0.05f);
                float scuff = lp.Low(rng.Bipolar()) * Env(t, 0.001f, tile ? 0.02f : 0.04f);
                float click = tile ? hp.High(rng.Bipolar()) * Env(t, 0.0005f, 0.012f) * 0.8f : 0f;
                float cr = hasCreak ? creak.Process(rng.Bipolar()) * Env(t - 0.04f, 0.03f, 0.08f) * 1.5f : 0f;
                s[i] = thump * 0.9f + scuff * 0.7f + click + cr;
            }
            return s;
        }

        private static float[] ClockTick(Rng rng)
        {
            var bpTick = Biquad.BandPass(2600f, 12f);
            var bpTock = Biquad.BandPass(1700f, 12f);
            return Loop(2f, 0.01f, i =>
            {
                float t = T(i) % 2f;
                float tick = bpTick.Process(rng.Bipolar() * Env(t, 0.0005f, 0.006f)) * 3f;
                float tock = bpTock.Process(rng.Bipolar() * Env(t - 1f, 0.0005f, 0.007f)) * 3f;
                return tick + tock;
            });
        }

        private static float[] Creak(Rng rng, int v)
        {
            var s = Buffer(1.6f);
            var bp = Biquad.BandPass(300f, 9f);
            float f0 = 260f + v * 45f, f1 = 140f + v * 20f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float k = t / 1.6f;
                bp.SetBandPass(Mathf.Lerp(f0, f1, k) * (1f + 0.04f * Mathf.Sin(t * 37f)), 9f);
                float grain = (rng.Next() > 0.6f ? rng.Bipolar() : 0f) + Saw(Mathf.Lerp(f0, f1, k) * 0.5f * t) * 0.3f;
                float env = Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI) * (0.6f + 0.4f * Mathf.Sin(t * 13f));
                s[i] = bp.Process(grain) * env * 2f;
            }
            Room(s, 0.4f, 1.2f);
            return s;
        }

        private static float[] Blip(float freq, float seconds, float level)
        {
            var s = Buffer(seconds);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                s[i] = (Sine(freq * t) + 0.25f * Sine(freq * 2.01f * t)) * Env(t, 0.004f, seconds * 0.35f) * level;
            }
            return s;
        }

        private static float[] TwoNote(float a, float b)
        {
            var s = Buffer(0.25f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float f = t < 0.07f ? a : b;
                s[i] = Sine(f * t) * Env(t < 0.07f ? t : t - 0.07f, 0.004f, 0.06f);
            }
            return s;
        }

        private static float[] Bell(Rng rng, float f, float seconds)
        {
            var s = Buffer(seconds);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                s[i] = (Sine(f * t) + 0.5f * Sine(f * 2.0f * t) * Env(t, 0.002f, 0.6f) + 0.3f * Sine(f * 2.76f * t) * Env(t, 0.002f, 0.4f))
                       * Env(t, 0.004f, seconds * 0.4f);
            }
            Room(s, 0.3f, 1.4f);
            return s;
        }

        private static float[] Clink(Rng rng)
        {
            var s = Buffer(0.6f);
            float[] partials = { 1240f, 1873f, 2651f, 3312f };
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float m = 0f;
                for (int p = 0; p < partials.Length; p++)
                    m += Sine(partials[p] * t) * Env(t, 0.001f, 0.25f / (1 + p * 0.5f)) / (1 + p);
                s[i] = m + rng.Bipolar() * Env(t, 0.0005f, 0.01f) * 0.4f;
            }
            return s;
        }

        private static float[] Clunk(Rng rng)
        {
            var s = Buffer(0.6f);
            var lp = new OnePole(400f);
            var hp = new OnePole(2500f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float thump = Sine(68f * t) * Env(t, 0.002f, 0.09f) + lp.Low(rng.Bipolar()) * Env(t, 0.001f, 0.06f) * 1.2f;
                float click = hp.High(rng.Bipolar()) * Env(t - 0.2f, 0.0005f, 0.015f) * 0.8f
                              + Sine(2300f * t) * Env(t - 0.2f, 0.001f, 0.05f) * 0.3f;
                s[i] = thump + click;
            }
            Room(s, 0.25f, 0.8f);
            return s;
        }

        private static float[] Knock(Rng rng)
        {
            var s = Buffer(0.45f);
            var lp = new OnePole(500f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float k1 = Sine(150f * t) * Env(t, 0.001f, 0.04f) + lp.Low(rng.Bipolar()) * Env(t, 0.001f, 0.03f);
                float k2 = Sine(140f * t) * Env(t - 0.16f, 0.001f, 0.04f) * 0.8f;
                s[i] = k1 + k2;
            }
            return s;
        }

        private static float[] Rattle(Rng rng)
        {
            var s = Buffer(0.7f);
            var bp = Biquad.BandPass(1900f, 5f);
            float[] hits = { 0f, 0.07f, 0.12f, 0.2f, 0.26f, 0.35f, 0.42f };
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float e = 0f;
                foreach (float h in hits)
                    e += Env(t - h, 0.0005f, 0.018f);
                s[i] = bp.Process(rng.Bipolar() * e) * 2f + Sine(110f * t) * Env(t - 0.42f, 0.002f, 0.05f) * 0.6f;
            }
            return s;
        }

        private static float[] DoorCreak(Rng rng)
        {
            var s = Buffer(2.0f);
            var bp = Biquad.BandPass(500f, 10f);
            var hp = new OnePole(2000f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float k = Mathf.Clamp01(t / 1.6f);
                float f = Mathf.Lerp(150f, 95f, k) * (1f + 0.06f * Mathf.Sin(t * 23f) + 0.03f * rng.Bipolar());
                bp.SetBandPass(f * 3.2f, 8f);
                float src = Saw(f * t) * 0.7f + rng.Bipolar() * 0.3f;
                float env = Mathf.Sin(k * Mathf.PI) * (t < 1.6f ? 1f : 0f);
                float latch = hp.High(rng.Bipolar()) * Env(t, 0.0005f, 0.02f);
                s[i] = bp.Process(src) * env * 2.2f + latch * 0.6f;
            }
            Room(s, 0.35f, 1.3f);
            return s;
        }

        private static float[] Surge(Rng rng)
        {
            var s = Buffer(2.2f);
            var hp = new OnePole(1200f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float crackle = (rng.Next() > 0.93f ? rng.Bipolar() : 0f) * (t < 0.9f ? 1f : Env(t - 0.9f, 0.001f, 0.1f));
                float buzz = (Sine(50f * t) + 0.6f * Sine(100f * t) + 0.4f * Sine(150f * t) + 0.25f * Mathf.Sign(Sine(100f * t)))
                             * Mathf.SmoothStep(0.6f, 1.6f, t) * 0.5f;
                float thunk = Sine(55f * t) * Env(t - 0.85f, 0.003f, 0.2f) * 1.2f;
                s[i] = hp.High(crackle) * 1.4f + buzz + thunk;
            }
            return s;
        }

        private static float[] Hum(Rng rng, float f, float buzz)
        {
            return Loop(4f, 0.5f, i =>
            {
                float t = T(i);
                float hum = Sine(f * t) + 0.5f * Sine(2f * f * t) + 0.3f * Sine(3f * f * t) + 0.15f * Sine(5f * f * t);
                float b = Mathf.Clamp(Sine(2f * f * t) * 4f, -1f, 1f) * buzz * 0.4f;
                float flutter = 1f + 0.1f * Mathf.Sin(t * 7.3f) * buzz;
                return (hum * 0.6f + b) * flutter + rng.Bipolar() * 0.02f;
            });
        }

        private static float[] Crackle(Rng rng, float seconds, float density)
        {
            var s = Buffer(seconds);
            var hp = new OnePole(1800f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float pop = rng.Next() < density / Rate * 10f ? rng.Bipolar() : 0f;
                s[i] = hp.High(pop + rng.Bipolar() * 0.05f) * Env(t, 0.005f, seconds * 0.45f) * 2f;
            }
            return s;
        }

        private static float[] Breath(Rng rng)
        {
            var inhale = Biquad.BandPass(650f, 1.5f);
            var exhale = Biquad.BandPass(330f, 2f);
            return Loop(6f, 0.3f, i =>
            {
                float t = T(i) % 6f;
                float inEnv = t < 1.8f ? Mathf.Sin(t / 1.8f * Mathf.PI) : 0f;
                float exEnv = t > 2.1f && t < 4.6f ? Mathf.Sin((t - 2.1f) / 2.5f * Mathf.PI) : 0f;
                float growl = Sine(58f * t + 0.3f * Mathf.Sin(t * 30f)) * exEnv * 0.35f;
                return inhale.Process(rng.Bipolar()) * inEnv * 1.2f + exhale.Process(rng.Bipolar()) * exEnv * 1.6f + growl;
            });
        }

        private static float[] Whisper(Rng rng)
        {
            var f1 = Biquad.BandPass(900f, 4f);
            var f2 = Biquad.BandPass(2400f, 5f);
            float syll = 0f;
            int nextSyll = 0;
            return Loop(8f, 0.5f, i =>
            {
                float t = T(i);
                if (i >= nextSyll)
                {
                    nextSyll = i + (int)(Rate * (0.12f + rng.Next() * 0.25f));
                    syll = rng.Next() > 0.25f ? 0.4f + rng.Next() * 0.6f : 0f;
                }
                float phrase = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(t * 0.9f + Mathf.Sin(t * 0.3f) * 2f), 2f);
                f1.SetBandPass(700f + 500f * (0.5f + 0.5f * Mathf.Sin(t * 11f)), 4f);
                float n = rng.Bipolar();
                return (f1.Process(n) + f2.Process(n) * 0.6f) * syll * phrase * 1.5f;
            });
        }

        private static float[] Screech(Rng rng)
        {
            var s = Buffer(1.8f);
            float[] f = { 1400f, 1483f, 2107f, 2219f, 997f };
            var hp = new OnePole(3000f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float drop = 1f - 0.25f * Mathf.SmoothStep(0.3f, 1.6f, t);
                float vib = 1f + 0.02f * Mathf.Sin(t * 38f);
                float m = 0f;
                for (int p = 0; p < f.Length; p++)
                    m += Saw(f[p] * drop * vib * t) * (p == 4 ? 0.6f : 0.35f);
                s[i] = (m * 0.6f + hp.High(rng.Bipolar()) * 0.5f) * Env(t, 0.03f, 0.55f);
            }
            Room(s, 0.4f, 1.5f);
            return s;
        }

        private static float[] Thud(Rng rng)
        {
            var s = Buffer(1.8f);
            var lp = new OnePole(180f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                s[i] = Sine(48f * t) * Env(t, 0.004f, 0.18f) + lp.Low(rng.Bipolar()) * Env(t, 0.002f, 0.12f) * 2f;
            }
            Room(s, 0.6f, 1.8f);
            return s;
        }

        private static float[] Hiss(Rng rng)
        {
            var s = Buffer(1.3f);
            var bp = Biquad.BandPass(4200f, 1.2f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float flutter = 0.7f + 0.3f * Mathf.Sin(t * 31f);
                s[i] = bp.Process(rng.Bipolar()) * Env(t, 0.08f, 0.45f) * flutter * 2f;
            }
            return s;
        }

        private static float[] Manifest(Rng rng)
        {
            var s = Buffer(2.6f);
            var lp = new OnePole(800f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float swell = Mathf.Pow(Mathf.Clamp01(t / 2.3f), 3f) * (t < 2.35f ? 1f : Env(t - 2.35f, 0.001f, 0.04f));
                lp.Set(200f + 3000f * swell);
                float tone = Saw(55f * t) + Saw(58.3f * t) + Saw(110.5f * t) * 0.5f;
                s[i] = (lp.Low(rng.Bipolar() * 1.5f + tone * 0.4f)) * swell * 1.6f;
            }
            return s;
        }

        private static float[] Rise(Rng rng)
        {
            var s = Buffer(1.3f);
            var lp = new OnePole(900f);
            float phase = 0f, phase2 = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float k = Mathf.Clamp01(t / 1.2f);
                float f = Mathf.Lerp(180f, 820f, k * k);
                phase += f / Rate;
                phase2 += f * 1.06f / Rate;
                lp.Set(500f + 4000f * k);
                float tone = Saw(phase) + Saw(phase2);
                s[i] = lp.Low(tone * 0.6f + rng.Bipolar() * 0.4f * k) * k * (t < 1.2f ? 1f : Env(t - 1.2f, 0.001f, 0.03f));
            }
            return s;
        }

        private static float[] Impact(Rng rng)
        {
            var s = Buffer(1.6f);
            var lp = new OnePole(1200f);
            float phase = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float f = Mathf.Lerp(130f, 38f, Mathf.Clamp01(t / 0.5f));
                phase += f / Rate;
                s[i] = Sine(phase) * Env(t, 0.002f, 0.45f) * 1.3f + lp.Low(rng.Bipolar()) * Env(t, 0.001f, 0.12f) * 1.4f;
            }
            Room(s, 0.4f, 1.5f);
            return s;
        }

        private static float[] Whoosh(Rng rng, float seconds, float from, float to)
        {
            var s = Buffer(seconds);
            var bp = Biquad.BandPass(from, 2f);
            for (int i = 0; i < s.Length; i++)
            {
                float k = T(i) / seconds;
                bp.SetBandPass(Mathf.Lerp(from, to, k), 2f);
                s[i] = bp.Process(rng.Bipolar()) * Mathf.Sin(k * Mathf.PI) * 2f;
            }
            return s;
        }

        private static float[] ProtectChord()
        {
            return Loop(6f, 1f, i =>
            {
                float t = T(i);
                return Sine(110f * t) * 0.5f + Sine(164.8f * t) * 0.35f + Sine(220.6f * t) * 0.25f
                       + Sine(329.6f * t) * 0.1f * (0.5f + 0.5f * Mathf.Sin(t * 0.8f));
            });
        }

        private static float[] Shimmer(Rng rng)
        {
            var s = Buffer(1.4f);
            var bp = Biquad.BandPass(3000f, 1.5f);
            float[] f = { 1760f, 2637f, 3520f, 4186f };
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float trem = 0.6f + 0.4f * Mathf.Sin(t * 70f);
                float m = 0f;
                for (int p = 0; p < f.Length; p++) m += Sine(f[p] * t) / (p + 1);
                float whoosh = bp.Process(rng.Bipolar()) * Env(t, 0.01f, 0.2f);
                s[i] = m * trem * Env(t, 0.005f, 0.5f) * 0.6f + whoosh * 1.2f;
            }
            Room(s, 0.3f, 1.2f);
            return s;
        }

        private static float[] Heartbeat()
        {
            return Loop(1f, 0.01f, i =>
            {
                float t = T(i) % 1f;
                return Sine(58f * t) * Env(t, 0.008f, 0.07f) + Sine(46f * t) * Env(t - 0.28f, 0.008f, 0.09f) * 0.75f;
            });
        }

        private static float[] DeathHit(Rng rng)
        {
            var s = Buffer(3.5f);
            var lp = new OnePole(3000f);
            float[] f = { 55f, 58.3f, 82.4f, 87.3f, 116.5f };
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                lp.Set(Mathf.Lerp(3500f, 300f, Mathf.Clamp01(t / 2.5f)));
                float m = 0f;
                foreach (float x in f) m += Saw(x * t);
                s[i] = lp.Low(m * 0.4f + rng.Bipolar() * Env(t, 0.001f, 0.25f) * 1.5f) * Env(t, 0.005f, 1.3f);
            }
            Room(s, 0.5f, 1.8f);
            return s;
        }

        private static float[] WinPad()
        {
            var s = Buffer(6f);
            var lp = new OnePole(700f);
            float[] f = { 110f, 164.81f, 220f, 277.18f, 293.66f };
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float m = 0f;
                foreach (float x in f) m += Saw(x * t) * 0.5f + Saw(x * 1.003f * t) * 0.5f;
                s[i] = lp.Low(m) * 0.3f * Mathf.SmoothStep(0f, 1f, t / 2f) * Env(t - 3f, 0.001f, 1.5f);
            }
            Room(s, 0.5f, 1.8f);
            return s;
        }

        private static float[] Static(Rng rng)
        {
            var bp = Biquad.BandPass(2200f, 0.8f);
            return Loop(5f, 0.3f, i =>
            {
                float t = T(i);
                float crack = rng.Next() > 0.997f ? rng.Bipolar() * 3f : 0f;
                float warble = Sine((900f + 60f * Mathf.Sin(t * 3f)) * t) * 0.06f * (0.5f + 0.5f * Mathf.Sin(t * 0.7f));
                return bp.Process(rng.Bipolar() * 0.8f + crack) + warble;
            });
        }

        private static float[] Rustle(Rng rng)
        {
            var s = Buffer(0.6f);
            var hp = new OnePole(2200f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float grains = 0.5f + 0.5f * Mathf.Sin(t * 60f + Mathf.Sin(t * 13f) * 4f);
                s[i] = hp.High(rng.Bipolar()) * grains * Mathf.Sin(Mathf.Clamp01(t / 0.6f) * Mathf.PI);
            }
            return s;
        }

        private static float[] Candle(Rng rng)
        {
            var lp = new OnePole(1500f);
            return Loop(5f, 0.3f, i =>
            {
                float pop = rng.Next() > 0.9993f ? rng.Bipolar() : 0f;
                return lp.Low(rng.Bipolar() * 0.05f) + pop * 0.8f;
            });
        }
    
        // ------------------------------------------------- the Hollow, score

        // A low pressure around the ghost: two sub tones a fraction of a hertz
        // apart (a slow beat), with upper harmonics so small speakers still
        // carry it, and a faint breathy band on top.
        private static float[] Presence(Rng rng)
        {
            var air = Biquad.BandPass(190f, 2.5f);
            return Loop(12f, 1f, i =>
            {
                float t = T(i);
                float swell = 0.65f + 0.35f * Mathf.Sin(t * 2f * Mathf.PI / 12f * 2f);
                float sub = Sine(38f * t) + Sine(38.5f * t) * 0.8f;
                float harm = Sine(76f * t) * 0.35f + Sine(114.3f * t) * 0.18f + Sine(152.6f * t) * 0.08f;
                return (sub * 0.5f + harm) * swell * 0.6f + air.Process(rng.Bipolar()) * 0.25f * swell;
            });
        }

        // Cloth dragged over boards: irregular pulls of band-passed noise with
        // a soft low rub under each.
        private static float[] Drag(Rng rng)
        {
            var cloth = Biquad.BandPass(1300f, 1.1f);
            var rub = Biquad.BandPass(240f, 3f);
            float pull = 0f;
            int nextPull = 0;
            float pullLen = 0.6f;
            int pullStart = 0;
            return Loop(6f, 0.4f, i =>
            {
                if (i >= nextPull)
                {
                    pullStart = i;
                    pullLen = 0.45f + rng.Next() * 0.5f;
                    nextPull = i + (int)(Rate * (pullLen + 0.1f + rng.Next() * 0.35f));
                    pull = 0.6f + rng.Next() * 0.4f;
                }
                float k = (float)(i - pullStart) / (Rate * pullLen);
                float env = k < 1f ? Mathf.Sin(k * Mathf.PI) * pull : 0f;
                float n = rng.Bipolar();
                return cloth.Process(n) * env * 1.4f + rub.Process(n) * env * 0.8f;
            });
        }

        // Score layer 1, the tension bed: a dark low cluster (A, B-flat, E)
        // with slow, independent swells and filtered air.
        private static float[] ScoreDrone(Rng rng)
        {
            var air = new OnePole(380f);
            float brown = 0f;
            return Loop(32f, 3f, i =>
            {
                float t = T(i);
                float a = Sine(55f * t) + Sine(110.1f * t) * 0.4f + Sine(165.2f * t) * 0.15f;
                float b = Sine(58.27f * t + 0.2f * Mathf.Sin(t * 0.21f)) + Sine(116.6f * t) * 0.3f;
                float e = Sine(82.4f * t) + Sine(164.9f * t) * 0.25f;
                float sa = 0.55f + 0.45f * Mathf.Sin(t * 0.196f);
                float sb = 0.4f + 0.4f * Mathf.Sin(t * 0.137f + 2f);
                float se = 0.35f + 0.35f * Mathf.Sin(t * 0.093f + 4f);
                brown = (brown + rng.Bipolar() * 0.02f) * 0.997f;
                return (a * sa + b * sb * 0.8f + e * se * 0.6f) * 0.35f + air.Low(brown) * 2.2f;
            });
        }

        // Score layer 2, unease: high, glassy, bowed-noise bands a semitone
        // apart, swelling and receding with an uneven tremolo.
        private static float[] ScoreStrings(Rng rng)
        {
            var b1 = Biquad.BandPass(1760f, 40f);
            var b2 = Biquad.BandPass(1864.7f, 40f);
            var b3 = Biquad.BandPass(2637f, 45f);
            return Loop(24f, 2.5f, i =>
            {
                float t = T(i);
                float n = rng.Bipolar();
                float s1 = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(t * 0.33f), 2f);
                float s2 = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(t * 0.27f + 1.7f), 2f);
                float s3 = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(t * 0.19f + 3.1f), 3f);
                float trem = 0.75f + 0.25f * Mathf.Sin(t * 2f * Mathf.PI * (6.5f + 2f * Mathf.Sin(t * 0.4f)));
                float glass = Sine(3520f * t) * 0.02f * s3;
                return (b1.Process(n) * s1 + b2.Process(n) * s2 * 0.9f + b3.Process(n) * s3 * 0.7f) * trem * 6f + glass;
            });
        }

        // Score layer 3, escalation: a low throb at 66 bpm with an accent
        // every fourth beat and a muffled ostinato under it.
        private static float[] ScorePulse(Rng rng)
        {
            const float beat = 60f / 66f;
            var thump = new OnePole(260f);
            var ost = new OnePole(420f);
            return Loop(beat * 12f, 0.05f, i =>
            {
                float t = T(i);
                int n = Mathf.FloorToInt(t / beat);
                float bt = t - n * beat;
                float accent = n % 4 == 0 ? 1f : 0.6f;
                float kick = Mathf.Sin(2f * Mathf.PI * (44f * bt + 30f * (1f - Mathf.Exp(-bt * 18f)) / 18f)) * Env(bt, 0.004f, 0.28f);
                float knock = thump.Low(rng.Bipolar()) * Env(bt, 0.002f, 0.06f) * 3f;
                float tone = ost.Low(Saw((n % 3 == 2 ? 103.8f : 110f) * t)) * Env(bt, 0.02f, 0.45f) * 0.5f;
                return (kick * 0.9f + knock) * accent + tone;
            });
        }

        // A rising swell that cuts off: the moment before something happens.
        private static float[] ScoreSwell(Rng rng)
        {
            var s = Buffer(3.2f);
            var bp = Biquad.BandPass(300f, 2f);
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float k = Mathf.Clamp01(t / 2.6f);
                bp.SetBandPass(Mathf.Lerp(250f, 3200f, k * k), 2f);
                float rise = Mathf.Pow(k, 2.4f) * (t < 2.6f ? 1f : Mathf.Exp(-(t - 2.6f) * 14f));
                float cluster = Sine(Mathf.Lerp(110f, 220f, k) * t) + Sine(Mathf.Lerp(116.5f, 233f, k) * t) * 0.8f;
                s[i] = (bp.Process(rng.Bipolar()) * 1.5f + cluster * 0.35f) * rise;
            }
            Room(s, 0.35f, 1.4f);
            return s;
        }

        // ---------------------------------------------- house, one-shots

        // Two to four dull knocks somewhere inside the walls.
        private static float[] DistantKnock(Rng rng, int v)
        {
            var s = Buffer(2.4f);
            var lp = new OnePole(220f);
            int knocks = 2 + v % 3;
            float at = 0.05f;
            var times = new float[knocks];
            for (int k = 0; k < knocks; k++)
            {
                times[k] = at;
                at += 0.24f + rng.Next() * 0.22f;
            }
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float x = 0f;
                for (int k = 0; k < knocks; k++)
                {
                    float kt = t - times[k];
                    if (kt < 0f) continue;
                    x += (Sine((95f + v * 12f) * kt) * 0.8f + rng.Bipolar() * 0.5f) * Env(kt, 0.002f, 0.07f) * (k == knocks - 1 ? 0.7f : 1f);
                }
                s[i] = lp.Low(x) * 2.5f;
            }
            Room(s, 0.5f, 1.6f);
            return s;
        }

        // An old pipe groaning under pressure: a gliding buzz through narrow
        // resonances, swelling and dying away.
        private static float[] PipeGroan(Rng rng, int v)
        {
            float len = 2.6f + v * 0.6f;
            var s = Buffer(len);
            var r1 = Biquad.BandPass(420f + v * 60f, 14f);
            var r2 = Biquad.BandPass(910f + v * 90f, 18f);
            var r3 = Biquad.BandPass(1730f, 20f);
            float phase = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float k = t / len;
                float f = Mathf.Lerp(78f + v * 9f, 64f, k) * (1f + 0.03f * Mathf.Sin(t * 5.3f));
                phase += f / Rate;
                float src = Saw(phase) + rng.Bipolar() * 0.15f;
                float env = Mathf.Pow(Mathf.Sin(k * Mathf.PI), 1.5f) * (0.75f + 0.25f * Mathf.Sin(t * 9f));
                s[i] = (r1.Process(src) * 1.2f + r2.Process(src) + r3.Process(src) * 0.5f) * env * 3f;
            }
            Room(s, 0.45f, 1.8f);
            return s;
        }

        // A single drip landing in standing water.
        private static float[] Drip(Rng rng, int v)
        {
            var s = Buffer(0.9f);
            float f0 = 700f + v * 140f;
            float phase = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = T(i);
                float f = f0 * (1f + 1.4f * (1f - Mathf.Exp(-t * 60f)));
                phase += f / Rate;
                float plop = Sine(phase) * Env(t, 0.001f, 0.045f);
                float splash = rng.Bipolar() * Env(t, 0.0005f, 0.012f) * 0.3f;
                s[i] = plop + splash;
            }
            Room(s, 0.6f, 1.1f);
            return s;
        }
    }
}
