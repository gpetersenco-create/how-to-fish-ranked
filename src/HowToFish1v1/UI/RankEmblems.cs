using System.Collections.Generic;
using UnityEngine;

namespace HowToFish1v1.UI
{
    /// <summary>
    /// Procedurally drawn rank emblems, one per tier: a bevelled metal shield with a brushed-metal sheen, a dark inner
    /// field, a ribbon band for the numeral, one gem per tier along the bottom, laurels from the gold tiers up and a crown
    /// for the top tier. Anti-aliased with 3x3 supersampling. Colours climb from copper to champion red.
    /// </summary>
    internal static class RankEmblems
    {
        private const int Size = 256;
        private const int SS = 3;   // supersamples per axis
        private static readonly Dictionary<int, Texture2D> _cache = new Dictionary<int, Texture2D>();

        private static readonly Color[] TierColors =
        {
            new Color(0.66f, 0.40f, 0.20f), // copper
            new Color(0.80f, 0.52f, 0.26f), // bronze
            new Color(0.72f, 0.74f, 0.78f), // silver
            new Color(0.58f, 0.68f, 0.80f), // steel blue
            new Color(0.93f, 0.74f, 0.18f), // gold
            new Color(1.00f, 0.84f, 0.32f), // bright gold
            new Color(0.50f, 0.86f, 0.86f), // platinum
            new Color(0.22f, 0.82f, 0.56f), // emerald
            new Color(0.66f, 0.46f, 0.96f), // diamond
            new Color(0.96f, 0.30f, 0.26f), // champion
        };

        public static Color ColorFor(int tier) => TierColors[Mathf.Clamp(tier, 0, TierColors.Length - 1)];
        public static int Count => TierColors.Length;

        public static string Numeral(int tier)
        {
            string[] r = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
            return r[Mathf.Clamp(tier, 0, r.Length - 1)];
        }

        public static Texture2D Get(int tier)
        {
            tier = Mathf.Clamp(tier, 0, TierColors.Length - 1);
            if (_cache.TryGetValue(tier, out var t) && t) return t;
            t = Draw(TierColors[tier], tier);
            _cache[tier] = t;
            return t;
        }

        // ------------------------------------------------------------------ shape helpers (u, v in -1..1, v up)

        /// <summary>Signed distance-like inside measure for the shield: positive inside, scaled roughly to units.</summary>
        private static float Shield(float u, float v)
        {
            float au = Mathf.Abs(u);
            float top = 0.80f;
            // Sides bow out slightly, then sweep to a point at the bottom.
            float half = v > -0.05f ? 0.70f + 0.06f * Mathf.Sin((v + 0.05f) / 0.85f * Mathf.PI) : 0.70f * Mathf.Sqrt(Mathf.Clamp01((v + 0.95f) / 0.90f));
            float dTop = top - v, dBottom = v + 0.95f, dSide = half - au;
            return Mathf.Min(dTop, Mathf.Min(dBottom, dSide));
        }

        private static float Star(float u, float v, float cx, float cy, float r)
        {
            float dx = u - cx, dy = v - cy;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            if (d < 1e-5f) return r;
            float ang = Mathf.Atan2(dy, dx);
            float k = 0.5f + 0.5f * Mathf.Cos(5f * ang - Mathf.PI / 2f);
            float radius = r * Mathf.Lerp(0.45f, 1f, Mathf.Pow(k, 3f));
            return radius - d;
        }

        private static float Leaf(float u, float v, float cx, float cy, float ang, float len, float wid)
        {
            float c = Mathf.Cos(ang), s = Mathf.Sin(ang);
            float dx = u - cx, dy = v - cy;
            float lx = dx * c + dy * s, ly = -dx * s + dy * c;
            float e = (lx * lx) / (len * len) + (ly * ly) / (wid * wid);
            return (1f - e) * wid;
        }

        private static float Smooth(float d, float aa = 0.012f) => Mathf.Clamp01(d / aa + 0.5f);

        // ------------------------------------------------------------------ painter

        private static Color Sample(float u, float v, Color baseColor, int tier)
        {
            bool champion = tier >= TierColors.Length - 1;
            bool laurels = tier >= 4;
            Color metalLight = Color.Lerp(baseColor, Color.white, 0.62f);
            Color metalDark = baseColor * 0.42f; metalDark.a = 1f;
            Color field = new Color(0.07f, 0.08f, 0.11f);
            Color rgba = Color.clear;

            float d = Shield(u, v);
            // --- laurels behind the shield
            if (laurels)
            {
                float best = -1f;
                for (int side = -1; side <= 1; side += 2)
                    for (int i = 0; i < 7; i++)
                    {
                        float t = i / 6f;
                        float ang = Mathf.Lerp(-0.55f, 0.75f, t);
                        float cx = side * (0.70f + 0.16f * Mathf.Sin(ang + 0.4f)), cy = -0.70f + 1.35f * t;
                        float leafAng = side * (Mathf.PI * 0.5f - ang * 0.6f);
                        best = Mathf.Max(best, Leaf(u, v, cx, cy, leafAng, 0.13f, 0.055f));
                    }
                if (best > -0.02f)
                {
                    float a = Smooth(best, 0.02f);
                    Color leaf = Color.Lerp(new Color(0.55f, 0.42f, 0.14f), new Color(1f, 0.86f, 0.4f), Mathf.Clamp01(best / 0.05f));
                    rgba = new Color(leaf.r, leaf.g, leaf.b, a);
                }
            }
            // --- crown for the top tier
            if (champion)
            {
                float crown = -1f;
                for (int i = -1; i <= 1; i++)
                {
                    float cx = i * 0.28f, tipY = 1.0f - Mathf.Abs(i) * 0.06f;
                    float w = 0.16f;
                    float t = Mathf.InverseLerp(0.78f, tipY, v);
                    float halfW = Mathf.Lerp(w, 0.02f, t);
                    if (v >= 0.78f && v <= tipY) crown = Mathf.Max(crown, halfW - Mathf.Abs(u - cx));
                }
                if (v >= 0.74f && v <= 0.80f && Mathf.Abs(u) <= 0.44f) crown = Mathf.Max(crown, 0.03f - Mathf.Abs(v - 0.77f));
                if (crown > -0.02f)
                {
                    float a = Smooth(crown, 0.015f);
                    Color c = Color.Lerp(new Color(1f, 0.85f, 0.35f), new Color(1f, 0.95f, 0.7f), Mathf.Clamp01((v - 0.74f) / 0.3f));
                    rgba = Color.Lerp(rgba, new Color(c.r, c.g, c.b, 1f), a);
                    rgba.a = Mathf.Max(rgba.a, a);
                }
            }
            if (d < -0.015f) return rgba;

            // --- shield body
            float alpha = Smooth(d);
            Color c2;
            float rim = 0.075f, bevel = 0.05f;
            if (d < rim)
            {
                // Metal rim with a brushed sheen and a bevel: lit from the top-left.
                float t = d / rim;
                float sheen = 0.5f + 0.5f * Mathf.Sin((u * 3f + v * 5f) * 3.1f) * 0.25f + 0.25f * Mathf.Sin((u - v) * 9f);
                Color rimC = Color.Lerp(metalDark, metalLight, 0.35f + 0.55f * sheen);
                float light = Mathf.Clamp01(0.5f + 0.5f * (-u * 0.7f + v * 0.7f));
                rimC = Color.Lerp(rimC, Color.white, 0.25f * light * (1f - t));
                rimC = Color.Lerp(rimC, metalDark, 0.35f * (1f - light) * (1f - t));
                c2 = rimC;
            }
            else
            {
                float inner = d - rim;
                Color body = field;
                // Radial highlight upper-left and a vertical gradient.
                float hl = Mathf.Clamp01(1f - Mathf.Sqrt((u + 0.3f) * (u + 0.3f) + (v - 0.35f) * (v - 0.35f)) / 0.9f);
                body = Color.Lerp(body, Color.Lerp(baseColor, Color.white, 0.2f), 0.22f * hl * hl);
                body = Color.Lerp(body, baseColor * 0.6f, 0.25f * Mathf.Clamp01((v + 1f) / 2f));
                // Inner bevel edge.
                if (inner < bevel) body = Color.Lerp(baseColor * 0.9f, body, inner / bevel);
                // Diagonal sheen stripe.
                float stripe = Mathf.Exp(-Mathf.Pow((u - v * 0.5f + 0.25f) / 0.07f, 2f));
                body = Color.Lerp(body, Color.Lerp(baseColor, Color.white, 0.5f), 0.12f * stripe);
                // Ribbon band for the numeral.
                float bandD = 0.20f - Mathf.Abs(v - 0.02f);
                if (bandD > -0.02f)
                {
                    float ba = Smooth(bandD, 0.015f);
                    Color band = Color.Lerp(baseColor * 0.55f, baseColor * 0.8f, Mathf.Clamp01((v + 0.18f) / 0.4f)); band.a = 1f;
                    float trim = Mathf.Min(Mathf.Abs(bandD - 0.02f), 1f);
                    if (bandD < 0.035f) band = Color.Lerp(metalLight, band, Mathf.Clamp01((bandD - 0.005f) / 0.03f));
                    body = Color.Lerp(body, band, ba);
                }
                // Gems along the bottom point: one per tier.
                int gems = tier + 1;
                for (int i = 0; i < gems; i++)
                {
                    float gx = (i - (gems - 1) / 2f) * 0.11f, gy = -0.36f - Mathf.Abs(gx) * 0.35f;
                    float gd = 0.04f - Mathf.Sqrt((u - gx) * (u - gx) + (v - gy) * (v - gy));
                    if (gd > -0.012f)
                    {
                        Color gem = Color.Lerp(baseColor, Color.white, 0.6f);
                        float spec = Mathf.Clamp01(1f - Mathf.Sqrt((u - gx + 0.012f) * (u - gx + 0.012f) + (v - gy - 0.012f) * (v - gy - 0.012f)) / 0.02f);
                        gem = Color.Lerp(gem, Color.white, spec);
                        if (gd < 0.008f) gem = metalDark;
                        body = Color.Lerp(body, gem, Smooth(gd));
                    }
                }
                // Small star at the top of the field.
                float st = Star(u, v, 0f, 0.52f, 0.11f);
                if (st > -0.012f) body = Color.Lerp(body, Color.Lerp(metalLight, Color.white, 0.4f), Smooth(st));
                // Champion sash.
                if (champion)
                {
                    float sash = 0.06f - Mathf.Abs(u + v * 0.55f + 0.35f);
                    if (sash > -0.01f && Mathf.Abs(v - 0.02f) > 0.2f) body = Color.Lerp(body, new Color(1f, 0.85f, 0.3f), Smooth(sash) * 0.85f);
                }
                c2 = body;
            }
            c2.a = 1f;
            // Drop shadow under the shield edge onto whatever lies behind (laurels).
            var outC = new Color(c2.r, c2.g, c2.b, alpha);
            return Color.Lerp(rgba, outC, alpha);
        }

        private static Texture2D Draw(Color baseColor, int tier)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true);
            var px = new Color[Size * Size];
            float inv = 1f / (SS * SS);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    Color acc = Color.clear;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                        {
                            float u = 2f * (x + (sx + 0.5f) / SS) / Size - 1f;
                            float v = 2f * (y + (sy + 0.5f) / SS) / Size - 1f;   // Unity rows start at the bottom
                            var c = Sample(u * 1.08f, v * 1.08f, baseColor, tier);
                            acc += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
                        }
                    acc *= inv;
                    px[y * Size + x] = acc.a > 0.001f ? new Color(acc.r / acc.a, acc.g / acc.a, acc.b / acc.a, acc.a) : Color.clear;
                }
            tex.SetPixels(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            return tex;
        }
    }
}
