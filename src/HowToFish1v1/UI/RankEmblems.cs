using System.Collections.Generic;
using UnityEngine;

namespace HowToFish1v1.UI
{
    /// <summary>
    /// Procedurally drawn rank emblems, one per tier. Every emblem is a bevelled brushed-metal badge with a dark inner
    /// field and an embossed fishing hook. The badge shape and trimmings climb with the tier: plain heater shield with
    /// stars (copper/bronze), shouldered shield with chevrons (silver/steel), winged pentagon (gold), winged hex badge
    /// with a gem (platinum/emerald), a faceted diamond, and for champion a crowned shield in a sunburst.
    /// Anti-aliased with 3x3 supersampling; colours climb from copper to champion red.
    /// </summary>
    internal static class RankEmblems
    {
        private const int Size = 256;
        private const int SS = 3;   // supersamples per axis
        private const float Canvas = 1.26f;
        private const float CornerRadius = 0.07f;
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
            t = Draw(tier);
            _cache[tier] = t;
            return t;
        }

        // ------------------------------------------------------------------ small maths

        private static float Smooth(float d, float aa = 0.012f) => Mathf.Clamp01(d / aa + 0.5f);
        private static Color Mix(Color a, Color b, float t) => Color.LerpUnclamped(a, b, Mathf.Clamp01(t));
        private static Color Mul(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        private static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float abx = bx - ax, aby = by - ay;
            float l2 = abx * abx + aby * aby;
            float t = l2 <= 0f ? 0f : Mathf.Clamp01(((px - ax) * abx + (py - ay) * aby) / l2);
            float cx = ax + abx * t, cy = ay + aby * t;
            float dx = px - cx, dy = py - cy;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Signed distance to a polygon: positive inside, negative outside.</summary>
        private static float Poly(float px, float py, Vector2[] pts)
        {
            int n = pts.Length; bool inside = false; float d = 1e9f;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = pts[i].x, yi = pts[i].y, xj = pts[j].x, yj = pts[j].y;
                if ((yi > py) != (yj > py))
                {
                    float x = (xj - xi) * (py - yi) / (yj - yi) + xi;
                    if (px < x) inside = !inside;
                }
                d = Mathf.Min(d, SegDist(px, py, xi, yi, xj, yj));
            }
            return inside ? d : -d;
        }

        /// <summary>Offsets a convex polygon inward by r: edges slide along their inward normals and corners are re-intersected.</summary>
        private static Vector2[] Inset(Vector2[] pts, float r)
        {
            int n = pts.Length;
            float area = 0f;
            for (int i = 0; i < n; i++) { var a = pts[i]; var b = pts[(i + 1) % n]; area += a.x * b.y - b.x * a.y; }
            float sgn = area > 0f ? 1f : -1f;
            var px = new float[n]; var py = new float[n]; var dx = new float[n]; var dy = new float[n];
            for (int i = 0; i < n; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % n];
                float ex = b.x - a.x, ey = b.y - a.y, l = Mathf.Sqrt(ex * ex + ey * ey);
                ex /= l; ey /= l;
                float nx = -ey * sgn, ny = ex * sgn;
                px[i] = a.x + nx * r; py[i] = a.y + ny * r; dx[i] = ex; dy[i] = ey;
            }
            var outPts = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                int h = (i + n - 1) % n;
                float den = dx[h] * dy[i] - dy[h] * dx[i];
                float t = ((px[i] - px[h]) * dy[i] - (py[i] - py[h]) * dx[i]) / den;
                outPts[i] = new Vector2(px[h] + dx[h] * t, py[h] + dy[h] * t);
            }
            return outPts;
        }

        private static Vector2[] StarPts(float cx, float cy, float r)
        {
            var pts = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI / 2f + i * Mathf.PI / 5f;
                float rr = (i % 2 == 0) ? r : r * 0.45f;
                pts[i] = new Vector2(cx + Mathf.Cos(a) * rr, cy + Mathf.Sin(a) * rr);
            }
            return pts;
        }

        private static Vector2 V(float x, float y) => new Vector2(x, y);

        private static Vector2[] ShieldPts(int group)
        {
            switch (group)
            {
                case 0: return new[] { V(-0.62f, 0.72f), V(0.62f, 0.72f), V(0.62f, 0.05f), V(0.45f, -0.45f), V(0f, -0.86f), V(-0.45f, -0.45f), V(-0.62f, 0.05f) };
                case 1: return new[] { V(-0.66f, 0.60f), V(-0.40f, 0.74f), V(0.40f, 0.74f), V(0.66f, 0.60f), V(0.66f, 0f), V(0.42f, -0.50f), V(0f, -0.88f), V(-0.42f, -0.50f), V(-0.66f, 0f) };
                case 2: return new[] { V(-0.70f, 0.42f), V(0f, 0.82f), V(0.70f, 0.42f), V(0.52f, -0.62f), V(0f, -0.88f), V(-0.52f, -0.62f) };
                case 3: return new[] { V(-0.72f, 0.30f), V(-0.40f, 0.80f), V(0.40f, 0.80f), V(0.72f, 0.30f), V(0.60f, -0.55f), V(0f, -0.90f), V(-0.60f, -0.55f) };
                case 4: return new[] { V(-0.72f, 0.30f), V(-0.36f, 0.72f), V(0.36f, 0.72f), V(0.72f, 0.30f), V(0f, -0.92f) };
                default: return new[] { V(-0.66f, 0.50f), V(-0.36f, 0.66f), V(0.36f, 0.66f), V(0.66f, 0.50f), V(0.66f, -0.05f), V(0.44f, -0.50f), V(0f, -0.92f), V(-0.44f, -0.50f), V(-0.66f, -0.05f) };
            }
        }

        private static readonly Vector2[] CrownPts = { V(-0.40f, 0.62f), V(-0.40f, 0.86f), V(-0.22f, 0.72f), V(0f, 0.96f), V(0.22f, 0.72f), V(0.40f, 0.86f), V(0.40f, 0.62f) };
        private static readonly Vector2[] GemPts = { V(-0.16f, 0.55f), V(0f, 0.66f), V(0.16f, 0.55f), V(0f, 0.32f) };

        /// <summary>Five overlapping feathers fanning up and out from the badge's shoulder.</summary>
        private static float Wing(float u, float v, float side, float size)
        {
            float best = -1f;
            for (int i = 0; i < 5; i++)
            {
                float t = i / 4f;
                float ang = side * (-0.05f + 0.55f * t);
                float cx = side * (0.56f + 0.08f * i * size), cy = 0.34f - 0.14f * i * size;
                float l = 0.22f * size * (1f - 0.10f * i), w = 0.058f * size;
                float c = Mathf.Cos(ang), s = Mathf.Sin(ang);
                cx += side * c * l * 0.55f; cy -= s * l * 0.55f;
                float dx = u - cx, dy = v - cy;
                float lx = dx * c + dy * s, ly = -dx * s + dy * c;
                float e = (lx * lx) / (l * l) + (ly * ly) / (w * w);
                best = Mathf.Max(best, (1f - e) * w);
            }
            return best;
        }

        private static Vector2[] _hookPts;
        private static Vector2[] HookPts()
        {
            if (_hookPts != null) return _hookPts;
            var list = new List<Vector2> { V(0.12f, 0.42f), V(0.12f, -0.16f) };
            for (int i = 1; i <= 14; i++)
            {
                float a = Mathf.PI * i / 14f;
                list.Add(V(0.12f - 0.25f * (1f - Mathf.Cos(a)), -0.16f - 0.25f * Mathf.Sin(a)));
            }
            list.Add(V(-0.38f, 0.02f));
            return _hookPts = list.ToArray();
        }
        private static readonly Vector2[] BarbPts = { V(-0.34f, -0.02f), V(-0.24f, -0.10f), V(-0.36f, -0.14f) };

        /// <summary>Fish hook silhouette: eye, shank, round bend, point rising to a barb. Positive inside.</summary>
        private static float Hook(float u, float v, float scale)
        {
            float x = u / scale, y = v / scale;
            var pts = HookPts();
            float d = 1e9f;
            for (int i = 0; i < pts.Length - 1; i++) d = Mathf.Min(d, SegDist(x, y, pts[i].x, pts[i].y, pts[i + 1].x, pts[i + 1].y));
            float body = 0.048f - d;
            float barb = Poly(x, y, BarbPts);
            float ex = x - 0.12f, ey = y - 0.48f;
            float eye = 0.032f - Mathf.Abs(Mathf.Sqrt(ex * ex + ey * ey) - 0.065f);
            return Mathf.Max(body, Mathf.Max(barb, eye)) * scale;
        }

        // ------------------------------------------------------------------ the sampler

        private struct Px { public float r, g, b, a; }

        private static void Blend(ref Px dst, Color c, float a)
        {
            a = Mathf.Clamp01(a);
            dst.r = Mathf.Lerp(dst.r, c.r, a); dst.g = Mathf.Lerp(dst.g, c.g, a); dst.b = Mathf.Lerp(dst.b, c.b, a);
            dst.a = Mathf.Max(dst.a, a);
        }

        private static int GroupOf(int tier) => tier < 2 ? 0 : tier < 4 ? 1 : tier < 6 ? 2 : tier < 8 ? 3 : tier == 8 ? 4 : 5;

        private static Px Sample(float u, float v, int tier, Vector2[] shield)
        {
            Color baseC = TierColors[tier];
            int group = GroupOf(tier);
            Color metalL = Mix(baseC, Color.white, 0.62f), metalD = Mul(baseC, 0.38f);
            Color field = new Color(0.06f, 0.075f, 0.11f);
            var outPx = new Px();

            if (group >= 2)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    float wd = Wing(u, v, side, group < 5 ? 1f : 1.15f);
                    if (wd > -0.02f)
                    {
                        float shade = 0.55f + 0.45f * Mathf.Clamp01((Mathf.Abs(u) - 0.6f) / 0.6f);
                        Blend(ref outPx, Mix(metalD, metalL, shade), Smooth(wd, 0.02f));
                    }
                }
            }
            if (group == 5)
            {
                float ang = Mathf.Atan2(v - 0.1f, u), r = Mathf.Sqrt(u * u + (v - 0.1f) * (v - 0.1f));
                if (r > 0.75f && r < 1.15f)
                {
                    float ray = 0.5f + 0.5f * Mathf.Cos(ang * 12f);
                    float glow = Mathf.Clamp01((1.15f - r) / 0.45f) * Mathf.Clamp01((r - 0.75f) / 0.1f);
                    Blend(ref outPx, new Color(1f, 0.75f, 0.3f), glow * (0.25f + 0.45f * ray * ray * ray));
                }
                float cd = Poly(u, v, CrownPts);
                if (cd > -0.02f)
                    Blend(ref outPx, Mix(new Color(1f, 0.85f, 0.35f), new Color(1f, 0.97f, 0.75f), (v - 0.62f) / 0.34f), Smooth(cd));
            }

            float d = Poly(u, v, shield) + CornerRadius;   // shield is pre-inset, so this rounds the corners
            if (d < -0.015f) return outPx;
            float alpha = Smooth(d);
            const float rim = 0.09f, bevel = 0.05f;
            Color c;
            if (d < rim)
            {
                float t = d / rim;
                float light = Mathf.Clamp01(0.5f + 0.5f * (-u * 0.6f + v * 0.8f));
                float sheen = 0.5f + 0.5f * Mathf.Sin((u * 3f + v * 5f) * 3.1f) * 0.2f + 0.15f * Mathf.Sin((u - v) * 11f);
                c = Mix(metalD, metalL, 0.30f + 0.55f * sheen);
                c = Mix(c, Color.white, 0.30f * light * (1f - t));
                c = Mix(c, metalD, 0.45f * (1f - light) * (1f - t));
                if (t > 0.78f) c = Mix(c, metalD, 0.5f);
            }
            else
            {
                float inner = d - rim;
                float hx = u + 0.3f, hy = v - 0.35f;
                float hl = Mathf.Clamp01(1f - Mathf.Sqrt(hx * hx + hy * hy) / 0.9f);
                c = Mix(field, Mix(baseC, Color.white, 0.2f), 0.25f * hl * hl);
                c = Mix(c, Mul(baseC, 0.7f), 0.22f * Mathf.Clamp01((v + 1f) / 2f));
                if (inner < bevel) c = Mix(Mul(baseC, 0.85f), c, inner / bevel);
                float sx = (u - v * 0.5f + 0.25f) / 0.07f;
                c = Mix(c, Mix(baseC, Color.white, 0.5f), 0.12f * Mathf.Exp(-sx * sx));

                // embossed hook: offset shadow, then the lit body
                float hs = Hook(u - 0.012f, v + 0.012f, 0.95f);
                if (hs > -0.012f) c = Mix(c, new Color(0.02f, 0.02f, 0.03f), Smooth(hs) * 0.7f);
                float hk = Hook(u, v, 0.95f);
                if (hk > -0.012f)
                {
                    Color hc = Mix(baseC, Color.white, 0.35f + 0.25f * Mathf.Clamp01(0.5f + 0.5f * (-u + v)));
                    c = Mix(c, hc, Smooth(hk) * 0.85f);
                }

                if (group == 1)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        float yy = 0.52f - k * 0.11f;
                        float ch = 0.035f - Mathf.Abs(Mathf.Abs(u) * 0.6f - (yy - v));
                        if (Mathf.Abs(u) < 0.36f && ch > -0.01f && v < yy + 0.02f) c = Mix(c, metalL, Smooth(ch) * 0.9f);
                    }
                }

                int nstars = group < 4 ? (tier % 2) + 1 : 3;
                for (int i = 0; i < nstars; i++)
                {
                    float sxp = (i - (nstars - 1) / 2f) * 0.20f;
                    float sd = Poly(u, v, StarPts(sxp, -0.50f, 0.075f));
                    if (sd > -0.012f) c = Mix(c, Mix(metalL, Color.white, 0.4f), Smooth(sd));
                }

                if (group == 4)
                {
                    float[] f = { -0.72f, 0.30f, 0f, -0.05f, 0.72f, 0.30f, 0f, -0.05f, -0.36f, 0.72f, 0f, -0.05f, 0.36f, 0.72f, 0f, -0.05f, 0f, -0.05f, 0f, -0.92f, -0.72f, 0.30f, 0.72f, 0.30f };
                    for (int i = 0; i < f.Length; i += 4)
                    {
                        float fd = 0.012f - SegDist(u, v, f[i], f[i + 1], f[i + 2], f[i + 3]);
                        if (fd > -0.01f) c = Mix(c, Mix(baseC, Color.white, 0.55f), Smooth(fd) * 0.5f);
                    }
                    if (v > 0.30f) c = Mix(c, Color.white, 0.10f);
                    else if (u < 0f) c = Mix(c, Color.black, 0.12f);
                }

                if (group >= 3)
                {
                    float gd = Poly(u, v, GemPts);
                    if (gd > -0.012f)
                    {
                        float facet = u < 0f ? 0.55f : 0.9f;
                        Color gc = Mix(Mix(baseC, Color.white, 0.5f), Color.white, facet * Mathf.Clamp01(0.6f + 0.4f * (v - 0.45f) / 0.2f));
                        if (gd < 0.012f) gc = metalD;
                        c = Mix(c, gc, Smooth(gd));
                    }
                }
            }
            Blend(ref outPx, c, alpha);
            return outPx;
        }

        private static Texture2D Draw(int tier)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[Size * Size];
            var shield = Inset(ShieldPts(GroupOf(tier)), CornerRadius);
            float inv = 1f / (SS * SS);
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                        {
                            float u = (2f * (x + (sx + 0.5f) / SS) / Size - 1f) * Canvas;
                            float v = (2f * (y + (sy + 0.5f) / SS) / Size - 1f) * Canvas;   // texture rows run bottom-up
                            var s = Sample(u, v, tier, shield);
                            r += s.r * s.a; g += s.g * s.a; b += s.b * s.a; a += s.a;
                        }
                    r *= inv; g *= inv; b *= inv; a *= inv;
                    if (a > 0.001f)
                        px[y * Size + x] = new Color32((byte)(Mathf.Clamp01(r / a) * 255), (byte)(Mathf.Clamp01(g / a) * 255), (byte)(Mathf.Clamp01(b / a) * 255), (byte)(a * 255));
                    else px[y * Size + x] = new Color32(0, 0, 0, 0);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
