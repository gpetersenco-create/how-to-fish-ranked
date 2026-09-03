using System.Collections.Generic;
using UnityEngine;

namespace HowToFish1v1.UI
{
    /// <summary>
    /// Shared look for the full-screen ranked pages: a 1920x1080 design canvas, dark navy rounded panels, gold accents,
    /// hover-reactive buttons, and small open/entrance animations driven by <see cref="Ease"/>.
    /// </summary>
    internal static class RankedStyles
    {
        public const float DesignW = 1920f, DesignH = 1080f;

        public static Texture2D Bg, Panel, PanelLight, PanelHover, Gold, GoldDim, BarBg, White, Green, Red;
        public static GUIStyle Tab, TabOn, Title, H1, H1Center, H2, Body, BodyCenter, Small, SmallCenter, SmallRight,
            Stat, StatLabel, GoldText, GreenText, BigButton, Button, ToggleButton, ToggleButtonOn;

        public static readonly Color GoldColor = new Color(0.95f, 0.78f, 0.25f);
        public static readonly Color Muted = new Color(0.72f, 0.77f, 0.84f);
        public static readonly Color PanelColor = new Color(0.09f, 0.13f, 0.19f, 0.95f);
        public static readonly Color PanelLightColor = new Color(0.14f, 0.20f, 0.28f, 0.95f);
        public static readonly Color PanelHoverColor = new Color(0.22f, 0.30f, 0.40f, 1f);
        public static readonly Color GoldDimColor = new Color(0.55f, 0.46f, 0.18f, 1f);
        public static readonly Color GreenColor = new Color(0.25f, 0.70f, 0.40f);
        public static readonly Color RedColor = new Color(0.70f, 0.25f, 0.25f);

        private static bool _ready;
        private static readonly Dictionary<string, float> _openAt = new Dictionary<string, float>();
        private static readonly Dictionary<Texture2D, Color> _texColor = new Dictionary<Texture2D, Color>();

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

        // ------------------------------------------------------------------ drawing helpers

        /// <summary>Rounded filled rectangle.</summary>
        public static void Box(Rect r, Color c, float radius = 12f)
        {
            GUI.DrawTexture(r, White, ScaleMode.StretchToFill, true, 0f, c * GUI.color, 0f, radius);
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

        /// <summary>Rounded button with hover lift and highlight. Styles map to colours; disabled buttons dim.</summary>
        public static bool Btn(Rect r, string text, GUIStyle style, float radius = 10f)
        {
            bool enabled = GUI.enabled;
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            Color bg = style == BigButton ? GoldColor : style == ToggleButtonOn ? GoldDimColor : PanelLightColor;
            if (hover) bg = Color.Lerp(bg, Color.white, style == BigButton ? 0.12f : 0.18f);
            if (!enabled) bg.a *= 0.45f;
            Rect rr = hover ? new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f) : r;
            Box(rr, bg, radius);
            if (hover) Outline(rr, new Color(1f, 1f, 1f, 0.25f), 1.5f, radius);
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

        public static void Ensure()
        {
            if (_ready) return;
            _ready = true;
            Bg = Solid(new Color(0.06f, 0.09f, 0.13f, 1f));
            Panel = Solid(PanelColor);
            PanelLight = Solid(PanelLightColor);
            PanelHover = Solid(PanelHoverColor);
            Gold = Solid(GoldColor);
            GoldDim = Solid(GoldDimColor);
            BarBg = Solid(new Color(0.22f, 0.26f, 0.32f));
            White = Solid(Color.white);
            Green = Solid(GreenColor);
            Red = Solid(RedColor);
            _texColor[Bg] = new Color(0.06f, 0.09f, 0.13f, 1f); _texColor[Panel] = PanelColor; _texColor[PanelLight] = PanelLightColor;
            _texColor[PanelHover] = PanelHoverColor; _texColor[Gold] = GoldColor; _texColor[GoldDim] = GoldDimColor;
            _texColor[BarBg] = new Color(0.22f, 0.26f, 0.32f); _texColor[White] = Color.white; _texColor[Green] = GreenColor; _texColor[Red] = RedColor;

            Tab = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            Tab.normal.textColor = Muted; Tab.hover.textColor = Color.white;
            TabOn = new GUIStyle(Tab); TabOn.normal.textColor = Color.white; TabOn.hover.textColor = Color.white;
            Title = Label(40, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
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

        /// <summary>Draws a rank emblem with its numeral; optional caption and name below. Glow pulses gently.</summary>
        public static void Emblem(float centerX, float top, float size, int tier, string name = null, string caption = null, bool dim = false, bool glow = false)
        {
            var tex = RankEmblems.Get(tier);
            var r = new Rect(centerX - size / 2, top, size, size);
            var saved = GUI.color;
            if (glow)
            {
                float pulse = 0.14f + 0.08f * Mathf.Sin(Time.unscaledTime * 2.2f);
                GUI.color = new Color(1f, 0.9f, 0.5f, pulse) * saved.a;
                float g = 1.24f + 0.06f * Mathf.Sin(Time.unscaledTime * 2.2f);
                GUI.DrawTexture(new Rect(centerX - size * g / 2f, top - size * (g - 1f) / 2f, size * g, size * g), tex);
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
