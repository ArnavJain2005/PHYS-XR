using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace PHYSXR.App
{
    // Procedural art for the main menu's Blackthorn House backdrop: night sky
    // with moon, drifting clouds, the house and bare trees in silhouette, lit
    // windows, mist, rain, a vignette and a distant figure for one window.
    //
    // Pixels are computed on a worker thread with plain math (no Unity API),
    // then turned into textures once, on the main thread. Results are cached
    // for the process lifetime, so returning to the menu never regenerates.
    // In batch mode (headless self-tests) nothing is generated.
    public static class MenuArt
    {
        public const int SceneW = 1280, SceneH = 720;

        public sealed class Pixels
        {
            public int W, H;
            public Color32[] Data;
        }

        public sealed class Generated
        {
            public Pixels Sky, Clouds, House, Fog, Rain, Vignette, Figure, Shade;
            // In scene UV (0..1, origin bottom-left): lit windows, gate lamps, the moon.
            public readonly List<Rect> Windows = new List<Rect>();
            public readonly List<Vector2> Lamps = new List<Vector2>();
            public int FigureWindow = -1;
            public Vector2 Moon;
        }

        public sealed class Textures
        {
            public Texture2D Sky, Clouds, House, Fog, Rain, Vignette, Figure, Shade;
            public Generated Layout;
        }

        private static Task<Generated> job;
        private static Textures ready;
        private static Sprite frame;

        // A hollow 1-unit border for sliced Images (UI's Outline effect
        // duplicates the whole quad, so it cannot draw a frame on a filled box).
        public static Sprite FrameSprite
        {
            get
            {
                if (frame != null)
                    return frame;
                var t = new Texture2D(4, 4, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave
                };
                var px = new Color32[16];
                for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    px[y * 4 + x] = new Color32(255, 255, 255, (byte)(x == 0 || y == 0 || x == 3 || y == 3 ? 255 : 0));
                t.SetPixels32(px);
                t.Apply(false, true);
                frame = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(1, 1, 1, 1));
                frame.hideFlags = HideFlags.DontSave;
                return frame;
            }
        }

        public static UnityEngine.UI.Image AddFrame(RectTransform parent, Color color)
        {
            UnityEngine.UI.Image img = PHYSXR.Game.UI.UIKit.Image(parent, "Frame", color, FrameSprite);
            img.type = UnityEngine.UI.Image.Type.Sliced;
            img.fillCenter = false;
            return img;
        }

        // Starts generation once (worker thread).
        public static void Begin()
        {
            if (job != null || Application.isBatchMode)
                return;
            job = Task.Run(() => Generate());
        }

        // Main thread: the finished textures, or null while still generating.
        public static Textures TryGet()
        {
            if (ready != null)
                return ready;
            if (job == null || !job.IsCompleted)
                return null;
            if (job.IsFaulted)
            {
                Debug.LogWarning("[MenuArt] backdrop generation failed: " + job.Exception?.GetBaseException().Message);
                job = null;
                return null;
            }

            Generated g = job.Result;
            ready = new Textures
            {
                Sky = Make(g.Sky, "MenuSky", TextureWrapMode.Clamp),
                Clouds = Make(g.Clouds, "MenuClouds", TextureWrapMode.Repeat),
                House = Make(g.House, "MenuHouse", TextureWrapMode.Clamp),
                Fog = Make(g.Fog, "MenuFog", TextureWrapMode.Repeat),
                Rain = Make(g.Rain, "MenuRain", TextureWrapMode.Repeat),
                Vignette = Make(g.Vignette, "MenuVignette", TextureWrapMode.Clamp),
                Figure = Make(g.Figure, "MenuFigure", TextureWrapMode.Clamp),
                Shade = Make(g.Shade, "MenuShade", TextureWrapMode.Clamp),
                Layout = g
            };
            return ready;
        }

        public static bool Failed => job == null && ready == null;

        private static Texture2D Make(Pixels p, string name, TextureWrapMode wrap)
        {
            var t = new Texture2D(p.W, p.H, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = wrap,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 2,
                hideFlags = HideFlags.DontSave
            };
            t.SetPixels32(p.Data);
            t.Apply(true, true);
            return t;
        }

        // ------------------------------------------------------------ generation

        public static Generated Generate()
        {
            var g = new Generated { Moon = new Vector2(0.535f, 0.845f) };
            g.Sky = Sky(SceneW, SceneH, g.Moon);
            g.Clouds = Clouds(1024, 384);
            g.House = House(SceneW, SceneH, g);
            g.Fog = Fog(1024, 256);
            g.Rain = Rain(256, 256);
            g.Vignette = Vignette(256, 256);
            g.Figure = Figure(64, 128);
            g.Shade = Shade(256, 4);
            return g;
        }

        private static Pixels Sky(int w, int h, Vector2 moon)
        {
            var p = new Pixels { W = w, H = h, Data = new Color32[w * h] };
            float mx = moon.x * w, my = moon.y * h, mr = 0.048f * h;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (float)x / w, v = (float)y / h;
                float k = Smooth(0.2f, 1f, v);
                float r = Lerp(0.075f, 0.012f, k), gch = Lerp(0.082f, 0.015f, k), b = Lerp(0.1f, 0.028f, k);

                float cloud = Fbm(u * 5f, v * 9f, 5, 11, 5);
                float band = Smooth(0.5f, 0.78f, cloud) * Smooth(0.35f, 0.7f, v);
                float dx = x - mx, dy = y - my;
                float d = (float)Math.Sqrt(dx * dx + dy * dy);
                float halo = (float)Math.Exp(-d / (0.16f * h)) * 0.22f + (float)Math.Exp(-d / (0.045f * h)) * 0.3f;
                float lit = 0.35f + halo * 2.2f;
                r += band * 0.05f * lit; gch += band * 0.055f * lit; b += band * 0.065f * lit;
                r += halo * 0.55f; gch += halo * 0.6f; b += halo * 0.7f;

                if (Hash(x, y, 91) > 0.9993f && band < 0.2f && v > 0.45f)
                {
                    float s = 0.25f + Hash(x, y, 93) * 0.35f;
                    r += s; gch += s; b += s * 1.1f;
                }

                float disc = Clamp01(mr - d + 0.5f);
                if (disc > 0f)
                {
                    float crater = Fbm(dx / mr * 1.6f + 3f, dy / mr * 1.6f + 7f, 4, 31, 0);
                    float shade = 0.82f + crater * 0.22f - Clamp01(d / mr) * 0.12f;
                    r = Lerp(r, 0.68f * shade, disc);
                    gch = Lerp(gch, 0.7f * shade, disc);
                    b = Lerp(b, 0.68f * shade, disc);
                }
                p.Data[y * w + x] = Col(r, gch, b, 1f);
            }
            return p;
        }

        private static Pixels Clouds(int w, int h)
        {
            var p = new Pixels { W = w, H = h, Data = new Color32[w * h] };
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (float)x / w, v = (float)y / h;
                float n = Fbm(u * 4f, v * 5f, 5, 41, 4);
                float a = Smooth(0.5f, 0.82f, n) * Smooth(0f, 0.25f, v) * Smooth(1f, 0.7f, v);
                p.Data[y * w + x] = Col(0.16f, 0.17f, 0.21f, a * 0.85f);
            }
            return p;
        }

        // The house on its hill, bare trees, a fence and gate. Shapes are
        // drawn with analytic anti-aliasing (signed distances).
        private static Pixels House(int w, int h, Generated g)
        {
            var alpha = new float[w * h];
            var lit = new float[w * h];
            var rng = new System.Random(7);

            // Hill
            for (int x = 0; x < w; x++)
            {
                float u = (float)x / w;
                float hill = (0.29f + 0.035f * (float)Math.Sin(u * 3.1f + 1.3f) + (Fbm(u * 6f, 0.5f, 3, 17, 0) - 0.5f) * 0.03f) * h;
                for (int y = 0; y < h && y <= hill + 1; y++)
                    alpha[y * w + x] = Math.Max(alpha[y * w + x], Clamp01(hill - y + 0.5f));
            }

            // The house (pixel coordinates; y up), set a little left of centre
            // so its right wing clears the sensor panel's column.
            const float shift = -0.03f;
            float U(float u) => (u + shift) * w;
            float V(float v) => v * h;
            Box(alpha, w, h, U(0.405f), V(0.28f), U(0.565f), V(0.5f));            // main block
            Tri(alpha, w, h, U(0.4f), V(0.495f), U(0.57f), V(0.495f), U(0.485f), V(0.6f));  // main gable
            Box(alpha, w, h, U(0.555f), V(0.28f), U(0.625f), V(0.445f));          // right wing
            Tri(alpha, w, h, U(0.55f), V(0.44f), U(0.63f), V(0.44f), U(0.59f), V(0.51f));   // wing gable
            Box(alpha, w, h, U(0.372f), V(0.28f), U(0.412f), V(0.565f));          // tower
            Tri(alpha, w, h, U(0.366f), V(0.56f), U(0.418f), V(0.56f), U(0.392f), V(0.7f)); // spire
            Capsule(alpha, w, h, U(0.392f), V(0.698f), U(0.392f), V(0.725f), 1.2f); // finial
            Box(alpha, w, h, U(0.445f), V(0.55f), U(0.457f), V(0.625f));          // chimneys
            Box(alpha, w, h, U(0.528f), V(0.53f), U(0.539f), V(0.6f));
            Box(alpha, w, h, U(0.6f), V(0.47f), U(0.608f), V(0.53f));
            Tri(alpha, w, h, U(0.452f), V(0.335f), U(0.518f), V(0.335f), U(0.485f), V(0.37f)); // porch roof
            for (float px = 0.456f; px <= 0.515f; px += 0.0295f)
                Box(alpha, w, h, U(px), V(0.28f), U(px + 0.004f), V(0.336f));     // porch posts

            // Lit windows: (u0, v0, u1, v1). One upper window is the figure's.
            float[][] windows =
            {
                new[] { 0.418f, 0.43f, 0.434f, 0.47f }, new[] { 0.448f, 0.43f, 0.464f, 0.47f },
                new[] { 0.505f, 0.43f, 0.521f, 0.47f }, new[] { 0.535f, 0.43f, 0.551f, 0.47f },
                new[] { 0.477f, 0.52f, 0.493f, 0.565f },
                new[] { 0.418f, 0.36f, 0.434f, 0.4f }, new[] { 0.535f, 0.36f, 0.551f, 0.4f },
                new[] { 0.384f, 0.48f, 0.4f, 0.52f },
                new[] { 0.573f, 0.375f, 0.589f, 0.41f }, new[] { 0.597f, 0.375f, 0.613f, 0.41f },
                new[] { 0.475f, 0.287f, 0.495f, 0.325f }
            };
            foreach (float[] r in windows)
            {
                Box(lit, w, h, U(r[0]), V(r[1]), U(r[2]), V(r[3]));
                g.Windows.Add(Rect.MinMaxRect(r[0] + shift, r[1], r[2] + shift, r[3]));
            }
            g.FigureWindow = 4;

            // Mullions darken the panes again.
            foreach (float[] r in windows)
            {
                float cx = (r[0] + r[2]) * 0.5f, cy = (r[1] + r[3]) * 0.55f;
                Carve(lit, w, h, U(cx) - 0.7f, V(r[1]), U(cx) + 0.7f, V(r[3]));
                Carve(lit, w, h, U(r[0]), V(cy) - 0.7f, U(r[2]), V(cy) + 0.7f);
            }

            // Fence and gate.
            float fenceY = 0.205f;
            for (float u = 0.25f; u <= 0.76f; u += 0.0075f)
            {
                if (u > 0.462f && u < 0.508f)
                    continue;
                Capsule(alpha, w, h, U(u), V(0.12f), U(u), V(fenceY), 0.9f);
                Tri(alpha, w, h, U(u) - 2.2f, V(fenceY), U(u) + 2.2f, V(fenceY), U(u), V(fenceY + 0.012f));
            }
            Capsule(alpha, w, h, U(0.25f), V(0.185f), U(0.462f), V(0.185f), 0.8f);
            Capsule(alpha, w, h, U(0.508f), V(0.185f), U(0.76f), V(0.185f), 0.8f);
            Box(alpha, w, h, U(0.455f), V(0.12f), U(0.466f), V(0.235f));
            Box(alpha, w, h, U(0.504f), V(0.12f), U(0.515f), V(0.235f));
            g.Lamps.Add(new Vector2(0.4605f + shift, 0.25f));
            g.Lamps.Add(new Vector2(0.5095f + shift, 0.25f));
            Box(alpha, w, h, U(0.458f), V(0.235f), U(0.463f), V(0.245f));
            Box(alpha, w, h, U(0.507f), V(0.235f), U(0.512f), V(0.245f));

            // Bare trees, left and right.
            Branch(alpha, w, h, rng, U(0.17f), V(0.27f), 90f, 0.2f * h, 9f, 7);
            Branch(alpha, w, h, rng, U(0.06f), V(0.25f), 84f, 0.15f * h, 6.5f, 6);
            Branch(alpha, w, h, rng, U(0.8f), V(0.3f), 95f, 0.17f * h, 7.5f, 7);
            Branch(alpha, w, h, rng, U(0.93f), V(0.27f), 88f, 0.21f * h, 9f, 7);

            var p = new Pixels { W = w, H = h, Data = new Color32[w * h] };
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float a = alpha[i];
                float l = lit[i];
                float grain = 0.9f + Hash(x, y, 5) * 0.2f;
                float r = 0.012f * grain, gc = 0.012f * grain, b = 0.016f * grain;
                if (l > 0f)
                {
                    float glow = 0.55f + Fbm((float)x / 9f, (float)y / 9f, 2, 61, 0) * 0.35f;
                    r = Lerp(r, 0.62f * glow, l);
                    gc = Lerp(gc, 0.36f * glow, l);
                    b = Lerp(b, 0.13f * glow, l);
                    a = Math.Max(a, l);
                }
                p.Data[i] = Col(r, gc, b, a);
            }
            return p;
        }

        private static void Branch(float[] a, int w, int h, System.Random rng, float x, float y, float angle, float len, float thick, int depth)
        {
            float rad = angle * (float)Math.PI / 180f;
            float x2 = x + (float)Math.Cos(rad) * len, y2 = y + (float)Math.Sin(rad) * len;
            Capsule(a, w, h, x, y, x2, y2, Math.Max(0.6f, thick * 0.5f));
            if (depth <= 0)
                return;
            int kids = depth > 4 ? 2 : 2 + rng.Next(2);
            for (int k = 0; k < kids; k++)
            {
                float turn = (k == 0 ? -1f : 1f) * (14f + (float)rng.NextDouble() * 26f) + ((float)rng.NextDouble() - 0.5f) * 12f;
                if (k == 2) turn *= 0.3f;
                Branch(a, w, h, rng, x2, y2, angle + turn, len * (0.66f + (float)rng.NextDouble() * 0.14f), thick * 0.64f, depth - 1);
            }
        }

        private static Pixels Fog(int w, int h)
        {
            var p = new Pixels { W = w, H = h, Data = new Color32[w * h] };
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (float)x / w, v = (float)y / h;
                float n = Fbm(u * 5f, v * 3f, 5, 71, 5);
                float a = Smooth(0.38f, 0.78f, n) * Smooth(0f, 0.3f, v) * Smooth(1f, 0.45f, v);
                p.Data[y * w + x] = Col(0.42f, 0.45f, 0.5f, a * 0.55f);
            }
            return p;
        }

        private static Pixels Rain(int w, int h)
        {
            var alpha = new float[w * h];
            var rng = new System.Random(13);
            for (int k = 0; k < 120; k++)
            {
                float x = (float)rng.NextDouble() * w, y = (float)rng.NextDouble() * h;
                float len = 9f + (float)rng.NextDouble() * 18f;
                float strength = 0.25f + (float)rng.NextDouble() * 0.5f;
                for (int ox = -1; ox <= 1; ox++)
                for (int oy = -1; oy <= 1; oy++)
                    CapsuleScaled(alpha, w, h, x + ox * w, y + oy * h, x + ox * w - len * 0.22f, y + oy * h - len, 0.55f, strength);
            }
            var p = new Pixels { W = w, H = h, Data = new Color32[w * h] };
            for (int i = 0; i < alpha.Length; i++)
                p.Data[i] = Col(0.75f, 0.8f, 0.88f, alpha[i]);
            return p;
        }

        private static Pixels Vignette(int w, int h)
        {
            var p = new Pixels { W = w, H = h, Data = new Color32[w * h] };
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (float)x / w * 2f - 1f, v = (float)y / h * 2f - 1f;
                float d = (float)Math.Sqrt(u * u * 0.75f + v * v);
                p.Data[y * w + x] = Col(0f, 0f, 0f, Smooth(0.55f, 1.25f, d) * 0.9f);
            }
            return p;
        }

        // A head-and-shoulders silhouette, soft-edged, for one lit window.
        private static Pixels Figure(int w, int h)
        {
            var a = new float[w * h];
            Circle(a, w, h, w * 0.5f, h * 0.72f, w * 0.17f);
            Box(a, w, h, w * 0.43f, h * 0.55f, w * 0.57f, h * 0.66f);
            Tri(a, w, h, w * 0.12f, 0f, w * 0.88f, 0f, w * 0.5f, h * 0.66f);
            Box(a, w, h, w * 0.18f, 0f, w * 0.82f, h * 0.5f);
            var p = new Pixels { W = w, H = h, Data = new Color32[w * h] };
            for (int i = 0; i < a.Length; i++)
                p.Data[i] = Col(0.02f, 0.015f, 0.015f, a[i]);
            return p;
        }

        // Horizontal fade: opaque at u = 0, clear at u = 1.
        private static Pixels Shade(int w, int h)
        {
            var p = new Pixels { W = w, H = h, Data = new Color32[w * h] };
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                p.Data[y * w + x] = Col(0f, 0f, 0f, 1f - Smooth(0f, 1f, (float)x / (w - 1)));
            return p;
        }

        // ------------------------------------------------------------ drawing

        private static void Box(float[] a, int w, int h, float x0, float y0, float x1, float y1)
        {
            int ix0 = Math.Max(0, (int)x0 - 1), ix1 = Math.Min(w - 1, (int)x1 + 1);
            int iy0 = Math.Max(0, (int)y0 - 1), iy1 = Math.Min(h - 1, (int)y1 + 1);
            float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f, hx = (x1 - x0) * 0.5f, hy = (y1 - y0) * 0.5f;
            for (int y = iy0; y <= iy1; y++)
            for (int x = ix0; x <= ix1; x++)
            {
                float dx = Math.Abs(x + 0.5f - cx) - hx, dy = Math.Abs(y + 0.5f - cy) - hy;
                float outside = (float)Math.Sqrt(Math.Max(dx, 0f) * Math.Max(dx, 0f) + Math.Max(dy, 0f) * Math.Max(dy, 0f));
                float sd = outside + Math.Min(Math.Max(dx, dy), 0f);
                Cover(a, y * w + x, Clamp01(0.5f - sd));
            }
        }

        private static void Carve(float[] a, int w, int h, float x0, float y0, float x1, float y1)
        {
            int ix0 = Math.Max(0, (int)x0), ix1 = Math.Min(w - 1, (int)x1);
            int iy0 = Math.Max(0, (int)y0), iy1 = Math.Min(h - 1, (int)y1);
            for (int y = iy0; y <= iy1; y++)
            for (int x = ix0; x <= ix1; x++)
                a[y * w + x] *= 0.15f;
        }

        private static void Circle(float[] a, int w, int h, float cx, float cy, float r)
        {
            int ix0 = Math.Max(0, (int)(cx - r) - 1), ix1 = Math.Min(w - 1, (int)(cx + r) + 1);
            int iy0 = Math.Max(0, (int)(cy - r) - 1), iy1 = Math.Min(h - 1, (int)(cy + r) + 1);
            for (int y = iy0; y <= iy1; y++)
            for (int x = ix0; x <= ix1; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                Cover(a, y * w + x, Clamp01(r - (float)Math.Sqrt(dx * dx + dy * dy) + 0.5f));
            }
        }

        private static void Capsule(float[] a, int w, int h, float ax, float ay, float bx, float by, float r)
        {
            CapsuleScaled(a, w, h, ax, ay, bx, by, r, 1f);
        }

        private static void CapsuleScaled(float[] a, int w, int h, float ax, float ay, float bx, float by, float r, float strength)
        {
            int ix0 = Math.Max(0, (int)(Math.Min(ax, bx) - r) - 1), ix1 = Math.Min(w - 1, (int)(Math.Max(ax, bx) + r) + 1);
            int iy0 = Math.Max(0, (int)(Math.Min(ay, by) - r) - 1), iy1 = Math.Min(h - 1, (int)(Math.Max(ay, by) + r) + 1);
            float vx = bx - ax, vy = by - ay;
            float len2 = Math.Max(1e-4f, vx * vx + vy * vy);
            for (int y = iy0; y <= iy1; y++)
            for (int x = ix0; x <= ix1; x++)
            {
                float px = x + 0.5f - ax, py = y + 0.5f - ay;
                float t = Clamp01((px * vx + py * vy) / len2);
                float dx = px - vx * t, dy = py - vy * t;
                Cover(a, y * w + x, Clamp01(r - (float)Math.Sqrt(dx * dx + dy * dy) + 0.5f) * strength);
            }
        }

        private static void Tri(float[] a, int w, int h, float x0, float y0, float x1, float y1, float x2, float y2)
        {
            int ix0 = Math.Max(0, (int)Math.Min(x0, Math.Min(x1, x2)) - 1), ix1 = Math.Min(w - 1, (int)Math.Max(x0, Math.Max(x1, x2)) + 1);
            int iy0 = Math.Max(0, (int)Math.Min(y0, Math.Min(y1, y2)) - 1), iy1 = Math.Min(h - 1, (int)Math.Max(y0, Math.Max(y1, y2)) + 1);
            for (int y = iy0; y <= iy1; y++)
            for (int x = ix0; x <= ix1; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float d = Math.Min(SegDist(px, py, x0, y0, x1, y1), Math.Min(SegDist(px, py, x1, y1, x2, y2), SegDist(px, py, x2, y2, x0, y0)));
                bool inside = Side(px, py, x0, y0, x1, y1) == Side(px, py, x1, y1, x2, y2) && Side(px, py, x1, y1, x2, y2) == Side(px, py, x2, y2, x0, y0);
                float sd = inside ? -d : d;
                Cover(a, y * w + x, Clamp01(0.5f - sd));
            }
        }

        private static bool Side(float px, float py, float ax, float ay, float bx, float by) => (bx - ax) * (py - ay) - (by - ay) * (px - ax) >= 0f;

        private static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay;
            float t = Clamp01(((px - ax) * vx + (py - ay) * vy) / Math.Max(1e-4f, vx * vx + vy * vy));
            float dx = px - ax - vx * t, dy = py - ay - vy * t;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static void Cover(float[] a, int i, float c)
        {
            if (c > a[i])
                a[i] = c;
        }

        // ------------------------------------------------------------ noise

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        // Value noise; tileable in x when periodX > 0.
        private static float Noise(float x, float y, int seed, int periodX)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            float fx = x - xi, fy = y - yi;
            int x0 = xi, x1 = xi + 1;
            if (periodX > 0)
            {
                x0 = ((x0 % periodX) + periodX) % periodX;
                x1 = ((x1 % periodX) + periodX) % periodX;
            }
            float a = Hash(x0, yi, seed), b = Hash(x1, yi, seed), c = Hash(x0, yi + 1, seed), d = Hash(x1, yi + 1, seed);
            float sx = fx * fx * (3f - 2f * fx), sy = fy * fy * (3f - 2f * fy);
            return Lerp(Lerp(a, b, sx), Lerp(c, d, sx), sy);
        }

        // Fractal noise; tileable in x over [0, periodX) of the input when periodX > 0.
        private static float Fbm(float x, float y, int octaves, int seed, int periodX)
        {
            float sum = 0f, amp = 0.5f, norm = 0f, f = 1f;
            for (int o = 0; o < octaves; o++)
            {
                sum += Noise(x * f, y * f, seed + o * 17, periodX > 0 ? (int)(periodX * f) : 0) * amp;
                norm += amp;
                amp *= 0.5f;
                f *= 2f;
            }
            return sum / norm;
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        private static float Smooth(float e0, float e1, float x)
        {
            float t = Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        private static Color32 Col(float r, float g, float b, float a)
        {
            return new Color32((byte)(Clamp01(r) * 255f), (byte)(Clamp01(g) * 255f), (byte)(Clamp01(b) * 255f), (byte)(Clamp01(a) * 255f));
        }
    }
}
