using System.Collections.Generic;
using HowToFish1v1.Core;
using UnityEngine;

namespace HowToFish1v1.UI
{
    /// <summary>Top-down minimap of an arena layout, drawn from its boxes. X runs left to right, Z bottom to top.</summary>
    internal static class MapPreview
    {
        private const int W = 384, H = 256;
        private static readonly Dictionary<int, Texture2D> _cache = new Dictionary<int, Texture2D>();

        public static Texture2D Get(int mapIndex)
        {
            if (_cache.TryGetValue(mapIndex, out var t) && t) return t;
            t = Draw(ArenaLayout.Create(mapIndex));
            _cache[mapIndex] = t;
            return t;
        }

        private static Color ColorFor(BoxKind k)
        {
            switch (k)
            {
                case BoxKind.Rust: return new Color(0.85f, 0.42f, 0.16f);
                case BoxKind.Steel: return new Color(0.45f, 0.48f, 0.55f);
                case BoxKind.Wood: return new Color(0.70f, 0.50f, 0.28f);
                case BoxKind.Brick: return new Color(0.60f, 0.28f, 0.22f);
                case BoxKind.Yellow: return new Color(0.98f, 0.82f, 0.15f);
                case BoxKind.Red: return new Color(0.85f, 0.18f, 0.15f);
                case BoxKind.Blue: return new Color(0.25f, 0.45f, 0.85f);
                case BoxKind.White: return new Color(0.92f, 0.92f, 0.88f);
                default: return new Color(0.66f, 0.66f, 0.64f);
            }
        }

        private static Texture2D Draw(ArenaLayout l)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var px = new Color[W * H];
            var bg = new Color(0.07f, 0.11f, 0.16f, 1f);
            for (int i = 0; i < px.Length; i++) px[i] = bg;

            float scale = Mathf.Min((W - 16) / (2f * l.HalfWidth), (H - 16) / (2f * l.HalfDepth));
            void Fill(float cx, float cz, float sx, float sz, Color c)
            {
                int x0 = Mathf.RoundToInt(W / 2f + (cx - sx / 2f) * scale), x1 = Mathf.RoundToInt(W / 2f + (cx + sx / 2f) * scale);
                int y0 = Mathf.RoundToInt(H / 2f - (cz + sz / 2f) * scale), y1 = Mathf.RoundToInt(H / 2f - (cz - sz / 2f) * scale);
                for (int y = Mathf.Max(0, y0); y < Mathf.Min(H, y1); y++)
                    for (int x = Mathf.Max(0, x0); x < Mathf.Min(W, x1); x++)
                        px[y * W + x] = c;
            }

            // Floor first, then everything else in layout order; taller boxes drawn brighter.
            foreach (var b in l.Boxes)
                if (b.Name == "Floor") Fill(b.X, b.Z, b.SX, b.SZ, new Color(0.22f, 0.25f, 0.28f));
            foreach (var b in l.Boxes)
            {
                if (b.Kind == BoxKind.Invisible || b.Name == "Floor") continue;
                var c = ColorFor(b.Kind);
                float top = b.Y + b.SY / 2f;
                c = Color.Lerp(c * 0.7f, c, Mathf.Clamp01(top / 4f));
                c.a = 1f;
                if (b.Name.StartsWith("SpawnPad")) c = new Color(0.25f, 0.55f, 0.95f);
                Fill(b.X, b.Z, b.SX, b.SZ, c);
            }
            // Spawns: pads as bright dots, free-for-all points as small green dots.
            foreach (var s in l.FfaSpawns) Fill(s.X, s.Z, 1.4f, 1.4f, new Color(0.35f, 0.95f, 0.45f));
            Fill(l.Left.X, l.Left.Z, 2f, 2f, new Color(0.4f, 0.75f, 1f));
            Fill(l.Right.X, l.Right.Z, 2f, 2f, new Color(1f, 0.55f, 0.35f));

            tex.SetPixels(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }
    }
}
