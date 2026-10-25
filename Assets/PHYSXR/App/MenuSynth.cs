using System;

namespace PHYSXR.App
{
    // Original procedural sound for the welcome screen (no samples, no
    // third-party material). Pure math, safe on a worker thread.
    //
    //   Music   48 s stereo loop: a low drone, slow low strings (Am - F - Dm - E),
    //           sparse inharmonic piano notes, wind and two distant metallic
    //           swells, in a long room. Seamless by construction: every
    //           sustained oscillator has a whole number of cycles in 48 s, note
    //           tails wrap around, and the reverb is run over two periods with
    //           the second kept.
    //   Accent  the title reveal: a rising air swell into a soft low impact
    //           and a high shimmer.
    //   Begin   the transition into the house: a deep swell and a distant bell.
    //   Hover / Select   quiet, dark UI ticks.
    public static class MenuSynth
    {
        public const int Rate = 22050;
        public const float MusicSeconds = 48f;

        private const float TwoPi = (float)(Math.PI * 2.0);
        private static readonly float[] SineTable = BuildSine(4096);

        public sealed class Bank
        {
            public float[] Music;   // interleaved stereo
            public float[] Accent;  // interleaved stereo
            public float[] Begin;   // interleaved stereo
            public float[] Hover;   // mono
            public float[] Select;  // mono
        }

        public static Bank RenderAll()
        {
            return new Bank
            {
                Music = Music(),
                Accent = Accent(),
                Begin = BeginCue(),
                Hover = Hover(),
                Select = Select()
            };
        }

        // ------------------------------------------------------------ music

        public static float[] Music()
        {
            int n = (int)(MusicSeconds * Rate);
            var left = new float[n];
            var right = new float[n];
            float period = MusicSeconds;

            // Drone: A1 with a partner one cycle per loop sharp (a slow beat),
            // and a quiet fifth.
            float a1 = Q(55f, period), a1b = Q(55f + 1f / period, period), e2 = Q(82.41f, period);
            var rng = new Random(29);

            // Low strings: chords 12 s each, 4 s cross-fades, voices detuned.
            float[][] chords =
            {
                new[] { 110f, 130.81f, 164.81f },  // Am
                new[] { 87.31f, 130.81f, 220f },   // F
                new[] { 146.83f, 174.61f, 220f },  // Dm
                new[] { 82.41f, 207.65f, 246.94f } // E
            };
            for (int c = 0; c < chords.Length; c++)
                for (int k = 0; k < chords[c].Length; k++)
                    chords[c][k] = Q(chords[c][k], period);

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float swell = 0.75f + 0.25f * Sin(Q(1f / 24f, period) * t);
                float drone = (Sin(a1 * t) * 0.55f + Sin(a1b * t) * 0.45f) * 0.32f * swell
                              + Tri(e2 * t) * 0.07f * (0.6f + 0.4f * Sin(Q(1f / 16f, period) * t + 1f));
                drone = (float)Math.Tanh(drone * 1.4f) * 0.8f;

                float strings = 0f;
                for (int c = 0; c < chords.Length; c++)
                {
                    float env = ChordEnvelope(t, c * 12f, 12f, 4f, period);
                    if (env <= 0f)
                        continue;
                    foreach (float f in chords[c])
                    {
                        // Vibrato as phase modulation, so the phase stays periodic in the loop.
                        float vib = 0.02f * Sin(Q(0.21f, period) * t + f * 0.01f);
                        strings += SoftSaw(f * t + vib) * 0.5f + SoftSaw(Q(f * 1.003f, period) * t) * 0.5f;
                    }
                    strings *= env;
                }
                strings *= 0.09f;

                left[i] = drone + strings * 0.95f;
                right[i] = drone * 0.97f + strings;
            }

            AddWind(left, right, rng);

            // Sparse piano: (time, frequency, velocity, pan -1..1).
            float[][] notes =
            {
                new[] { 2.5f, 659.25f, 0.55f, -0.3f }, new[] { 6.2f, 440f, 0.45f, 0.25f }, new[] { 9.6f, 523.25f, 0.4f, -0.1f },
                new[] { 14.5f, 440f, 0.5f, 0.3f }, new[] { 18.3f, 523.25f, 0.38f, -0.25f }, new[] { 21.8f, 698.46f, 0.32f, 0.15f },
                new[] { 24.4f, 220f, 0.3f, 0f },
                new[] { 26.7f, 587.33f, 0.5f, -0.3f }, new[] { 30.2f, 698.46f, 0.4f, 0.3f }, new[] { 33.8f, 440f, 0.42f, -0.15f },
                new[] { 38.6f, 415.3f, 0.5f, 0.25f }, new[] { 42.1f, 493.88f, 0.38f, -0.25f }, new[] { 45.3f, 466.16f, 0.22f, 0.35f }
            };
            foreach (float[] note in notes)
                AddPiano(left, right, note[0], note[1], note[2], note[3], rng);

            AddMetalSwell(left, right, 16f, 1240f, -0.7f, rng);
            AddMetalSwell(left, right, 40f, 1530f, 0.65f, rng);

            ReverbPeriodic(left, right, 0.32f, 1.25f);
            Normalize(left, right, 0.72f);
            return Interleave(left, right);
        }

        // 1 inside [start, start+len], with cosine fades of `fade` seconds on
        // both sides, measured circularly over the loop period.
        private static float ChordEnvelope(float t, float start, float len, float fade, float period)
        {
            float rel = t - (start - fade);
            rel = ((rel % period) + period) % period;
            float total = len + 2f * fade;
            if (rel > total)
                return 0f;
            if (rel < fade * 2f)
                return 0.5f - 0.5f * (float)Math.Cos(Math.PI * rel / (fade * 2f));
            if (rel > len)
                return 0.5f - 0.5f * (float)Math.Cos(Math.PI * (total - rel) / (fade * 2f));
            return 1f;
        }

        private static void AddWind(float[] l, float[] r, Random rng)
        {
            int n = l.Length;
            var noise = new float[n];
            for (int i = 0; i < n; i++)
                noise[i] = (float)(rng.NextDouble() * 2.0 - 1.0);

            // Band-pass (difference of one-poles) run around the loop twice so
            // the filter state at the seam matches.
            float lo = 0f, lo2 = 0f;
            var outp = new float[n];
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float sweep = 0.5f + 0.5f * Sin(Q(1f / 12f, MusicSeconds) * t);
                float c1 = 0.012f + 0.02f * sweep, c2 = 0.0025f;
                lo += (noise[i] - lo) * c1;
                lo2 += (noise[i] - lo2) * c2;
                if (pass == 1)
                    outp[i] = (lo - lo2) * (0.35f + 0.65f * sweep);
            }
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float pan = 0.5f + 0.35f * Sin(Q(1f / 48f, MusicSeconds) * t);
                l[i] += outp[i] * 0.22f * (1f - pan);
                r[i] += outp[i] * 0.22f * pan;
            }
        }

        private static void AddPiano(float[] l, float[] r, float at, float f, float velocity, float pan, Random rng)
        {
            int n = l.Length;
            int start = (int)(at * Rate);
            int length = (int)(7f * Rate);
            const float inharm = 0.0004f;
            float gl = velocity * (1f - pan) * 0.5f, gr = velocity * (1f + pan) * 0.5f;
            for (int i = 0; i < length; i++)
            {
                float t = (float)i / Rate;
                float s = 0f;
                for (int h = 1; h <= 7; h++)
                {
                    float fh = f * h * (float)Math.Sqrt(1f + inharm * h * h);
                    float decay = (float)Math.Exp(-t * (0.55f + h * 0.38f));
                    s += Sin(fh * t) * decay / (float)Math.Pow(h, 1.35);
                }
                float attack = Math.Min(1f, t / 0.004f);
                float hammer = t < 0.02f ? (float)(rng.NextDouble() * 2.0 - 1.0) * (1f - t / 0.02f) * 0.15f : 0f;
                float v = (s * attack + hammer) * 0.22f;
                int j = (start + i) % n;
                l[j] += v * gl;
                r[j] += v * gr;
            }
        }

        // A distant bowed-metal resonance: noise through a narrow resonator.
        private static void AddMetalSwell(float[] l, float[] r, float at, float freq, float pan, Random rng)
        {
            int n = l.Length;
            int start = (int)(at * Rate);
            int length = (int)(5f * Rate);
            float w = TwoPi * freq / Rate, q = 0.9975f;
            float y1 = 0f, y2 = 0f;
            float a1 = 2f * q * (float)Math.Cos(w), a2 = -q * q;
            float gl = (1f - pan) * 0.5f, gr = (1f + pan) * 0.5f;
            for (int i = 0; i < length; i++)
            {
                float t = (float)i / Rate;
                float env = (float)Math.Sin(Math.PI * Math.Min(1.0, t / 5.0));
                float x = (float)(rng.NextDouble() * 2.0 - 1.0) * env * env;
                float y = x * (1f - q) + a1 * y1 + a2 * y2;
                y2 = y1;
                y1 = y;
                int j = (start + i) % n;
                l[j] += y * 0.55f * gl;
                r[j] += y * 0.55f * gr;
            }
        }

        // ------------------------------------------------------------ cues

        public static float[] Accent()
        {
            int n = (int)(6.5f * Rate);
            var l = new float[n];
            var r = new float[n];
            var rng = new Random(41);
            float lo = 0f, lo2 = 0f;
            const float hit = 1.7f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                // Rising air into the hit.
                float rise = t < hit ? (float)Math.Pow(t / hit, 2.4) : (float)Math.Exp(-(t - hit) * 7f);
                float c = 0.01f + 0.12f * Math.Min(1f, t / hit);
                float nz = (float)(rng.NextDouble() * 2.0 - 1.0);
                lo += (nz - lo) * c;
                lo2 += (nz - lo2) * 0.004f;
                float air = (lo - lo2) * rise * 0.6f;

                // Soft low impact with a falling pitch.
                float th = t - hit;
                float boom = 0f, shimmer = 0f;
                if (th >= 0f)
                {
                    float f = 38f + 24f * (float)Math.Exp(-th * 3f);
                    boom = Sin(f * th) * (float)Math.Exp(-th * 1.4f) * Math.Min(1f, th / 0.02f) * 0.75f;
                    shimmer = (Sin(880f * th) * 0.5f + Sin(1318.5f * th) * 0.35f + Sin(1321.5f * th) * 0.35f)
                              * (float)Math.Exp(-th * 0.9f) * Math.Min(1f, th / 0.3f) * 0.07f;
                }
                l[i] = air * 0.9f + boom + shimmer * 1.1f;
                r[i] = air + boom + shimmer * 0.9f;
            }
            Reverb(l, r, 0.4f, 1.4f);
            FadeOut(l, r, 0.6f);
            Normalize(l, r, 0.8f);
            return Interleave(l, r);
        }

        public static float[] BeginCue()
        {
            int n = (int)(5.5f * Rate);
            var l = new float[n];
            var r = new float[n];
            var rng = new Random(53);
            float lo = 0f;
            float[] bell = { 1f, 2.0f, 2.76f, 5.4f, 8.93f };
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float sub = Sin(41f * t) * Math.Min(1f, t / 1.1f) * (float)Math.Exp(-Math.Max(0f, t - 1.1f) * 1.2f) * 0.6f;
                float nz = (float)(rng.NextDouble() * 2.0 - 1.0);
                lo += (nz - lo) * 0.03f;
                float breath = lo * (float)Math.Sin(Math.PI * Math.Min(1.0, t / 1.6)) * 0.5f;
                float tb = t - 0.9f, toll = 0f;
                if (tb >= 0f)
                    for (int k = 0; k < bell.Length; k++)
                        toll += Sin(110f * bell[k] * tb) * (float)Math.Exp(-tb * (0.7f + k * 0.55f)) / (1f + k);
                toll *= 0.22f * Math.Min(1f, tb / 0.003f);
                l[i] = sub + breath * 0.8f + toll * 0.9f;
                r[i] = sub + breath + toll;
            }
            Reverb(l, r, 0.45f, 1.5f);
            FadeOut(l, r, 1f);
            Normalize(l, r, 0.8f);
            return Interleave(l, r);
        }

        public static float[] Hover()
        {
            int n = (int)(0.07f * Rate);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                s[i] = (Sin(1150f * t) * 0.6f + Sin(575f * t) * 0.4f) * (float)Math.Exp(-t / 0.014f) * Math.Min(1f, t / 0.002f);
            }
            NormalizeMono(s, 0.5f);
            return s;
        }

        public static float[] Select()
        {
            int n = (int)(0.45f * Rate);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float env = (float)Math.Exp(-t / 0.09f) * Math.Min(1f, t / 0.003f);
                s[i] = (Sin(392f * t) * 0.55f + Sin(587.33f * t) * 0.35f + Sin(196f * t) * 0.4f * (float)Math.Exp(-t / 0.2f)) * env;
            }
            NormalizeMono(s, 0.7f);
            return s;
        }

        // ------------------------------------------------------------ dsp

        // A small Schroeder room per channel.
        private static void Reverb(float[] l, float[] r, float wet, float size)
        {
            ApplyRoom(l, wet, size, 0);
            ApplyRoom(r, wet, size, 1);
        }

        // Reverb for a loop: process two periods, keep the second.
        private static void ReverbPeriodic(float[] l, float[] r, float wet, float size)
        {
            l = PeriodicRoom(l, wet, size, 0);
            r = PeriodicRoom(r, wet, size, 1);
        }

        private static float[] PeriodicRoom(float[] s, float wet, float size, int channel)
        {
            int n = s.Length;
            var twice = new float[n * 2];
            Array.Copy(s, 0, twice, 0, n);
            Array.Copy(s, 0, twice, n, n);
            ApplyRoom(twice, wet, size, channel);
            Array.Copy(twice, n, s, 0, n);
            return s;
        }

        private static void ApplyRoom(float[] s, float wet, float size, int channel)
        {
            int spread = channel * 23;
            int[] combs = { 1116 + spread, 1188 + spread, 1277 + spread, 1356 + spread };
            float[] fb = { 0.84f, 0.83f, 0.82f, 0.81f };
            var bufs = new float[combs.Length][];
            var idx = new int[combs.Length];
            var damp = new float[combs.Length];
            for (int c = 0; c < combs.Length; c++)
                bufs[c] = new float[Math.Max(1, (int)(combs[c] * size * Rate / 44100f))];
            int[] aps = { 556 + spread, 441 + spread };
            var apb = new float[aps.Length][];
            var api = new int[aps.Length];
            for (int a = 0; a < aps.Length; a++)
                apb[a] = new float[Math.Max(1, (int)(aps[a] * Rate / 44100f))];

            for (int i = 0; i < s.Length; i++)
            {
                float input = s[i];
                float acc = 0f;
                for (int c = 0; c < combs.Length; c++)
                {
                    float y = bufs[c][idx[c]];
                    damp[c] = y * 0.6f + damp[c] * 0.4f;
                    bufs[c][idx[c]] = input * 0.25f + damp[c] * fb[c];
                    idx[c] = (idx[c] + 1) % bufs[c].Length;
                    acc += y;
                }
                for (int a = 0; a < aps.Length; a++)
                {
                    float bv = apb[a][api[a]];
                    float o = -acc + bv;
                    apb[a][api[a]] = acc + bv * 0.5f;
                    api[a] = (api[a] + 1) % apb[a].Length;
                    acc = o;
                }
                s[i] = input * (1f - wet * 0.5f) + acc * wet;
            }
        }

        private static void FadeOut(float[] l, float[] r, float seconds)
        {
            int f = Math.Min(l.Length, (int)(seconds * Rate));
            for (int i = 0; i < f; i++)
            {
                float k = (float)i / f;
                l[l.Length - 1 - i] *= k;
                r[r.Length - 1 - i] *= k;
            }
        }

        private static void Normalize(float[] l, float[] r, float peak)
        {
            float m = 1e-6f;
            for (int i = 0; i < l.Length; i++)
                m = Math.Max(m, Math.Max(Math.Abs(l[i]), Math.Abs(r[i])));
            float g = peak / m;
            for (int i = 0; i < l.Length; i++)
            {
                l[i] *= g;
                r[i] *= g;
            }
        }

        private static void NormalizeMono(float[] s, float peak)
        {
            float m = 1e-6f;
            foreach (float v in s)
                m = Math.Max(m, Math.Abs(v));
            for (int i = 0; i < s.Length; i++)
                s[i] *= peak / m;
        }

        private static float[] Interleave(float[] l, float[] r)
        {
            var o = new float[l.Length * 2];
            for (int i = 0; i < l.Length; i++)
            {
                o[i * 2] = l[i];
                o[i * 2 + 1] = r[i];
            }
            return o;
        }

        // Nearest frequency with a whole number of cycles in the loop.
        private static float Q(float f, float period) => (float)Math.Round(f * period) / period;

        private static float[] BuildSine(int size)
        {
            var t = new float[size + 1];
            for (int i = 0; i <= size; i++)
                t[i] = (float)Math.Sin(i * Math.PI * 2.0 / size);
            return t;
        }

        // sin(2 pi x) from the table; x in cycles.
        private static float Sin(float cycles)
        {
            float p = cycles - (float)Math.Floor(cycles);
            float f = p * (SineTable.Length - 1);
            int i = (int)f;
            return SineTable[i] + (SineTable[i + 1] - SineTable[i]) * (f - i);
        }

        private static float Tri(float cycles)
        {
            float p = cycles - (float)Math.Floor(cycles);
            return 4f * Math.Abs(p - 0.5f) - 1f;
        }

        // A mellow saw from 8 harmonics with a steep roll-off.
        private static float SoftSaw(float cycles)
        {
            float s = 0f;
            for (int h = 1; h <= 8; h++)
                s += Sin(cycles * h) / (float)Math.Pow(h, 1.5);
            return s * 0.6f;
        }
    }
}
