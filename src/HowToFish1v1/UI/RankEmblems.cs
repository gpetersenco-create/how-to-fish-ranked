using System.Collections.Generic;
using UnityEngine;

namespace HowToFish1v1.UI
{
    /// <summary>Procedurally drawn shield emblems, one per rank tier, in metal colors that climb from copper to champion red.</summary>
    internal static class RankEmblems
    {
        private const int Size = 256;
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

        private static Texture2D Draw(Color baseColor, int tier)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var px = new Color[Size * Size];
            Color border = Color.Lerp(baseColor, Color.white, 0.55f);
            Color dark = baseColor * 0.45f; dark.a = 1f;
            Color band = Color.Lerp(baseColor, Color.white, 0.18f);
            bool champion = tier >= TierColors.Length - 1;

            for (int y = 0; y < Size; y++)
            {
                float v = 2f * y / (Size - 1) - 1f;          // Unity textures start at the bottom row: -1 bottom, +1 top
                for (int x = 0; x < Size; x++)
                {
                    float u = 2f * x / (Size - 1) - 1f;
                    float au = Mathf.Abs(u);
                    // Shield outline: flat top, straight sides, tapering to a point at the bottom.
                    float half = v > -0.15f ? 0.82f : 0.82f * (v + 1f) / 0.85f;
                    float top = 0.92f;
                    bool inside = v <= top && v >= -1f && au <= half;
                    if (!inside) { px[y * Size + x] = Color.clear; continue; }
                    float edge = Mathf.Min(half - au, top - v);
                    Color c;
                    if (edge < 0.07f) c = border;
                    else
                    {
                        float shade = 0.72f + 0.33f * (v + 1f) / 2f + (u - v) * 0.04f;
                        c = baseColor * shade; c.a = 1f;
                        // Inner shield gives depth; a horizontal band carries the numeral.
                        float half2 = half * 0.72f;
                        bool inner = au <= half2 && v <= 0.62f && v >= -0.74f;
                        if (inner)
                        {
                            c = dark;
                            if (Mathf.Abs(v - 0.05f) < 0.22f) c = Color.Lerp(dark, band, 0.35f);
                            if (Mathf.Abs(au - half2) < 0.05f || Mathf.Abs(v - 0.62f) < 0.05f) c = Color.Lerp(dark, border, 0.6f);
                        }
                        // Champion tier gets a diagonal gold sash.
                        if (champion && Mathf.Abs(u + v * 0.6f) < 0.08f && !inner) c = new Color(1f, 0.85f, 0.3f);
                    }
                    px[y * Size + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }
    }
}
