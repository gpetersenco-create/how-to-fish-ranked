using HowToFish1v1.Core;
using HowToFish1v1.Match;
using TMPro;
using UnityEngine;

namespace HowToFish1v1.UI
{
    /// <summary>Scoreboard (top center) and banner (center) using the game's own canvas text prefab.</summary>
    public static class Hud
    {
        private static TextMeshProUGUI _score;
        private static TextMeshProUGUI _banner;
        private static float _liveAt = -10f;
        private static MatchPhase _lastPhase;

        public static void Update()
        {
            bool show = ModState.IsActive && ClientMatchView.HasState && Player.LocalPlayer;
            if (!show)
            {
                if (_score) _score.gameObject.SetActive(false);
                if (_banner) _banner.gameObject.SetActive(false);
                return;
            }
            if (!EnsureCreated()) return;
            _score.gameObject.SetActive(true);
            _banner.gameObject.SetActive(true);

            var me = ClientMatchView.Me;
            var them = ClientMatchView.Them;
            string themName = them.Present ? them.Name : "---";
            _score.text = $"YOU  {me.Score}  -  {them.Score}  {themName}";

            var s = ClientMatchView.Latest;
            double left = ClientMatchView.SecondsLeftInPhase;
            TrackLive();
            switch (ModState.Phase)
            {
                case MatchPhase.Lobby:
                    _banner.text = s.StatusText ?? "";
                    break;
                case MatchPhase.Countdown:
                    int n = (int)System.Math.Ceiling(left);
                    _banner.text = n <= 0 ? "FIGHT" : $"Round {s.Round}\n{n}";
                    break;
                case MatchPhase.Live:
                    _banner.text = (Time.unscaledTime - _liveAt < 1f) ? "FIGHT" : "";
                    break;
                case MatchPhase.RoundEnd:
                    _banner.text = s.StatusText ?? "";
                    break;
                case MatchPhase.MatchEnd:
                    _banner.text = (s.StatusText ?? "") + "\n" + $"{s.AName} {s.AScore} - {s.BScore} {s.BName}";
                    break;
                default:
                    _banner.text = "";
                    break;
            }
        }

        /// <summary>Remembers when the phase became Live so the banner can flash FIGHT for one second.</summary>
        private static void TrackLive()
        {
            if (ModState.Phase == MatchPhase.Live && _lastPhase != MatchPhase.Live) _liveAt = Time.unscaledTime;
            _lastPhase = ModState.Phase;
        }

        private static bool EnsureCreated()
        {
            if (_score && _banner) return true;
            var prefab = PlayerUI.CanvasTextPrefab;
            var parent = PlayerUI.FXCanvasTrans;
            if (!prefab || !parent) return false;

            _score = Make(prefab, parent, "HTF1v1_Score", new Vector2(0.5f, 1f), new Vector2(0f, -40f), 36f);
            _banner = Make(prefab, parent, "HTF1v1_Banner", new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), 64f);
            return true;
        }

        private static TextMeshProUGUI Make(TextMeshProUGUI prefab, Transform parent, string name, Vector2 anchor, Vector2 offset, float size)
        {
            var t = Object.Instantiate(prefab, parent);
            t.name = name;
            t.transform.localScale = Vector3.one;
            t.transform.localRotation = Quaternion.identity;
            var rt = t.rectTransform;
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, anchor.y);
            rt.anchoredPosition = offset;
            rt.sizeDelta = new Vector2(900f, 200f);
            t.alignment = TextAlignmentOptions.Center;
            t.fontSize = size;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.text = "";
            t.color = Color.white;
            t.outlineWidth = 0.2f;
            t.outlineColor = Color.black;
            return t;
        }
    }
}
