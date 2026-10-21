using System.Collections.Generic;
using UnityEngine;

namespace PHYSXR.Game.Art
{
    // Original, code-generated surface textures for Blackthorn House. No
    // third-party art is needed, so everything shipped is licence-clean and
    // reproducible from source.
    //
    // Each texture is generated once per process (value noise, a few
    // octaves) and cached; scene reloads reuse the cache. Albedo textures are
    // sRGB; normal maps are linear RGB with alpha 1, which URP's
    // UnpackNormalRGorAG decodes correctly. Textures are made non-readable
    // after upload so their CPU copy is released.
    public static class ProceduralTextures
    {
        public enum Id
        {
            FloorBoards,
            FloorBoardsNormal,
            Wallpaper,
            WallpaperNormal,
            Wainscot,
            WainscotNormal,
            Plaster,
            Tiles,
            TilesNormal,
            Rug,
            Runner,
            DarkWood,
            Metal,
            Paper,
            Siding,
            Roof,
            Ground,
            SoftBlob,
            RainStreak,
            Portrait,
            Landscape,
            Fabric,
            MenuBackdrop
        }

        private static readonly Dictionary<Id, Texture2D> Cache = new Dictionary<Id, Texture2D>();

        public static Texture2D Get(Id id)
        {
            if (Cache.TryGetValue(id, out Texture2D cached) && cached != null)
                return cached;

            Texture2D texture = Build(id);
            texture.name = "PHYSXR_" + id;
            texture.hideFlags = HideFlags.DontSave;
            Cache[id] = texture;
            return texture;
        }

        // Generates every texture now (called while the main menu is up).
        public static void WarmAll()
        {
            foreach (Id id in System.Enum.GetValues(typeof(Id)))
                Get(id);
        }

        private static Texture2D Build(Id id)
        {
            switch (id)
            {
                case Id.FloorBoards: return Boards(512, new Color(0.30f, 0.20f, 0.13f), 8, 0.22f, false, 11);
                case Id.FloorBoardsNormal: return NormalFrom(BoardsHeight(512, 8, 11), 512, 2.2f);
                case Id.Wallpaper: return Wallpaper(512);
                case Id.WallpaperNormal: return NormalFrom(WallpaperHeight(512), 512, 0.8f);
                case Id.Wainscot: return Boards(256, new Color(0.22f, 0.14f, 0.09f), 6, 0.15f, true, 23);
                case Id.WainscotNormal: return NormalFrom(PanelHeight(256), 256, 2.5f);
                case Id.Plaster: return Plaster(256);
                case Id.Tiles: return Tiles(512, false);
                case Id.TilesNormal: return NormalFrom(TilesHeight(512), 512, 3f);
                case Id.Rug: return Rug(256, new Color(0.32f, 0.07f, 0.06f), new Color(0.55f, 0.42f, 0.22f), 31);
                case Id.Runner: return Rug(256, new Color(0.12f, 0.15f, 0.22f), new Color(0.45f, 0.38f, 0.25f), 37);
                case Id.DarkWood: return Grain(256, new Color(0.18f, 0.11f, 0.07f), 41);
                case Id.Metal: return Metal(256);
                case Id.Paper: return Paper(256);
                case Id.Siding: return Siding(256);
                case Id.Roof: return Roof(256);
                case Id.Ground: return Ground(256);
                case Id.SoftBlob: return SoftBlob(64);
                case Id.RainStreak: return RainStreak(32, 128);
                case Id.Portrait: return Portrait(128, 53);
                case Id.Landscape: return Landscape(128, 59);
                case Id.Fabric: return Fabric(128);
                case Id.MenuBackdrop: return Landscape(512, 61);
                default: return Solid(Color.magenta);
            }
        }

        // ------------------------------------------------------------- noise

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        // Tileable value noise: lattice wraps every `period` cells.
        private static float Value(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = x - x0;
            float fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int xa = Wrap(x0, period), xb = Wrap(x0 + 1, period);
            int ya = Wrap(y0, period), yb = Wrap(y0 + 1, period);
            float a = Hash(xa, ya, seed), b = Hash(xb, ya, seed);
            float c = Hash(xa, yb, seed), d = Hash(xb, yb, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static int Wrap(int v, int period)
        {
            int m = v % period;
            return m < 0 ? m + period : m;
        }

        // Fractal noise in [0,1], tileable over a unit square (u, v in 0..1).
        private static float Fbm(float u, float v, int baseCells, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int cells = baseCells;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Value(u * cells, v * cells, cells, seed + o * 101);
                norm += amp;
                amp *= 0.5f;
                cells *= 2;
            }
            return sum / norm;
        }

        // ---------------------------------------------------------- builders

        private static Texture2D NewTexture(int w, int h, bool linear)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, linear)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            return t;
        }

        private static Texture2D Finish(Texture2D t, Color32[] pixels)
        {
            t.SetPixels32(pixels);
            t.Apply(true, true);
            return t;
        }

        private static Color32 C(Color c)
        {
            return (Color32)new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), Mathf.Clamp01(c.a));
        }

        private static Texture2D Solid(Color c)
        {
            var t = NewTexture(4, 4, false);
            var p = new Color32[16];
            for (int i = 0; i < p.Length; i++) p[i] = C(c);
            return Finish(t, p);
        }

        private static float[] BoardsHeight(int size, int boards, int seed)
        {
            var h = new float[size * size];
            float boardW = 1f / boards;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                int board = Mathf.FloorToInt(u / boardW);
                float local = u / boardW - board;
                float offset = Hash(board, 0, seed) * 0.8f;
                float joint = Mathf.Repeat(v + offset, 0.5f);
                float edge = Mathf.Min(Mathf.Min(local, 1f - local) * boardW * size, Mathf.Min(joint, 0.5f - joint) * size);
                h[y * size + x] = Mathf.Clamp01(edge / 2.5f) * 0.8f + Fbm(u * 0.5f, v * 4f, 4, 3, seed) * 0.2f;
            }
            return h;
        }

        private static Texture2D Boards(int size, Color baseColor, int boards, float variation, bool vertical, int seed)
        {
            var t = NewTexture(size, size, false);
            var p = new Color32[size * size];
            float[] height = BoardsHeight(size, boards, seed);
            float boardW = 1f / boards;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                int board = Mathf.FloorToInt(u / boardW);
                float tint = (Hash(board, 7, seed) - 0.5f) * variation;
                float grain = Fbm(u * 0.25f + board * 0.37f, v * 6f, 2, 4, seed + board);
                float streak = Mathf.Sin((u * boards * 9f + grain * 6f) * Mathf.PI) * 0.04f;
                float gap = 1f - (1f - height[y * size + x]) * 0.9f;
                float grime = Fbm(u, v, 3, 3, seed + 99) * 0.25f;
                Color c = baseColor * (1f + tint + streak + (grain - 0.5f) * 0.35f - grime) * gap;
                c.a = 1f;
                p[y * size + x] = C(c);
            }
            return Finish(t, p);
        }

        private static float[] WallpaperHeight(int size)
        {
            var h = new float[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                h[y * size + x] = Damask(u, v) * 0.6f + Fbm(u, v, 16, 2, 5) * 0.4f;
            }
            return h;
        }

        // A repeating, slightly ornate motif: diamond lattice with lobes.
        private static float Damask(float u, float v)
        {
            float cu = Mathf.Repeat(u * 4f, 1f) - 0.5f;
            float cv = Mathf.Repeat(v * 3f, 1f) - 0.5f;
            float diamond = Mathf.Abs(cu) + Mathf.Abs(cv) * 0.8f;
            float lobe = Mathf.Cos(Mathf.Atan2(cv, cu) * 4f) * 0.08f;
            float motif = Mathf.SmoothStep(0.36f, 0.30f, diamond + lobe) - Mathf.SmoothStep(0.22f, 0.16f, diamond - lobe * 0.5f) * 0.6f;
            float stripe = Mathf.SmoothStep(0.47f, 0.5f, Mathf.Abs(Mathf.Repeat(u * 4f, 1f) - 0.5f) + 0.0f) * 0.5f;
            return Mathf.Clamp01(motif + stripe);
        }

        private static Texture2D Wallpaper(int size)
        {
            var t = NewTexture(size, size, false);
            var p = new Color32[size * size];
            Color baseCol = new Color(0.30f, 0.33f, 0.27f);
            Color motifCol = new Color(0.40f, 0.38f, 0.28f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float m = Damask(u, v);
                float stains = Mathf.SmoothStep(0.55f, 0.75f, Fbm(u, v, 3, 4, 77));
                float fade = Fbm(u, v, 6, 3, 13);
                float drip = Mathf.SmoothStep(0.8f, 1f, Fbm(u * 1f, v * 0.15f, 8, 2, 91)) * (1f - v);
                Color c = Color.Lerp(baseCol, motifCol, m * (0.6f + fade * 0.4f));
                c *= 0.85f + fade * 0.3f;
                c = Color.Lerp(c, new Color(0.22f, 0.18f, 0.10f), stains * 0.55f + drip * 0.4f);
                c.a = 1f;
                p[y * size + x] = C(c);
            }
            return Finish(t, p);
        }

        private static float[] PanelHeight(int size)
        {
            var h = new float[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float pu = Mathf.Repeat(u * 2f, 1f), pv = v;
                float frame = Mathf.Min(Mathf.Min(pu, 1f - pu), Mathf.Min(pv, 1f - pv));
                float bevel = Mathf.Clamp01((frame - 0.08f) / 0.05f);
                h[y * size + x] = 0.4f + bevel * 0.6f - (frame < 0.08f ? 0f : 0.15f);
            }
            return h;
        }

        private static Texture2D Plaster(int size)
        {
            var t = NewTexture(size, size, false);
            var p = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float n = Fbm(u, v, 8, 4, 3);
                float stain = Mathf.SmoothStep(0.6f, 0.8f, Fbm(u, v, 2, 4, 17));
                float crack = Mathf.SmoothStep(0.02f, 0f, Mathf.Abs(Fbm(u, v, 4, 3, 29) - 0.5f)) * 0.5f;
                Color c = new Color(0.42f, 0.40f, 0.36f) * (0.85f + n * 0.25f);
                c = Color.Lerp(c, new Color(0.25f, 0.21f, 0.14f), stain * 0.6f);
                c *= 1f - crack;
                c.a = 1f;
                p[y * size + x] = C(c);
            }
            return Finish(t, p);
        }

        private static float[] TilesHeight(int size)
        {
            var h = new float[size * size];
            const int tiles = 8;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size * tiles, v = (float)y / size * tiles;
                float fu = u - Mathf.Floor(u), fv = v - Mathf.Floor(v);
                float edge = Mathf.Min(Mathf.Min(fu, 1f - fu), Mathf.Min(fv, 1f - fv));
                h[y * size + x] = Mathf.Clamp01((edge - 0.03f) / 0.04f);
            }
            return h;
        }

        private static Texture2D Tiles(int size, bool unused)
        {
            var t = NewTexture(size, size, false);
            var p = new Color32[size * size];
            float[] h = TilesHeight(size);
            const int tiles = 8;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                int tx = Mathf.FloorToInt(u * tiles), ty = Mathf.FloorToInt(v * tiles);
                bool dark = ((tx + ty) & 1) == 0;
                Color tile = dark ? new Color(0.16f, 0.17f, 0.18f) : new Color(0.55f, 0.53f, 0.48f);
                tile *= 0.92f + Hash(tx, ty, 61) * 0.12f;
                float grime = Fbm(u, v, 4, 4, 67);
                tile = Color.Lerp(tile, new Color(0.2f, 0.17f, 0.12f), Mathf.SmoothStep(0.5f, 0.85f, grime) * 0.5f);
                Color grout = new Color(0.12f, 0.11f, 0.09f);
                Color c = Color.Lerp(grout, tile, h[y * size + x]);
                c.a = 1f;
                p[y * size + x] = C(c);
            }
            return Finish(t, p);
        }

        private static Texture2D Rug(int size, Color field, Color border, int seed)
        {
            var t = NewTexture(size, size, false);
            var p = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                float inner = Mathf.Abs(u - 0.5f) + Mathf.Abs(v - 0.5f);
                float medallion = Mathf.SmoothStep(0.27f, 0.25f, inner) - Mathf.SmoothStep(0.18f, 0.16f, inner) * 0.7f;
                float bands = edge < 0.12f ? (edge < 0.04f || (edge > 0.07f && edge < 0.09f) ? 1f : 0.3f) : 0f;
                float wear = Fbm(u, v, 4, 4, seed);
                Color c = Color.Lerp(field, border, Mathf.Clamp01(bands + medallion));
                c *= 0.7f + wear * 0.45f;
                c = Color.Lerp(c, new Color(0.15f, 0.12f, 0.1f), Mathf.SmoothStep(0.6f, 0.9f, wear) * 0.6f);
                float weave = (Mathf.Sin(x * 1.6f) * Mathf.Sin(y * 1.6f)) * 0.04f;
                c *= 1f + weave;
                c.a = 1f;
                p[y * size + x] = C(c);
            }
            return Finish(t, p);
        }

        private static Texture2D Grain(int size, Color baseColor, int seed)
        {
            var t = NewTexture(size, size, false);
            var p = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float g = Fbm(u * 0.5f, v * 8f, 2, 4, seed);
                float rings = Mathf.Sin((u * 14f + g * 5f) * Mathf.PI) * 0.06f;
                Color c = baseColor * (0.8f + g * 0.4f + rings);
                c.a = 1f;
                p[y * size + x] = C(c);
            }
            return Finish(t, p);
        }

        private static Texture2D Metal(int size)
        {
            var t = NewTexture(size, size, false);
            var p = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float brushed = Fbm(u * 8f, v * 0.25f, 4, 3, 71);
                float rust = Mathf.SmoothStep(0.55f, 0.8f, Fbm(u, v, 3, 5, 73));
                Color c = new Color(0.34f, 0.35f, 0.36f) * (0.85f + brushed * 0.3f);
                c = Color.Lerp(c, new Color(0.30f, 0.16f, 0.08f), rust * 0.75f);
                c.a = 1f;
                p[y * size + x] = C(c);
            }
            return Finish(t, p);
        }

        private static Texture2D Paper(int size)
        {
            var t = NewTexture(size, size, false);
            var p = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float n = Fbm(u, v, 8, 3, 83);
                float line = (Mathf.Repeat(v * 18f, 1f) < 0.04f && v > 0.12f && v < 0.92f && u > 0.1f && u < 0.9f) ? 0.12f : 0f;
                float ink = 0f;
                if (u > 0.12f && u < 0.86f && v > 0.14f && v < 0.88f)
                {
                    float row = Mathf.Repeat(v * 18f, 1f);
                    float scrib = Fbm(u * 6f, v * 18f, 8, 2, 89);
                    if (row > 0.35f && row < 0.7f && scrib > 0.55f)
                        ink = 0.5f;
                }
                Color c = new Color(0.78f, 0.72f, 0.58f) * (0.9f + n * 0.15f);
                c = Color.Lerp(c, new Color(0.35f, 0.3f, 0.45f), line);
                c = Color.Lerp(c, new Color(0.12f, 0.1f, 0.12f), ink);
                c.a = 1f;
                p[y * size + x] = C(c);
            }
            return Finish(t, p);
        }

        private static Texture2D Siding(int size)
        {
            var t = NewTexture(size, size, false);
            var p = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float course = Mathf.Repeat(v * 10f, 1f);
                float shade = Mathf.Lerp(0.65f, 1f, course);
                float n = Fbm(u * 0.5f, v * 10f, 4, 3, 97);
                Color c = new Color(0.26f, 0.27f, 0.27f) * shade * (0.8f + n * 0.35f);
                c.a = 1f;
                p[y * size + x] = C(c);
            }
            return Finish(t, p);
        }

        private static Texture2D Roof(int size)
        {
            var t = NewTexture(size, size, false);
            var p = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                int row = Mathf.FloorToInt(v * 12f);
                float su = u * 10f + (row & 1) * 0.5f;
                float local = su - Mathf.Floor(su);
                float edge = Mathf.Min(local, 1f - local) < 0.05f ? 0.6f : 1f;
                float rv = Mathf.Repeat(v * 12f, 1f);
                Color c = new Color(0.12f, 0.12f, 0.13f) * edge * Mathf.Lerp(0.7f, 1.1f, rv) * (0.85f + Hash(Mathf.FloorToInt(su), row, 101) * 0.3f);
                c.a = 1f;
                p[y * size + x] = C(c);
            }
            return Finish(t, p);
        }

        private static Texture2D Ground(int size)
        {
            var t = NewTexture(size, size, false);
            var p = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float n = Fbm(u, v, 6, 5, 103);
                float puddle = Mathf.SmoothStep(0.62f, 0.66f, Fbm(u, v, 3, 3, 107));
                Color c = Color.Lerp(new Color(0.09f, 0.08f, 0.06f), new Color(0.14f, 0.15f, 0.09f), n);
                c = Color.Lerp(c, new Color(0.05f, 0.05f, 0.06f), puddle);
                c.a = 1f;
                p[y * size + x] = C(c);
            }
            return Finish(t, p);
        }

        private static Texture2D SoftBlob(int size)
        {
            var t = NewTexture(size, size, false);
            t.wrapMode = TextureWrapMode.Clamp;
            var p = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size - 0.5f, dy = (y + 0.5f) / size - 0.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                float n = Fbm((float)x / size, (float)y / size, 4, 3, 109);
                float a = Mathf.Clamp01(1f - r) * (0.6f + n * 0.4f);
                a = a * a;
                p[y * size + x] = C(new Color(1f, 1f, 1f, a));
            }
            return Finish(t, p);
        }

        private static Texture2D RainStreak(int w, int h)
        {
            var t = NewTexture(w, h, false);
            t.wrapMode = TextureWrapMode.Clamp;
            var p = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = Mathf.Abs((x + 0.5f) / w - 0.5f) * 2f;
                float dy = (float)y / h;
                float a = Mathf.Clamp01(1f - dx * 3f) * Mathf.Sin(dy * Mathf.PI) * 0.8f;
                p[y * w + x] = C(new Color(0.8f, 0.85f, 0.9f, a));
            }
            return Finish(t, p);
        }

        private static Texture2D Portrait(int size, int seed)
        {
            var t = NewTexture(size, size, false);
            var p = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float n = Fbm(u, v, 4, 4, seed);
                float head = new Vector2((u - 0.5f) / 0.16f, (v - 0.64f) / 0.21f).sqrMagnitude;
                float shoulders = new Vector2((u - 0.5f) / 0.36f, (v - 0.12f) / 0.32f).sqrMagnitude;
                float figure = Mathf.Max(Mathf.SmoothStep(1.1f, 0.9f, head), Mathf.SmoothStep(1.1f, 0.9f, shoulders) * (v < 0.45f ? 1f : 0f));
                float eyes = (Mathf.Abs(v - 0.67f) < 0.02f && (Mathf.Abs(u - 0.44f) < 0.025f || Mathf.Abs(u - 0.56f) < 0.025f)) ? 1f : 0f;
                Color bg = Color.Lerp(new Color(0.08f, 0.06f, 0.05f), new Color(0.22f, 0.15f, 0.08f), n * (1f - v * 0.5f));
                Color fig = new Color(0.05f, 0.04f, 0.035f) + new Color(0.15f, 0.12f, 0.09f) * Mathf.SmoothStep(1f, 0.2f, head);
                Color c = Color.Lerp(bg, fig, figure);
                c = Color.Lerp(c, new Color(0.02f, 0.02f, 0.02f), eyes * 0.9f);
                c *= 0.85f + Fbm(u * 4f, v * 4f, 8, 2, seed + 1) * 0.2f;
                c.a = 1f;
                p[y * size + x] = C(c);
            }
            return Finish(t, p);
        }

        private static Texture2D Landscape(int size, int seed)
        {
            var t = NewTexture(size, size, false);
            var p = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float hill = 0.35f + Fbm(u, 0.3f, 3, 3, seed) * 0.2f;
                float sea = 0.3f;
                Color sky = Color.Lerp(new Color(0.25f, 0.22f, 0.16f), new Color(0.08f, 0.08f, 0.09f), v);
                Color c = v < sea ? new Color(0.06f, 0.08f, 0.09f) * (0.8f + Fbm(u * 8f, v, 4, 2, seed + 3) * 0.4f)
                    : v < hill ? new Color(0.07f, 0.06f, 0.04f) : sky;
                bool house = u > 0.62f && u < 0.7f && v > hill - 0.02f && v < hill + 0.09f;
                if (house) c = new Color(0.03f, 0.03f, 0.03f);
                c *= 0.9f + Fbm(u * 3f, v * 3f, 8, 2, seed + 5) * 0.2f;
                c.a = 1f;
                p[y * size + x] = C(c);
            }
            return Finish(t, p);
        }

        private static Texture2D Fabric(int size)
        {
            var t = NewTexture(size, size, false);
            var p = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                float weave = ((x + y) % 4 < 2 ? 0.05f : -0.05f);
                float n = Fbm(u, v, 4, 3, 113);
                Color c = new Color(0.2f, 0.19f, 0.17f) * (0.85f + n * 0.3f + weave);
                c.a = 1f;
                p[y * size + x] = C(c);
            }
            return Finish(t, p);
        }

        // Linear normal map from a height field (Sobel), alpha 1.
        private static Texture2D NormalFrom(float[] height, int size, float strength)
        {
            var t = NewTexture(size, size, true);
            var p = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float l = height[y * size + (x - 1 + size) % size];
                float r = height[y * size + (x + 1) % size];
                float d = height[((y - 1 + size) % size) * size + x];
                float u = height[((y + 1) % size) * size + x];
                var n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                p[y * size + x] = new Color32((byte)((n.x * 0.5f + 0.5f) * 255f), (byte)((n.y * 0.5f + 0.5f) * 255f), (byte)((n.z * 0.5f + 0.5f) * 255f), 255);
            }
            return Finish(t, p);
        }
    }
}
