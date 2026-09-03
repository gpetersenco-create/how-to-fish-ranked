using System.Collections.Generic;
using HowToFish1v1.Core;
using UnityEngine;

namespace HowToFish1v1.Arena
{
    /// <summary>
    /// Materials for the arena boxes. Each kind gets a generated texture (concrete, rust, brushed steel, planks, bricks,
    /// painted metal) plus a matching bump map, on the render pipeline's lit shader. Boxes carry world-scaled UVs
    /// (see <see cref="WorldUvBox"/>), so a 2 m tile always covers 2 m whatever the box size.
    /// </summary>
    internal static class ArenaMaterials
    {
        private static readonly Dictionary<string, Material> _cache = new Dictionary<string, Material>();
        private static Shader _lit;
        private const int N = 512;

        /// <summary>The render pipeline's lit shader (with working emission), for anything the mod draws itself.</summary>
        public static Shader LitShader => _lit ? _lit : (_lit = FindShader());

        public static Material For(BoxKind kind) => Get(kind.ToString(), kind, floor: false);

        /// <summary>The ground: asphalt, tiled coarser than the walls.</summary>
        public static Material Floor() => Get("Floor", BoxKind.Concrete, floor: true);

        private static Material Get(string key, BoxKind kind, bool floor)
        {
            if (_cache.TryGetValue(key, out var m) && m) return m;
            var shader = LitShader;
            m = shader ? new Material(shader) : new Material(Shader.Find("Sprites/Default"));
            m.name = "HTF1v1_" + key;
            Texture2D albedo, bump;
            float metal, gloss, bumpScale;
            Color tint = Color.white;
            if (floor) { (albedo, bump) = Tex.Asphalt(); metal = 0.05f; gloss = 0.25f; bumpScale = 0.8f; }
            else switch (kind)
            {
                case BoxKind.Rust: (albedo, bump) = Tex.Rust(); metal = 0.35f; gloss = 0.35f; bumpScale = 1f; break;
                case BoxKind.Steel: (albedo, bump) = Tex.Steel(); metal = 0.85f; gloss = 0.6f; bumpScale = 1f; break;
                case BoxKind.Wood: (albedo, bump) = Tex.Wood(); metal = 0.0f; gloss = 0.3f; bumpScale = 0.9f; break;
                case BoxKind.Brick: (albedo, bump) = Tex.Brick(); metal = 0.0f; gloss = 0.2f; bumpScale = 1.2f; break;
                case BoxKind.Yellow: (albedo, bump) = Tex.Painted(new Color(0.95f, 0.72f, 0.12f)); metal = 0.5f; gloss = 0.55f; bumpScale = 0.7f; break;
                case BoxKind.Red: (albedo, bump) = Tex.Painted(new Color(0.72f, 0.14f, 0.12f)); metal = 0.5f; gloss = 0.55f; bumpScale = 0.7f; break;
                case BoxKind.Blue: (albedo, bump) = Tex.Painted(new Color(0.14f, 0.30f, 0.66f)); metal = 0.5f; gloss = 0.55f; bumpScale = 0.7f; break;
                case BoxKind.White: (albedo, bump) = Tex.Painted(new Color(0.88f, 0.88f, 0.84f)); metal = 0.3f; gloss = 0.5f; bumpScale = 0.7f; break;
                default: (albedo, bump) = Tex.Concrete(); metal = 0.0f; gloss = 0.28f; bumpScale = 1f; break;
            }
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", albedo);
            else if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", albedo);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
            m.color = tint;
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", gloss);
            if (bump && m.HasProperty("_BumpMap"))
            {
                m.SetTexture("_BumpMap", bump);
                if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", bumpScale);
                m.EnableKeyword("_NORMALMAP");
            }
            _cache[key] = m;
            return m;
        }

        private static Shader FindShader()
        {
            foreach (var name in new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Standard" })
            {
                var s = Shader.Find(name);
                if (s) return s;
            }
            foreach (var s in Resources.FindObjectsOfTypeAll<Shader>())
                if (s.name == "Universal Render Pipeline/Lit") return s;
            Plugin.Log.LogWarning("No URP shader found; arena will use a fallback material");
            return null;
        }

        // ------------------------------------------------------------------ generated surfaces

        /// <summary>Procedural albedo + bump pairs. Every texture tiles; one tile is 2 m in the world.</summary>
        private static class Tex
        {
            private static float Hash(int x, int y, int seed) { unchecked { int h = x * 374761393 + y * 668265263 + seed * 1274126177; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xFFFF) / 65535f; } }

            private static float Noise(float x, float y, int cells, int seed)
            {
                float fx = x * cells / N, fy = y * cells / N;
                int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
                float tx = fx - x0, ty = fy - y0;
                tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
                int cx0 = ((x0 % cells) + cells) % cells, cx1 = (cx0 + 1) % cells, cy0 = ((y0 % cells) + cells) % cells, cy1 = (cy0 + 1) % cells;
                float a = Hash(cx0, cy0, seed), b = Hash(cx1, cy0, seed), c = Hash(cx0, cy1, seed), d = Hash(cx1, cy1, seed);
                return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
            }

            private static float Fbm(float x, float y, int seed, int oct = 4, int baseCells = 4)
            {
                float v = 0f, amp = 0.5f, sum = 0f; int cells = baseCells;
                for (int i = 0; i < oct; i++) { v += Noise(x, y, cells, seed + i) * amp; sum += amp; amp *= 0.5f; cells *= 2; }
                return v / sum;
            }

            private static void Voronoi(int x, int y, int cells, int seed, out float d1, out float d2)
            {
                float cs = (float)N / cells;
                int cx = Mathf.FloorToInt(x / cs), cy = Mathf.FloorToInt(y / cs);
                d1 = d2 = float.MaxValue;
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int gx = cx + ox, gy = cy + oy;
                        int wx = ((gx % cells) + cells) % cells, wy = ((gy % cells) + cells) % cells;
                        float px = (gx + Hash(wx, wy, seed)) * cs, py = (gy + Hash(wx, wy, seed + 7)) * cs;
                        float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                        if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
                    }
            }

            /// <summary>Builds albedo and a bump map from per-pixel colour and height functions.</summary>
            private static (Texture2D, Texture2D) Build(string name, System.Func<int, int, Color> colour, System.Func<int, int, float> height, float bumpStrength)
            {
                var albedo = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "HTF1v1_" + name, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
                var px = new Color[N * N];
                var h = new float[N * N];
                for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) { px[y * N + x] = colour(x, y); h[y * N + x] = height(x, y); }
                albedo.SetPixels(px); albedo.Apply();
                var bump = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { name = "HTF1v1_" + name + "_n", wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
                var np = new Color[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = (h[y * N + (x + 1) % N] - h[y * N + (x + N - 1) % N]) * bumpStrength;
                        float dy = (h[((y + 1) % N) * N + x] - h[((y + N - 1) % N) * N + x]) * bumpStrength;
                        var n = new Vector3(-dx, -dy, 1f).normalized;
                        np[y * N + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                    }
                bump.SetPixels(np); bump.Apply();
                return (albedo, bump);
            }

            public static (Texture2D, Texture2D) Concrete() => Build("Concrete",
                (x, y) =>
                {
                    float g = 0.50f + 0.14f * (Fbm(x, y, 1) - 0.5f) + 0.06f * (Hash(x, y, 2) - 0.5f);
                    float stain = Mathf.Clamp01((Fbm(x, y, 3, 3, 2) - 0.55f) * 2.5f);
                    g -= stain * 0.12f;
                    Voronoi(x, y, 5, 4, out float d1, out float d2);
                    if (d2 - d1 < 1.4f && Fbm(x, y, 5, 2, 3) > 0.45f) g -= 0.18f;   // hairline cracks
                    return new Color(g, g * 0.99f, g * 0.96f);
                },
                (x, y) => Fbm(x, y, 1) * 0.6f + Hash(x, y, 2) * 0.15f, 2.2f);

            public static (Texture2D, Texture2D) Asphalt() => Build("Asphalt",
                (x, y) =>
                {
                    float g = 0.20f + 0.08f * (Fbm(x, y, 11, 5, 8) - 0.5f) + 0.05f * (Hash(x, y, 12) - 0.5f);
                    float patch = Mathf.Clamp01((Fbm(x, y, 13, 3, 2) - 0.6f) * 3f);
                    g += patch * 0.05f;
                    Voronoi(x, y, 3, 14, out float d1, out float d2);
                    if (d2 - d1 < 1.2f && Fbm(x, y, 15, 2, 2) > 0.5f) g -= 0.08f;
                    return new Color(g, g, g * 1.03f);
                },
                (x, y) => Fbm(x, y, 11, 5, 8), 1.6f);

            public static (Texture2D, Texture2D) Rust() => Build("Rust",
                (x, y) =>
                {
                    float streak = Fbm(x, y * 0.25f, 21, 3, 6);              // vertical run-off
                    float blotch = Fbm(x, y, 22, 4, 3);
                    Color paint = new Color(0.55f, 0.28f, 0.14f);
                    Color rustDark = new Color(0.28f, 0.12f, 0.06f);
                    Color rustBright = new Color(0.80f, 0.40f, 0.12f);
                    Color c = Color.Lerp(paint, rustDark, Mathf.Clamp01((blotch - 0.45f) * 3f));
                    c = Color.Lerp(c, rustBright, Mathf.Clamp01((streak - 0.55f) * 3f) * 0.7f);
                    c *= 0.9f + 0.2f * Hash(x, y, 23);
                    // corrugation shading
                    c *= 0.9f + 0.1f * Mathf.Sin(x / (float)N * Mathf.PI * 2f * 16f);
                    return c;
                },
                (x, y) => 0.5f + 0.5f * Mathf.Sin(x / (float)N * Mathf.PI * 2f * 16f) * 0.8f + Fbm(x, y, 22, 4, 3) * 0.3f, 1.8f);

            public static (Texture2D, Texture2D) Steel() => Build("Steel",
                (x, y) =>
                {
                    float brush = 0.5f + 0.12f * (Noise(x * 0.15f, y, 64, 31) - 0.5f) + 0.05f * (Hash(x, y, 32) - 0.5f);
                    Color c = new Color(brush * 0.9f, brush * 0.93f, brush);
                    int rx = x % 128, ry = y % 128;
                    float rd = Mathf.Sqrt((rx - 12) * (rx - 12) + (ry - 12) * (ry - 12));
                    if (rd < 5f) c *= 0.65f + 0.5f * Mathf.Clamp01((5f - rd) / 5f);   // rivets
                    float seam = (x % 256 < 2 || y % 256 < 2) ? 0.6f : 1f;           // panel seams
                    return c * seam;
                },
                (x, y) =>
                {
                    int rx = x % 128, ry = y % 128;
                    float rd = Mathf.Sqrt((rx - 12) * (rx - 12) + (ry - 12) * (ry - 12));
                    float h = 0.5f + 0.05f * Noise(x * 0.15f, y, 64, 31);
                    if (rd < 5f) h += 0.4f * Mathf.Clamp01((5f - rd) / 5f);
                    if (x % 256 < 2 || y % 256 < 2) h -= 0.3f;
                    return h;
                }, 2.5f);

            public static (Texture2D, Texture2D) Wood() => Build("Wood",
                (x, y) =>
                {
                    int plank = y / 64;
                    float shade = 0.85f + 0.3f * Hash(plank, 0, 41);
                    float grain = Fbm(x * 0.2f, y + plank * 37f, 42, 3, 6);
                    Color c = new Color(0.55f, 0.36f, 0.20f) * shade * (0.85f + 0.3f * grain);
                    if (y % 64 < 3) c *= 0.45f;                                   // gaps between planks
                    if (Hash(x / 6, plank, 43) > 0.985f) c *= 0.6f;                // knots / nails
                    return c;
                },
                (x, y) => (y % 64 < 3 ? 0.1f : 0.5f) + 0.2f * Fbm(x * 0.2f, y, 42, 3, 6), 1.5f);

            public static (Texture2D, Texture2D) Brick() => Build("Brick",
                (x, y) =>
                {
                    int row = y / 64;
                    int ox = (row % 2 == 0) ? 0 : 64;
                    int bx = (x + ox) % 128, by = y % 64;
                    bool mortar = bx < 6 || by < 6;
                    if (mortar) { float m = 0.62f + 0.1f * (Hash(x, y, 51) - 0.5f); return new Color(m, m * 0.98f, m * 0.94f); }
                    float v = 0.8f + 0.4f * Hash((x + ox) / 128, row, 52);
                    Color c = new Color(0.62f * v, 0.28f * v, 0.20f * v);
                    c *= 0.9f + 0.2f * Fbm(x, y, 53, 3, 8);
                    return c;
                },
                (x, y) =>
                {
                    int row = y / 64; int ox = (row % 2 == 0) ? 0 : 64;
                    int bx = (x + ox) % 128, by = y % 64;
                    bool mortar = bx < 6 || by < 6;
                    return (mortar ? 0.1f : 0.6f) + 0.15f * Fbm(x, y, 53, 3, 8);
                }, 2.0f);

            public static (Texture2D, Texture2D) Painted(Color paint) => Build("Painted" + ColorUtility.ToHtmlStringRGB(paint),
                (x, y) =>
                {
                    float wear = Mathf.Clamp01((Fbm(x, y, 61, 4, 3) - 0.62f) * 5f);
                    float edgeWear = (x % 256 < 10 || y % 256 < 10) ? 0.35f : 0f;
                    Color metal = new Color(0.45f, 0.46f, 0.48f);
                    Color c = Color.Lerp(paint, metal, Mathf.Clamp01(wear + edgeWear * Hash(x, y, 62)));
                    c *= 0.93f + 0.14f * Hash(x, y, 63);
                    if (x % 256 < 2 || y % 256 < 2) c *= 0.7f;
                    return c;
                },
                (x, y) => (x % 256 < 2 || y % 256 < 2 ? 0.2f : 0.5f) + 0.1f * Fbm(x, y, 61, 4, 3), 1.2f);
        }
    }

    /// <summary>Unit cubes whose UVs are scaled to the box's world size, so tiled textures never stretch.</summary>
    internal static class WorldUvBox
    {
        private static readonly Dictionary<(int, int, int), Mesh> _cache = new Dictionary<(int, int, int), Mesh>();
        public const float TileMetres = 2f;

        public static Mesh For(float sx, float sy, float sz)
        {
            var key = (Mathf.RoundToInt(sx * 100f), Mathf.RoundToInt(sy * 100f), Mathf.RoundToInt(sz * 100f));
            if (_cache.TryGetValue(key, out var m) && m) return m;
            m = Build(sx, sy, sz);
            _cache[key] = m;
            return m;
        }

        private static Mesh Build(float sx, float sy, float sz)
        {
            var v = new List<Vector3>(24); var n = new List<Vector3>(24); var uv = new List<Vector2>(24); var t = new List<int>(36);
            void Face(Vector3 normal, Vector3 right, Vector3 up, float w, float h)
            {
                int i0 = v.Count;
                Vector3 c = normal * 0.5f;
                v.Add(c - right * 0.5f - up * 0.5f); v.Add(c + right * 0.5f - up * 0.5f); v.Add(c + right * 0.5f + up * 0.5f); v.Add(c - right * 0.5f + up * 0.5f);
                for (int i = 0; i < 4; i++) n.Add(normal);
                float uw = w / TileMetres, uh = h / TileMetres;
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(uw, 0)); uv.Add(new Vector2(uw, uh)); uv.Add(new Vector2(0, uh));
                t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1); t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 2);
            }
            Face(Vector3.forward, Vector3.left, Vector3.up, sx, sy);
            Face(Vector3.back, Vector3.right, Vector3.up, sx, sy);
            Face(Vector3.right, Vector3.forward, Vector3.up, sz, sy);
            Face(Vector3.left, Vector3.back, Vector3.up, sz, sy);
            Face(Vector3.up, Vector3.right, Vector3.forward, sx, sz);
            Face(Vector3.down, Vector3.right, Vector3.back, sx, sz);
            var m = new Mesh { name = "HTF1v1_Box" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }
    }
}
