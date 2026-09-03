using System.Collections.Generic;
using UnityEngine;

namespace HowToFish1v1.UI
{
    /// <summary>
    /// Shared look for the full-screen ranked pages: a 1920x1080 design canvas, a deep navy gradient background with
    /// drifting light, layered cards (soft shadow, top-lit gradient, hairline edge), gold accents, buttons that lift and
    /// glow as the mouse reaches them, radial glows behind rank emblems, and small entrance animations driven by
    /// <see cref="Ease"/>.
    /// </summary>
    internal static class RankedStyles
    {
        public const float DesignW = 1920f, DesignH = 1080f;

        public static Texture2D Bg, Panel, PanelLight, PanelHover, Gold, GoldDim, BarBg, White, Green, Red;
        public static GUIStyle Tab, TabOn, Title, H1, H1Center, H2, Body, BodyCenter, Small, SmallCenter, SmallRight,
            Stat, StatLabel, GoldText, GreenText, BigButton, Button, ToggleButton, ToggleButtonOn;

        public static readonly Color GoldColor = new Color(0.97f, 0.80f, 0.30f);
        public static readonly Color Muted = new Color(0.70f, 0.76f, 0.85f);
        public static readonly Color PanelColor = new Color(0.075f, 0.11f, 0.17f, 0.96f);
        public static readonly Color PanelLightColor = new Color(0.12f, 0.18f, 0.27f, 0.96f);
        public static readonly Color PanelHoverColor = new Color(0.20f, 0.28f, 0.40f, 1f);
        public static readonly Color GoldDimColor = new Color(0.52f, 0.42f, 0.16f, 1f);
        public static readonly Color GreenColor = new Color(0.25f, 0.72f, 0.42f);
        public static readonly Color RedColor = new Color(0.78f, 0.26f, 0.26f);
        public static readonly Color BgTop = new Color(0.07f, 0.11f, 0.18f, 1f);
        public static readonly Color BgBottom = new Color(0.025f, 0.035f, 0.06f, 1f);

        private static bool _ready;
        private static readonly Dictionary<string, float> _openAt = new Dictionary<string, float>();
        private static readonly Dictionary<Texture2D, Color> _texColor = new Dictionary<Texture2D, Color>();
        private static Texture2D _gradientV, _gradientBg, _vignette, _glow, _streak;

        // ------------------------------------------------------------------ animation clocks

        /// <summary>Call when a page/screen opens; drives its entrance animation.</summary>
        public static void MarkOpen(string key) => _openAt[key] = Time.unscaledTime;

        /// <summary>0..1 eased progress since MarkOpen(key), with an optional delay (for staggered entrances).</summary>
        public static float Ease(string key, float duration = 0.3f, float delay = 0f)
        {
            if (!_openAt.TryGetValue(key, out float at)) return 1f;
            float t = Mathf.Clamp01((Time.unscaledTime - at - delay) / Mathf.Max(0.01f, duration));
            return 1f - (1f - t) * (1f - t) * (1f - t);   // ease-out cubic
        }

        /// <summary>Scales GUI to the design canvas with an optional vertical slide; returns the previous matrix to restore afterwards.</summary>
        public static Matrix4x4 BeginCanvas(float slideY = 0f)
        {
            Ensure();
            var saved = GUI.matrix;
            float sx = Screen.width / DesignW, sy = Screen.height / DesignH;
            GUI.matrix = Matrix4x4.TRS(new Vector3(0f, slideY * sy, 0f), Quaternion.identity, new Vector3(sx, sy, 1f));
            return saved;
        }

        // ------------------------------------------------------------------ background

        /// <summary>Full-screen backdrop: navy gradient, slow diagonal light streaks, a warm glow in one corner, vignette.</summary>
        public static void DrawBackground(float open = 1f)
        {
            Ensure();
            var saved = GUI.color;
            var m = GUI.matrix;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0, 0, DesignW, DesignH), _gradientBg);
            float t = Time.unscaledTime;
            // Three soft streaks drifting slowly across the page; the entrance slides them in from the right.
            for (int i = 0; i < 3; i++)
            {
                float phase = (t * (0.012f + 0.005f * i) + i * 0.37f) % 1f;
                float x = -700f + phase * (DesignW + 1400f) + (1f - open) * 300f;
                GUI.color = new Color(1f, 1f, 1f, 0.035f + 0.012f * i);
                GUIUtility.RotateAroundPivot(-18f, new Vector2(x + 260f, DesignH / 2f));
                GUI.DrawTexture(new Rect(x, -400f, 520f - i * 120f, DesignH + 800f), _streak);
                GUI.matrix = m;
            }
            // Warm glow top-left, cool glow bottom-right.
            GUI.color = new Color(1f, 0.8f, 0.4f, 0.10f);
            GUI.DrawTexture(new Rect(-500f, -500f, 1300f, 1300f), _glow);
            GUI.color = new Color(0.35f, 0.6f, 1f, 0.08f);
            GUI.DrawTexture(new Rect(DesignW - 900f, DesignH - 900f, 1400f, 1400f), _glow);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0, 0, DesignW, DesignH), _vignette);
            GUI.color = saved;
        }

        // ------------------------------------------------------------------ drawing helpers

        /// <summary>
        /// Rounded filled rectangle. Large panel-coloured rectangles are drawn as cards: soft shadow, a top-lit gradient
        /// and a hairline edge. Small ones (bars, stripes, badges) stay flat.
        /// </summary>
        public static void Box(Rect r, Color c, float radius = 12f)
        {
            bool card = (Same(c, PanelColor) || Same(c, PanelLightColor) || Same(c, PanelHoverColor)) && r.width >= 160f && r.height >= 44f;
            if (card) { Card(r, c, radius); return; }
            GUI.DrawTexture(r, White, ScaleMode.StretchToFill, true, 0f, c * GUI.color, 0f, radius);
        }

        private static bool Same(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

        /// <summary>A card: two-layer shadow, gradient body (lighter at the top), hairline edge.</summary>
        public static void Card(Rect r, Color c, float radius = 14f)
        {
            var g = GUI.color;
            GUI.DrawTexture(new Rect(r.x + 4f, r.y + 10f, r.width, r.height), White, ScaleMode.StretchToFill, true, 0f, new Color(0f, 0f, 0f, 0.28f) * g, 0f, radius + 4f);
            GUI.DrawTexture(new Rect(r.x + 1f, r.y + 3f, r.width, r.height), White, ScaleMode.StretchToFill, true, 0f, new Color(0f, 0f, 0f, 0.22f) * g, 0f, radius + 2f);
            GUI.DrawTexture(r, _gradientV, ScaleMode.StretchToFill, true, 0f, c * g, 0f, radius);
            GUI.DrawTexture(r, White, ScaleMode.StretchToFill, true, 0f, new Color(1f, 1f, 1f, 0.07f) * g, 1f, radius);
        }

        /// <summary>Rounded filled rectangle using one of the solid textures (Panel, PanelLight, ...).</summary>
        public static void Box(Rect r, Texture2D tex, float radius = 12f)
        {
            Box(r, _texColor.TryGetValue(tex, out var c) ? c : Color.white, radius);
        }

        /// <summary>Rounded outline.</summary>
        public static void Outline(Rect r, Color c, float width = 2f, float radius = 12f)
        {
            GUI.DrawTexture(r, White, ScaleMode.StretchToFill, true, 0f, c * GUI.color, width, radius);
        }

        /// <summary>A soft radial glow centred on the rect, tinted.</summary>
        public static void Glow(Rect r, Color c, float spread = 1.6f)
        {
            Ensure();
            float w = r.width * spread, h = r.height * spread;
            var saved = GUI.color;
            GUI.color = c * saved;
            GUI.DrawTexture(new Rect(r.center.x - w / 2f, r.center.y - h / 2f, w, h), _glow);
            GUI.color = saved;
        }

        /// <summary>Progress bar: dark track, coloured fill with a bright tip and a glow.</summary>
        public static void Bar(Rect r, float frac, Color color)
        {
            frac = Mathf.Clamp01(frac);
            Box(r, new Color(0.16f, 0.20f, 0.27f), r.height / 2f);
            if (frac <= 0.005f) return;
            var fill = new Rect(r.x, r.y, r.width * frac, r.height);
            Glow(new Rect(fill.xMax - r.height, r.y - r.height, r.height * 2f, r.height * 3f), new Color(color.r, color.g, color.b, 0.55f), 2.2f);
            GUI.DrawTexture(fill, _gradientV, ScaleMode.StretchToFill, true, 0f, color * GUI.color, 0f, r.height / 2f);
            GUI.DrawTexture(new Rect(fill.xMax - 3f, r.y - 4f, 6f, r.height + 8f), White, ScaleMode.StretchToFill, true, 0f, new Color(1f, 1f, 1f, 0.9f) * GUI.color, 0f, 3f);
        }

        private static readonly Dictionary<int, float> _hover = new Dictionary<int, float>();

        /// <summary>Rounded button with an animated hover lift, glow and highlight. Styles map to colours; disabled buttons dim.</summary>
        public static bool Btn(Rect r, string text, GUIStyle style, float radius = 10f)
        {
            bool enabled = GUI.enabled;
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            int key = (Mathf.RoundToInt(r.x) * 73856093) ^ (Mathf.RoundToInt(r.y) * 19349663) ^ (Mathf.RoundToInt(r.width) * 83492791) ^ (text ?? "").GetHashCode();
            _hover.TryGetValue(key, out float amt);
            if (Event.current.type == EventType.Repaint) { amt = Mathf.MoveTowards(amt, hover ? 1f : 0f, Time.unscaledDeltaTime * 7f); _hover[key] = amt; }
            bool gold = style == BigButton;
            Color bg = gold ? GoldColor : style == ToggleButtonOn ? GoldDimColor : PanelLightColor;
            bg = Color.Lerp(bg, Color.white, (gold ? 0.10f : 0.16f) * amt);
            if (!enabled) bg.a *= 0.45f;
            float lift = 2.5f * amt;
            Rect rr = new Rect(r.x - lift, r.y - lift, r.width + lift * 2f, r.height + lift * 2f);
            var g = GUI.color;
            if (enabled)
            {
                Color glowC = gold ? new Color(1f, 0.85f, 0.4f, 0.35f + 0.25f * amt) : new Color(0.5f, 0.7f, 1f, 0.28f * amt);
                if (gold || amt > 0.01f) Glow(rr, glowC, 1.35f + 0.25f * amt);
            }
            GUI.DrawTexture(new Rect(rr.x + 1f, rr.y + 3f, rr.width, rr.height), White, ScaleMode.StretchToFill, true, 0f, new Color(0f, 0f, 0f, 0.25f) * g, 0f, radius + 2f);
            GUI.DrawTexture(rr, _gradientV, ScaleMode.StretchToFill, true, 0f, bg * g, 0f, radius);
            GUI.DrawTexture(rr, White, ScaleMode.StretchToFill, true, 0f, new Color(1f, 1f, 1f, gold ? 0.35f : 0.10f + 0.25f * amt) * g, 1f, radius);
            var ls = LabelFor(style);
            if (!enabled) { var dim = new GUIStyle(ls); dim.normal.textColor = new Color(ls.normal.textColor.r, ls.normal.textColor.g, ls.normal.textColor.b, 0.5f); ls = dim; }
            GUI.Label(rr, text, ls);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        private static readonly Dictionary<GUIStyle, GUIStyle> _labelFor = new Dictionary<GUIStyle, GUIStyle>();
        private static GUIStyle LabelFor(GUIStyle button)
        {
            if (_labelFor.TryGetValue(button, out var l)) return l;
            l = new GUIStyle(GUI.skin.label) { fontSize = button.fontSize, fontStyle = button.fontStyle, alignment = TextAnchor.MiddleCenter };
            l.normal.textColor = button.normal.textColor;
            _labelFor[button] = l;
            return l;
        }

        /// <summary>Text with a soft dark shadow under it (for big titles over busy backgrounds).</summary>
        public static void Shadowed(Rect r, string text, GUIStyle style)
        {
            var g = GUI.color;
            var dark = new GUIStyle(style); dark.normal.textColor = new Color(0f, 0f, 0f, 0.55f);
            GUI.Label(new Rect(r.x + 2f, r.y + 3f, r.width, r.height), text, dark);
            GUI.Label(r, text, style);
            GUI.color = g;
        }

        /// <summary>A thin gold rule with a soft glow, for headers.</summary>
        public static void Rule(float x, float y, float w)
        {
            Glow(new Rect(x, y - 6f, w, 12f), new Color(1f, 0.85f, 0.4f, 0.25f), 1.1f);
            Box(new Rect(x, y, w, 2f), GoldColor, 1f);
        }

        public static void Ensure()
        {
            if (_ready) return;
            _ready = true;
            Bg = Solid(BgBottom);
            Panel = Solid(PanelColor);
            PanelLight = Solid(PanelLightColor);
            PanelHover = Solid(PanelHoverColor);
            Gold = Solid(GoldColor);
            GoldDim = Solid(GoldDimColor);
            BarBg = Solid(new Color(0.22f, 0.26f, 0.32f));
            White = Solid(Color.white);
            Green = Solid(GreenColor);
            Red = Solid(RedColor);
            _texColor[Bg] = BgBottom; _texColor[Panel] = PanelColor; _texColor[PanelLight] = PanelLightColor;
            _texColor[PanelHover] = PanelHoverColor; _texColor[Gold] = GoldColor; _texColor[GoldDim] = GoldDimColor;
            _texColor[BarBg] = new Color(0.22f, 0.26f, 0.32f); _texColor[White] = Color.white; _texColor[Green] = GreenColor; _texColor[Red] = RedColor;

            _gradientV = Gradient(64, y => Color.Lerp(new Color(1f, 1f, 1f, 1f), new Color(0.78f, 0.80f, 0.85f, 1f), y));
            _gradientBg = Gradient(128, y => Color.Lerp(BgTop, BgBottom, Mathf.Pow(y, 0.8f)));
            _vignette = Radial(256, d => new Color(0f, 0f, 0f, Mathf.Clamp01((d - 0.55f) / 0.55f) * 0.55f));
            _glow = Radial(256, d => new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f)));
            _streak = Gradient(64, y => Color.white, horizontal: true, alphaCurve: x => Mathf.Pow(Mathf.Sin(x * Mathf.PI), 2f));

            Tab = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            Tab.normal.textColor = Muted; Tab.hover.textColor = Color.white;
            TabOn = new GUIStyle(Tab); TabOn.normal.textColor = Color.white; TabOn.hover.textColor = Color.white;
            Title = Label(42, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            H1 = Label(28, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            H1Center = Label(28, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            H2 = Label(22, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            Body = Label(20, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white, wrap: true);
            BodyCenter = Label(20, FontStyle.Normal, TextAnchor.MiddleCenter, Color.white, wrap: true);
            Small = Label(16, FontStyle.Normal, TextAnchor.MiddleLeft, Muted, wrap: true);
            SmallCenter = Label(16, FontStyle.Normal, TextAnchor.MiddleCenter, Muted, wrap: true);
            SmallRight = Label(16, FontStyle.Normal, TextAnchor.MiddleRight, Muted);
            Stat = Label(40, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            StatLabel = new GUIStyle(Small);
            GoldText = Label(20, FontStyle.Normal, TextAnchor.MiddleLeft, GoldColor, wrap: true);
            GreenText = Label(20, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.45f, 0.9f, 0.55f));

            var dark = new Color(0.08f, 0.08f, 0.1f);
            BigButton = new GUIStyle(GUI.skin.button) { fontSize = 26, fontStyle = FontStyle.Bold };
            BigButton.normal.textColor = dark;
            Button = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
            Button.normal.textColor = Color.white;
            ToggleButton = new GUIStyle(Button) { fontSize = 20 };
            ToggleButtonOn = new GUIStyle(ToggleButton);
            ToggleButtonOn.normal.textColor = Color.white;
        }

        private static GUIStyle Label(int size, FontStyle style, TextAnchor anchor, Color color, bool wrap = false)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = style, alignment = anchor, wordWrap = wrap };
            s.normal.textColor = color;
            return s;
        }

        public static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        /// <summary>A 1-D gradient texture (vertical by default; top of the texture is the top on screen).</summary>
        private static Texture2D Gradient(int n, System.Func<float, Color> f, bool horizontal = false, System.Func<float, float> alphaCurve = null)
        {
            var t = new Texture2D(horizontal ? n : 1, horizontal ? 1 : n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)(n - 1);
                var c = f(u);
                if (alphaCurve != null) c.a *= alphaCurve(u);
                if (horizontal) t.SetPixel(i, 0, c);
                else t.SetPixel(0, n - 1 - i, c);   // i=0 is the top on screen
            }
            t.Apply();
            return t;
        }

        /// <summary>A square texture coloured by distance from the centre (0 centre .. 1 edge).</summary>
        private static Texture2D Radial(int n, System.Func<float, Color> f)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            float c = (n - 1) / 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = f(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c);
            t.SetPixels(px); t.Apply();
            return t;
        }

        /// <summary>Draws a rank emblem with its numeral over a radial glow in the tier's colour; optional caption and name below.</summary>
        public static void Emblem(float centerX, float top, float size, int tier, string name = null, string caption = null, bool dim = false, bool glow = false)
        {
            Ensure();
            var tex = RankEmblems.Get(tier);
            var r = new Rect(centerX - size / 2, top, size, size);
            var saved = GUI.color;
            var tint = RankEmblems.ColorFor(tier);
            if (glow)
            {
                float pulse = 0.55f + 0.2f * Mathf.Sin(Time.unscaledTime * 2.2f);
                Glow(r, new Color(tint.r, tint.g, tint.b, pulse * saved.a), 2.4f);
                Glow(r, new Color(1f, 0.92f, 0.6f, 0.35f * saved.a), 1.5f);
            }
            else if (size >= 60f)
            {
                Glow(r, new Color(tint.r, tint.g, tint.b, (dim ? 0.18f : 0.32f) * saved.a), 1.6f);
            }
            GUI.color = (dim ? new Color(1f, 1f, 1f, 0.55f) : Color.white) * saved;
            GUI.DrawTexture(r, tex);
            var numeral = new GUIStyle(H1Center) { fontSize = Mathf.Max(10, Mathf.RoundToInt(size * 0.28f)) };
            numeral.normal.textColor = new Color(1f, 0.95f, 0.8f);
            GUI.Label(new Rect(r.x, r.y + size * 0.30f, size, size * 0.3f), RankEmblems.Numeral(tier), numeral);
            GUI.color = saved;
            if (caption != null) GUI.Label(new Rect(centerX - 220, top + size + 8, 440, 30), caption, SmallCenter);
            if (name != null) GUI.Label(new Rect(centerX - 220, top + size + 36, 440, 40), name.ToUpperInvariant(), dim ? BodyCenter : H1Center);
        }
    }
}
