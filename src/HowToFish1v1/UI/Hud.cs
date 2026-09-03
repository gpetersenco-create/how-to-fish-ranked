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
        private static TextMeshProUGUI _corner;
        private static float _liveAt = -10f;
        private static MatchPhase _lastPhase;

        private const float FeedSeconds = 7f;
        private const int FeedMax = 5;
        private static readonly System.Collections.Generic.List<(string text, float at)> _feedLines = new System.Collections.Generic.List<(string, float)>();

        // CoD-style score popups: "+100" rises and fades next to the crosshair on your own kills.
        private static TextMeshProUGUI _popup;
        private static readonly System.Collections.Generic.Queue<string> _popupQueue = new System.Collections.Generic.Queue<string>();
        private static string _popupText = "";
        private static float _popupAt = -10f;
        private const float PopupSeconds = 1.1f;

        private static void EnqueuePopup(string text)
        {
            _popupQueue.Enqueue(text);
        }

        /// <summary>Score-style popup next to the crosshair.</summary>
        public static void Popup(string text) => EnqueuePopup(text);

        private static void UpdatePopup()
        {
            float age = Time.unscaledTime - _popupAt;
            if ((age > PopupSeconds * 0.55f || string.IsNullOrEmpty(_popupText)) && _popupQueue.Count > 0)
            {
                _popupText = _popupQueue.Dequeue();
                _popupAt = Time.unscaledTime;
                age = 0f;
            }
            if (!_popup) return;
            if (string.IsNullOrEmpty(_popupText) || age > PopupSeconds) { _popup.text = ""; return; }
            float t = age / PopupSeconds;
            _popup.text = _popupText;
            _popup.rectTransform.anchoredPosition = new Vector2(140f, -20f + 70f * t);
            _popup.color = new Color(1f, 0.85f, 0.3f, 1f - Mathf.SmoothStep(0f, 1f, Mathf.Max(0f, (t - 0.45f) / 0.55f)));
            _popup.fontSize = 44f + 10f * Mathf.Clamp01(1f - t * 4f);
        }

        /// <summary>Called once; listens for host kill announcements.</summary>
        public static void Init()
        {
            Net.ModNet.KillFeedReceived += k =>
            {
                if (!k.Suicide && k.KillerId == ModState.LocalOwnerId && k.KillerId != k.VictimId)
                {
                    EnqueuePopup("+100");
                    EnqueuePopup(ClientMatchView.IsFfa ? "KILL" : "ELIMINATED");
                }
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
                if (_popup) _popup.gameObject.SetActive(false);
                if (_corner) _corner.gameObject.SetActive(false);
                return;
            }
            if (!EnsureCreated()) return;
            _score.gameObject.SetActive(true);
            _banner.gameObject.SetActive(true);
            _feed.gameObject.SetActive(true);
            _feedLines.RemoveAll(l => Time.unscaledTime - l.at > FeedSeconds);
            _feed.text = string.Join("\n", _feedLines.Select(l => l.text));
            if (_popup) _popup.gameObject.SetActive(true);
            UpdatePopup();
            if (_corner)
            {
                bool tsLive = Trickshot.IsMode && ModState.Phase == MatchPhase.Live && !KillCam.Active;
                _corner.gameObject.SetActive(tsLive);
                if (tsLive) _corner.text = Trickshot.Status;
            }

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
            TrackCard();
            if (KillCam.Active)
            {
                _banner.text = "";   // the calling card at the bottom of the screen carries the killcam text
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
                    if (Trickshot.IsMode) _banner.text = (Time.unscaledTime - _liveAt < 2f) ? "JUMP AND HIT A BOT" : "";
                    else _banner.text = (Time.unscaledTime - _liveAt < 1f) ? "FIGHT" : (ClientMatchView.IsFfa && me != null && DeadNow() ? "Respawning..." : "");
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

        /// <summary>IMGUI: a calling card along the bottom of the screen during a killcam: emblem, name, rank, gun, and what happens next.</summary>
        public static void DrawKillcamCard()
        {
            if (!KillCam.Active || ModState.PanelOpen || !ClientMatchView.HasState) return;
            var s = ClientMatchView.Latest;
            var entry = ClientMatchView.Players.FirstOrDefault(p => p.Id == KillCam.KillerId);
            int tier = entry.Name != null ? RankService.Ladder.TierIndex(entry.RankPoints) : 0;
            string rank = entry.Name != null ? RankService.Ladder.TierName(entry.RankPoints).ToUpperInvariant() : "";
            string gun = entry.Name != null ? LoadoutService.Summary(entry.Loadout) : "";
            string head = KillCam.IsPreview ? "KILLCAM PREVIEW" : KillCam.IsFinal ? "FINAL KILLCAM" : (KillCam.IsReplay ? (KillCam.SlowMotion ? "KILLCAM   (slow motion)" : "KILLCAM") : "KILLED BY");
            string tail;
            if (KillCam.IsPreview) tail = "your own last seconds  |  F8 again to stop";
            else if (ModState.Phase == MatchPhase.RoundEnd || ModState.Phase == MatchPhase.MatchEnd) tail = s.StatusText ?? "";
            else if (ClientMatchView.IsFfa) tail = $"Respawning in {System.Math.Max(1, (int)System.Math.Ceiling(KillCam.SecondsLeft))}";
            else tail = "";

            var saved = RankedStyles.BeginCanvas();
            float w = 820f, h = 118f;
            float x = (RankedStyles.DesignW - w) / 2f, y = RankedStyles.DesignH - h - 26f;
            float open = Mathf.Clamp01((Time.unscaledTime - _cardShownAt) * 3f);
            if (!_cardWasActive) { _cardShownAt = Time.unscaledTime; _cardWasActive = true; open = 0f; }
            y += (1f - open) * 60f;
            GUI.color = new Color(1f, 1f, 1f, open);
            RankedStyles.Box(new Rect(x, y, w, h), RankedStyles.PanelColor, 16f);
            RankedStyles.Outline(new Rect(x, y, w, h), RankedStyles.GoldColor, 2f, 16f);
            GUI.DrawTexture(new Rect(x, y + 18, 6, h - 36), RankedStyles.Gold);
            RankedStyles.Emblem(x + 74, y + 10, 98, tier);
            GUI.Label(new Rect(x + 140, y + 8, w - 460, 30), head, RankedStyles.GoldText);
            GUI.Label(new Rect(x + 140, y + 36, w - 460, 40), KillCam.KillerName + (KillCam.IsFinal ? $"   <size=60%>killed  {KillCam.VictimName}</size>" : ""), RankedStyles.H1);
            GUI.Label(new Rect(x + 140, y + 80, w - 460, 28), (rank.Length > 0 ? rank + "   |   " : "") + gun, RankedStyles.Small);
            if (tail.Length > 0) GUI.Label(new Rect(x + w - 320, y + 30, 300, 60), tail, RankedStyles.BodyCenter);
            GUI.color = Color.white;
            GUI.matrix = saved;
        }

        private static float _cardShownAt;
        private static bool _cardWasActive;

        private static string _announce = "";
        private static float _announceUntil = -1f, _announceStart;
        private static bool _announceRed;

        /// <summary>A big centre-screen announcement for a few seconds (red flash when it is about you).</summary>
        public static void Announce(string text, float seconds, bool red = false)
        {
            _announce = text; _announceStart = Time.unscaledTime; _announceUntil = Time.unscaledTime + seconds; _announceRed = red;
        }

        public static void DrawAnnouncement()
        {
            if (Time.unscaledTime > _announceUntil || string.IsNullOrEmpty(_announce)) return;
            float age = Time.unscaledTime - _announceStart;
            float a = Mathf.Clamp01(age * 4f) * Mathf.Clamp01((_announceUntil - Time.unscaledTime) * 2f);
            var saved = RankedStyles.BeginCanvas();
            if (_announceRed)
            {
                GUI.color = new Color(0.6f, 0f, 0f, 0.35f * a * (0.7f + 0.3f * Mathf.Sin(age * 9f)));
                GUI.DrawTexture(new Rect(0, 0, RankedStyles.DesignW, RankedStyles.DesignH), RankedStyles.White);
            }
            GUI.color = new Color(1f, 1f, 1f, a);
            float w = 1100f, h = 180f, x = (RankedStyles.DesignW - w) / 2f, y = 300f + (1f - Mathf.Clamp01(age * 4f)) * 40f;
            RankedStyles.Box(new Rect(x, y, w, h), RankedStyles.PanelColor, 18f);
            RankedStyles.Outline(new Rect(x, y, w, h), RankedStyles.RedColor, 3f, 18f);
            var style = new GUIStyle(RankedStyles.H1Center) { fontSize = 40, richText = true };
            style.normal.textColor = new Color(1f, 0.35f, 0.3f);
            GUI.Label(new Rect(x, y, w, h), _announce, style);
            GUI.color = Color.white;
            GUI.matrix = saved;
        }

        /// <summary>Resets the card's slide-in when the killcam ends.</summary>
        public static void TrackCard() { if (!KillCam.Active) _cardWasActive = false; }

        /// <summary>Remembers when the phase became Live so the banner can flash FIGHT for one second.</summary>
        private static void TrackLive()
        {
            if (ModState.Phase == MatchPhase.Live && _lastPhase != MatchPhase.Live) _liveAt = Time.unscaledTime;
            _lastPhase = ModState.Phase;
        }

        private static bool EnsureCreated()
        {
            if (_score && _banner && _feed && _popup && _corner) return true;
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
            _corner = Make(prefab, parent, "HTF1v1_Corner", new Vector2(1f, 0f), new Vector2(-30f, 30f), 24f);
            _corner.alignment = TextAlignmentOptions.BottomRight;
            _corner.rectTransform.pivot = new Vector2(1f, 0f);
            _corner.rectTransform.sizeDelta = new Vector2(500f, 40f);
            _corner.color = new Color(1f, 1f, 1f, 0.85f);
            _popup = Make(prefab, parent, "HTF1v1_ScorePopup", new Vector2(0.5f, 0.5f), new Vector2(140f, -20f), 48f);
            _popup.alignment = TextAlignmentOptions.Left;
            _popup.rectTransform.pivot = new Vector2(0f, 0.5f);
            _popup.rectTransform.sizeDelta = new Vector2(400f, 80f);
            _popup.fontStyle = FontStyles.Bold;
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
