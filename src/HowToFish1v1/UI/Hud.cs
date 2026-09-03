using System.Linq;
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
        private static TextMeshProUGUI _feed;
        private static float _liveAt = -10f;
        private static MatchPhase _lastPhase;

        private const float FeedSeconds = 7f;
        private const int FeedMax = 5;
        private static readonly System.Collections.Generic.List<(string text, float at)> _feedLines = new System.Collections.Generic.List<(string, float)>();

        /// <summary>Called once; listens for host kill announcements.</summary>
        public static void Init()
        {
            Net.ModNet.KillFeedReceived += k =>
            {
                string me = Player.LocalPlayer ? Player.LocalPlayer.SteamName : null;
                string killer = k.Killer == me ? "<color=#F5C740>YOU</color>" : k.Killer;
                string victim = k.Victim == me ? "<color=#F5C740>YOU</color>" : k.Victim;
                string line = k.Suicide ? $"{victim}  died" : $"{killer}  <color=#FF6A5A>killed</color>  {victim}";
                _feedLines.Insert(0, (line, Time.unscaledTime));
                while (_feedLines.Count > FeedMax) _feedLines.RemoveAt(_feedLines.Count - 1);
            };
        }

        public static void Update()
        {
            bool show = ModState.IsActive && ClientMatchView.HasState && Player.LocalPlayer && !ModState.PanelOpen;
            if (!show)
            {
                if (_score) _score.gameObject.SetActive(false);
                if (_banner) _banner.gameObject.SetActive(false);
                if (_feed) _feed.gameObject.SetActive(false);
                return;
            }
            if (!EnsureCreated()) return;
            _score.gameObject.SetActive(true);
            _banner.gameObject.SetActive(true);
            _feed.gameObject.SetActive(true);
            _feedLines.RemoveAll(l => Time.unscaledTime - l.at > FeedSeconds);
            _feed.text = string.Join("\n", _feedLines.Select(l => l.text));

            var s = ClientMatchView.Latest;
            var me = ClientMatchView.Me;
            if (ClientMatchView.IsFfa)
            {
                var leader = ClientMatchView.Players.OrderByDescending(p => p.Kills).FirstOrDefault();
                int myKills = me?.Kills ?? 0;
                _score.text = leader.Name == null ? "" : $"YOU {myKills} kills   |   leader {leader.Name} {leader.Kills}   |   first to {s.KillsToWin}";
            }
            else
            {
                int myTeam = me?.Team ?? 0;
                int mine = myTeam == 0 ? s.TeamScoreA : s.TeamScoreB;
                int theirs = myTeam == 0 ? s.TeamScoreB : s.TeamScoreA;
                string them = ClientMatchView.TeamLabel(1 - myTeam);
                _score.text = $"YOU  {mine}  -  {theirs}  {them}";
            }

            double left = ClientMatchView.SecondsLeftInPhase;
            TrackLive();
            if (KillCam.Active)
            {
                string head = KillCam.IsFinal ? "FINAL KILLCAM" : (KillCam.IsReplay ? "KILLCAM" : "KILLED BY");
                string who = KillCam.IsFinal ? $"{KillCam.KillerName}  killed  {KillCam.VictimName}" : KillCam.KillerName;
                _banner.text = $"<size=60%>{head}</size>\n{who}\n<size=45%>{KillCam.KillerInfo}</size>";
                return;
            }
            switch (ModState.Phase)
            {
                case MatchPhase.Lobby:
                    _banner.text = ModState.PanelOpen ? "" : (s.StatusText ?? "") + "\nPress F5 to open the lobby";
                    break;
                case MatchPhase.Countdown:
                    int n = (int)System.Math.Ceiling(left);
                    _banner.text = n <= 0 ? "FIGHT" : (ClientMatchView.IsFfa ? $"{n}" : $"Round {s.Round}\n{n}");
                    break;
                case MatchPhase.Live:
                    _banner.text = (Time.unscaledTime - _liveAt < 1f) ? "FIGHT" : (ClientMatchView.IsFfa && me != null && DeadNow() ? "Respawning..." : "");
                    break;
                case MatchPhase.RoundEnd:
                    _banner.text = s.StatusText ?? "";
                    break;
                case MatchPhase.MatchEnd:
                    _banner.text = (s.StatusText ?? "") + "\n" + RankService.LastResultText;
                    break;
                default:
                    _banner.text = "";
                    break;
            }
        }

        private static bool DeadNow() => Player.LocalPlayer && Player.LocalPlayer.Dying.IsDead;

        /// <summary>Remembers when the phase became Live so the banner can flash FIGHT for one second.</summary>
        private static void TrackLive()
        {
            if (ModState.Phase == MatchPhase.Live && _lastPhase != MatchPhase.Live) _liveAt = Time.unscaledTime;
            _lastPhase = ModState.Phase;
        }

        private static bool EnsureCreated()
        {
            if (_score && _banner && _feed) return true;
            var prefab = PlayerUI.CanvasTextPrefab;
            var parent = PlayerUI.FXCanvasTrans;
            if (!prefab || !parent) return false;

            _score = Make(prefab, parent, "HTF1v1_Score", new Vector2(0.5f, 1f), new Vector2(0f, -40f), 36f);
            _banner = Make(prefab, parent, "HTF1v1_Banner", new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), 64f);
            _feed = Make(prefab, parent, "HTF1v1_KillFeed", new Vector2(0f, 1f), new Vector2(30f, -30f), 26f);
            _feed.alignment = TextAlignmentOptions.TopLeft;
            _feed.rectTransform.pivot = new Vector2(0f, 1f);
            _feed.rectTransform.sizeDelta = new Vector2(700f, 260f);
            _feed.richText = true;
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
            rt.sizeDelta = new Vector2(1100f, 200f);
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
