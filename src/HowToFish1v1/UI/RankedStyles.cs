using UnityEngine;

namespace HowToFish1v1.UI
{
    /// <summary>Shared look for the full-screen ranked pages: a 1920x1080 design canvas, dark navy panels, gold accents.</summary>
    internal static class RankedStyles
    {
        public const float DesignW = 1920f, DesignH = 1080f;

        public static Texture2D Bg, Panel, PanelLight, PanelHover, Gold, GoldDim, BarBg, White, Green, Red;
        public static GUIStyle Tab, TabOn, Title, H1, H1Center, H2, Body, BodyCenter, Small, SmallCenter, SmallRight,
            Stat, StatLabel, GoldText, GreenText, BigButton, Button, ToggleButton, ToggleButtonOn;

        public static readonly Color GoldColor = new Color(0.95f, 0.78f, 0.25f);
        public static readonly Color Muted = new Color(0.72f, 0.77f, 0.84f);

        private static bool _ready;

        /// <summary>Scales GUI to the design canvas; returns the previous matrix to restore afterwards.</summary>
        public static Matrix4x4 BeginCanvas()
        {
            Ensure();
            var saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / DesignW, Screen.height / DesignH, 1f));
            return saved;
        }

        public static void Ensure()
        {
            if (_ready) return;
            _ready = true;
            Bg = Solid(new Color(0.06f, 0.09f, 0.13f, 1f));
            Panel = Solid(new Color(0.09f, 0.13f, 0.19f, 0.95f));
            PanelLight = Solid(new Color(0.14f, 0.20f, 0.28f, 0.95f));
            PanelHover = Solid(new Color(0.22f, 0.30f, 0.40f));
            Gold = Solid(GoldColor);
            GoldDim = Solid(new Color(0.55f, 0.46f, 0.18f));
            BarBg = Solid(new Color(0.22f, 0.26f, 0.32f));
            White = Solid(Color.white);
            Green = Solid(new Color(0.25f, 0.70f, 0.40f));
            Red = Solid(new Color(0.70f, 0.25f, 0.25f));

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

            BigButton = new GUIStyle(GUI.skin.button) { fontSize = 26, fontStyle = FontStyle.Bold };
            var dark = new Color(0.08f, 0.08f, 0.1f);
            BigButton.normal.background = Gold; BigButton.hover.background = Solid(new Color(1f, 0.86f, 0.4f)); BigButton.active.background = Gold;
            BigButton.normal.textColor = dark; BigButton.hover.textColor = dark; BigButton.active.textColor = dark;
            Button = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
            Button.normal.background = PanelLight; Button.hover.background = PanelHover; Button.active.background = PanelLight;
            Button.normal.textColor = Color.white; Button.hover.textColor = Color.white; Button.active.textColor = Color.white;
            ToggleButton = new GUIStyle(Button) { fontSize = 20 };
            ToggleButtonOn = new GUIStyle(ToggleButton);
            ToggleButtonOn.normal.background = GoldDim; ToggleButtonOn.hover.background = Gold; ToggleButtonOn.active.background = GoldDim;
            ToggleButtonOn.normal.textColor = Color.white; ToggleButtonOn.hover.textColor = dark;
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

        /// <summary>Draws a rank emblem with its numeral; optional caption and name below.</summary>
        public static void Emblem(float centerX, float top, float size, int tier, string name = null, string caption = null, bool dim = false, bool glow = false)
        {
            var tex = RankEmblems.Get(tier);
            var r = new Rect(centerX - size / 2, top, size, size);
            if (glow)
            {
                GUI.color = new Color(1f, 0.9f, 0.5f, 0.18f);
                GUI.DrawTexture(new Rect(centerX - size * 0.62f, top - size * 0.12f, size * 1.24f, size * 1.24f), tex);
            }
            GUI.color = dim ? new Color(1f, 1f, 1f, 0.55f) : Color.white;
            GUI.DrawTexture(r, tex);
            var numeral = new GUIStyle(H1Center) { fontSize = Mathf.Max(10, Mathf.RoundToInt(size * 0.28f)) };
            numeral.normal.textColor = new Color(1f, 0.95f, 0.8f);
            GUI.Label(new Rect(r.x, r.y + size * 0.30f, size, size * 0.3f), RankEmblems.Numeral(tier), numeral);
            GUI.color = Color.white;
            if (caption != null) GUI.Label(new Rect(centerX - 220, top + size + 8, 440, 30), caption, SmallCenter);
            if (name != null) GUI.Label(new Rect(centerX - 220, top + size + 36, 440, 40), name.ToUpperInvariant(), dim ? BodyCenter : H1Center);
        }
    }
}
