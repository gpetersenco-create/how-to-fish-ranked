using System.Collections.Generic;
using HarmonyLib;
using HowToFish1v1.Match;
using UnityEngine;
using S = HowToFish1v1.UI.RankedStyles;

namespace HowToFish1v1.UI
{
    /// <summary>
    /// Feel: a red direction indicator toward whoever hit you, a red vignette when low, a camera kick on damage, a
    /// custom crosshair, and a custom hit marker (in place of the game's while a style is chosen).
    /// </summary>
    public static class HitReactions
    {
        private sealed class Indicator { public Vector3 From; public float At; }
        private static readonly List<Indicator> _indicators = new List<Indicator>();
        private static float _shake;
        private static float _markerAt = -10f;
        private static bool _markerKill;
        private static Texture2D _vignette;
        private static readonly string[] CrosshairNames = { "Game default", "Dot", "Cross", "Circle", "Chevron", "T" };
        private static readonly string[] MarkerNames = { "Game default", "X", "Plus", "Box", "Circle" };
        public static IReadOnlyList<string> Crosshairs => CrosshairNames;
        public static IReadOnlyList<string> Markers => MarkerNames;

        /// <summary>The local player took a hit from a direction (or from a player).</summary>
        public static void Hit(Vector3 from, int damage)
        {
            _indicators.Add(new Indicator { From = from, At = Time.unscaledTime });
            if (_indicators.Count > 6) _indicators.RemoveAt(0);
            _shake = Mathf.Min(1f, _shake + Mathf.Clamp(damage / 60f, 0.25f, 0.8f));
        }

        /// <summary>Our hit marker (called where the game's would play its sound).</summary>
        public static void Marker(bool kill)
        {
            _markerAt = Time.unscaledTime; _markerKill = kill;
        }

        /// <summary>Camera kick, applied after the game positioned the camera.</summary>
        public static void ApplyShake(PlayerCamera cam)
        {
            if (_shake <= 0.001f || !cam || !cam.Cam) return;
            float dt = Time.unscaledDeltaTime;
            float a = _shake * 0.035f;
            cam.Cam.transform.position += new Vector3(Mathf.PerlinNoise(Time.unscaledTime * 40f, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, Time.unscaledTime * 43f) - 0.5f, 0f) * a * 2f;
            cam.Cam.transform.rotation *= Quaternion.Euler((Mathf.PerlinNoise(Time.unscaledTime * 37f, 0.9f) - 0.5f) * _shake * 2.2f, 0f, (Mathf.PerlinNoise(0.2f, Time.unscaledTime * 35f) - 0.5f) * _shake * 2.5f);
            _shake = Mathf.MoveTowards(_shake, 0f, dt * 3.2f);
        }

        public static void Draw()
        {
            var me = Player.LocalPlayer;
            if (!ModState.IsActive || !me || ModState.PanelOpen || MainMenuManager.IsInMenu || Results.Visible) return;
            bool alive = !me.Dying.IsDead;
            var cam = me.CamObject ? me.CamObject : me.Transform;
            float w = Screen.width, h = Screen.height, cx = w / 2f, cy = h / 2f;

            if (alive && !KillCam.Active)
            {
                // Low-health vignette.
                int hp = 100;
                try { hp = me.Vitals.Health; } catch (System.Exception) { }
                if (hp < 40)
                {
                    if (!_vignette) _vignette = Radial(256, d => new Color(0.7f, 0f, 0f, Mathf.Clamp01((d - 0.35f) / 0.65f)));
                    float a = Mathf.Lerp(0.15f, 0.7f, 1f - hp / 40f) * (0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 6f));
                    GUI.color = new Color(1f, 1f, 1f, a);
                    GUI.DrawTexture(new Rect(0, 0, w, h), _vignette);
                    GUI.color = Color.white;
                }
                // Damage direction: an arc segment around the centre pointing at the attacker.
                for (int i = _indicators.Count - 1; i >= 0; i--)
                {
                    var ind = _indicators[i];
                    float age = Time.unscaledTime - ind.At;
                    if (age > 0.9f) { _indicators.RemoveAt(i); continue; }
                    Vector3 to = ind.From - cam.position;
                    Vector3 flat = Vector3.ProjectOnPlane(to, Vector3.up);
                    if (flat.sqrMagnitude < 0.01f) continue;
                    float ang = Vector3.SignedAngle(Vector3.ProjectOnPlane(cam.forward, Vector3.up), flat, Vector3.up);
                    float alpha = 1f - age / 0.9f;
                    var m = GUI.matrix;
                    GUIUtility.RotateAroundPivot(ang, new Vector2(cx, cy));
                    GUI.color = new Color(1f, 0.15f, 0.1f, 0.85f * alpha);
                    float r = 150f * (h / 1080f);
                    S.Box(new Rect(cx - 55f, cy - r - 14f, 110f, 14f), new Color(1f, 0.15f, 0.1f, 0.85f * alpha), 7f);
                    GUI.matrix = m;
                    GUI.color = Color.white;
                }
                // Crosshair.
                int style = Mathf.Clamp(Plugin.Cfg.Crosshair.Value, 0, CrosshairNames.Length - 1);
                bool ads = me.Holding && me.Holding.HeldItem is Weapon wpn && wpn.IsAds;
                if (style > 0 && !ads && !Scoreboard.Visible)
                {
                    Color c = Color.white;
                    ColorUtility.TryParseHtmlString(Plugin.Cfg.CrosshairColor.Value, out c);
                    float size = Mathf.Clamp(Plugin.Cfg.CrosshairSize.Value, 4f, 60f) * (h / 1080f);
                    DrawCrosshair(style, cx, cy, size, c);
                }
            }
            // Hit marker.
            float mAge = Time.unscaledTime - _markerAt;
            int ms = Mathf.Clamp(Plugin.Cfg.HitmarkerStyle.Value, 0, MarkerNames.Length - 1);
            if (ms > 0 && mAge < 0.25f)
            {
                float k = 1f - mAge / 0.25f;
                Color c = _markerKill ? new Color(1f, 0.25f, 0.2f, k) : new Color(1f, 1f, 1f, k);
                float size = (18f + 10f * (1f - k)) * (h / 1080f);
                DrawMarker(ms, cx, cy, size, c);
            }
        }

        private static void Line(float x, float y, float len, float th, float angle, Color c)
        {
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, new Vector2(x, y));
            GUI.color = c;
            GUI.DrawTexture(new Rect(x, y - th / 2f, len, th), Texture2D.whiteTexture);
            GUI.matrix = m;
            GUI.color = Color.white;
        }

        private static void DrawCrosshair(int style, float cx, float cy, float size, Color c)
        {
            float th = Mathf.Max(2f, size * 0.12f), gap = size * 0.35f;
            switch (style)
            {
                case 1: S.Box(new Rect(cx - th, cy - th, th * 2f, th * 2f), c, th); break;
                case 2:
                    Line(cx + gap, cy, size, th, 0f, c); Line(cx - gap, cy, size, th, 180f, c);
                    Line(cx, cy + gap, size, th, 90f, c); Line(cx, cy - gap, size, th, -90f, c); break;
                case 3:
                    S.Outline(new Rect(cx - size, cy - size, size * 2f, size * 2f), c, th, size);
                    S.Box(new Rect(cx - th * 0.6f, cy - th * 0.6f, th * 1.2f, th * 1.2f), c, th); break;
                case 4:
                    Line(cx, cy + gap * 0.4f, size, th, 45f, c); Line(cx, cy + gap * 0.4f, size, th, 135f, c); break;
                case 5:
                    Line(cx + gap, cy, size, th, 0f, c); Line(cx - gap, cy, size, th, 180f, c); Line(cx, cy + gap, size, th, 90f, c); break;
            }
        }

        private static void DrawMarker(int style, float cx, float cy, float size, Color c)
        {
            float th = Mathf.Max(2f, size * 0.14f), gap = size * 0.3f;
            switch (style)
            {
                case 1: foreach (float a in new[] { 45f, 135f, 225f, 315f }) Line(cx + Mathf.Cos(a * Mathf.Deg2Rad) * gap, cy + Mathf.Sin(a * Mathf.Deg2Rad) * gap, size, th, a, c); break;
                case 2: foreach (float a in new[] { 0f, 90f, 180f, 270f }) Line(cx + Mathf.Cos(a * Mathf.Deg2Rad) * gap, cy + Mathf.Sin(a * Mathf.Deg2Rad) * gap, size, th, a, c); break;
                case 3: S.Outline(new Rect(cx - size, cy - size, size * 2f, size * 2f), c, th, 2f); break;
                case 4: S.Outline(new Rect(cx - size, cy - size, size * 2f, size * 2f), c, th, size); break;
            }
        }

        private static Texture2D Radial(int n, System.Func<float, Color> f)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            float c = (n - 1) / 2f;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) px[y * n + x] = f(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c);
            t.SetPixels(px); t.Apply();
            return t;
        }
    }

    [HarmonyPatch]
    internal static class HitReactionPatches
    {
        // The victim's client hears about every hit through this observer RPC, with the attacker and the hit point.
        [HarmonyPatch(typeof(PlayerVitals), "RpcLogic___ObserverHit___2388800966")]
        [HarmonyPostfix]
        private static void OnObserverHit(PlayerVitals __instance, Player __0, Vector3 __1, Vector3 __2, int __3)
        {
            if (!ModState.IsActive) return;
            try
            {
                var victim = Traverse.Create(__instance).Field<Player>("_player").Value;
                if (!victim || victim.Owner == null || !victim.Owner.IsLocalClient) return;
                Vector3 from = __0 && __0 != victim && __0.Transform ? __0.Transform.position + Vector3.up : __1 - __2 * 5f;
                HitReactions.Hit(from, __3);
            }
            catch (System.Exception) { }
        }

        // The game's world-space hit marker steps aside while a custom style is chosen.
        [HarmonyPatch(typeof(HitmarkerManager), nameof(HitmarkerManager.AddHitMarker))]
        [HarmonyPrefix]
        private static bool CustomMarker(bool kill)
        {
            if (!ModState.IsActive || Plugin.Cfg.HitmarkerStyle.Value <= 0) return true;
            HitReactions.Marker(kill);
            return false;
        }
    }
}
