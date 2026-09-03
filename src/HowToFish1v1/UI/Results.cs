using System.Collections.Generic;
using System.Linq;
using HowToFish1v1.Core;
using HowToFish1v1.Match;
using HowToFish1v1.Net.Proto2;
using UnityEngine;
using S = HowToFish1v1.UI.RankedStyles;

namespace HowToFish1v1.UI
{
    /// <summary>
    /// End-of-match screen: the result, every player's kills, deaths, best streak and medals, and the local player's rank
    /// change, shown when the match returns to the lobby. Continue closes it (or it fades after a while).
    /// </summary>
    public static class Results
    {
        private sealed class Row { public int Id; public string Name; public int Team; public int Kills, Deaths, Streak, RankPoints; public List<string> Medals; }

        private static bool _visible;
        private static float _shownAt, _autoCloseAt;
        private static readonly List<Row> _rows = new List<Row>();
        private static string _title = "", _subtitle = "", _rankLine = "";
        private static bool _won, _ffa;
        private static MatchStateBroadcast _snapshot;
        private static bool _haveSnapshot;

        public static bool Visible => _visible;

        /// <summary>Remember the last state of the match while it is still in MatchEnd (the lobby clears the scores).</summary>
        public static void Snapshot(MatchStateBroadcast s)
        {
            _snapshot = s; _haveSnapshot = true;
        }

        public static void Show()
        {
            if (!_haveSnapshot) return;
            var s = _snapshot;
            var mode = (MatchMode)s.Mode;
            int me = ModState.LocalOwnerId;
            _ffa = MatchModes.IsFfa(mode);
            _rows.Clear();
            foreach (var p in s.Players ?? new PlayerEntry[0])
            {
                var t = MatchEvents.Of(p.Id);
                _rows.Add(new Row { Id = p.Id, Name = p.Name, Team = p.Team, Kills = p.Kills, Deaths = p.Deaths, Streak = t.BestStreak, RankPoints = p.RankPoints, Medals = new List<string>(t.Medals) });
            }
            var meEntry = s.Players?.FirstOrDefault(p => p.Id == me);
            if (MatchModes.IsSolo(mode)) { _won = true; _title = "TRICKSHOT HIT"; }
            else if (_ffa) { _won = s.MatchWinnerId == me; _title = _won ? "VICTORY" : "DEFEAT"; }
            else { _won = meEntry.HasValue && s.MatchWinnerTeam == meEntry.Value.Team; _title = _won ? "VICTORY" : "DEFEAT"; }
            string map = ArenaLayout.MapNames[((s.MapIndex % ArenaLayout.MapCount) + ArenaLayout.MapCount) % ArenaLayout.MapCount];
            _subtitle = $"{MatchModes.Name(mode).ToUpperInvariant()}   |   {map.ToUpperInvariant()}   |   {s.StatusText}";
            _rankLine = MatchModes.IsSolo(mode) ? "" : RankService.LastResultText;
            _visible = true;
            _shownAt = Time.unscaledTime;
            _autoCloseAt = Time.unscaledTime + 25f;
            S.MarkOpen("results");
            Announcer.Play(MatchModes.IsSolo(mode) ? "trickshot" : (_won ? "victory" : "defeat"));
            if (!MatchModes.IsSolo(mode))
            {
                var mine = MatchEvents.Of(me);
                Plugin.Log.LogInfo($"Results: {_title} K{meEntry?.Kills} D{meEntry?.Deaths} streak {mine.BestStreak} medals {mine.Medals.Count}");
            }
        }

        public static void Hide() { _visible = false; }

        public static void Draw()
        {
            if (!_visible) return;
            if (Time.unscaledTime > _autoCloseAt || !ModState.IsActive) { _visible = false; return; }
            float open = S.Ease("results", 0.4f);
            var saved = S.BeginCanvas((1f - open) * 30f);
            S.DrawBackground(open);
            GUI.color = new Color(1f, 1f, 1f, open);

            var titleStyle = new GUIStyle(S.Title) { fontSize = 76, alignment = TextAnchor.MiddleCenter };
            titleStyle.normal.textColor = _won ? S.GoldColor : new Color(0.9f, 0.4f, 0.4f);
            S.Glow(new Rect(S.DesignW / 2f - 300, 60, 600, 120), _won ? new Color(1f, 0.85f, 0.4f, 0.35f) : new Color(1f, 0.3f, 0.3f, 0.25f), 1.6f);
            S.Shadowed(new Rect(0, 60, S.DesignW, 120), _title, titleStyle);
            GUI.Label(new Rect(0, 180, S.DesignW, 34), _subtitle, S.SmallCenter);
            if (_rankLine.Length > 0) GUI.Label(new Rect(0, 214, S.DesignW, 40), _rankLine, new GUIStyle(S.GoldText) { alignment = TextAnchor.MiddleCenter, fontSize = 24 });

            float w = 1500f, x = (S.DesignW - w) / 2f, y = 270f;
            S.Box(new Rect(x, y, w, 46), S.PanelColor, 10f);
            GUI.Label(new Rect(x + 24, y + 8, 60, 30), "#", S.Small);
            GUI.Label(new Rect(x + 90, y + 8, 500, 30), "PLAYER", S.Small);
            GUI.Label(new Rect(x + 640, y + 8, 100, 30), "KILLS", S.SmallRight);
            GUI.Label(new Rect(x + 760, y + 8, 100, 30), "DEATHS", S.SmallRight);
            GUI.Label(new Rect(x + 880, y + 8, 100, 30), "K/D", S.SmallRight);
            GUI.Label(new Rect(x + 1000, y + 8, 120, 30), "STREAK", S.SmallRight);
            GUI.Label(new Rect(x + 1150, y + 8, 330, 30), "MEDALS", S.Small);
            float ry = y + 56;
            int me = ModState.LocalOwnerId;
            var ordered = _ffa ? _rows.OrderByDescending(r => r.Kills).ThenBy(r => r.Deaths).ToList() : _rows.OrderBy(r => r.Team).ThenByDescending(r => r.Kills).ToList();
            int i = 0;
            foreach (var r in ordered)
            {
                float ce = S.Ease("results", 0.3f, 0.08f + 0.05f * i);
                var g = GUI.color; GUI.color = new Color(1f, 1f, 1f, g.a * ce);
                float ox = (1f - ce) * 40f;
                bool mine = r.Id == me;
                S.Box(new Rect(x + ox, ry, w, 64), mine ? S.PanelLightColor : S.PanelColor, 12f);
                if (mine) S.Outline(new Rect(x + ox, ry, w, 64), new Color(1f, 0.85f, 0.4f, 0.5f), 1.5f, 12f);
                if (!_ffa) S.Box(new Rect(x + ox, ry + 10, 6, 44), r.Team == 0 ? S.GoldColor : S.PanelHoverColor, 3f);
                GUI.Label(new Rect(x + ox + 24, ry + 14, 60, 36), (i + 1).ToString(), S.Body);
                S.Emblem(x + ox + 118, ry + 8, 48, RankService.Ladder.TierIndex(r.RankPoints));
                GUI.Label(new Rect(x + ox + 160, ry + 14, 460, 36), r.Name + (mine ? "  (you)" : ""), mine ? S.GoldText : S.Body);
                GUI.Label(new Rect(x + ox + 640, ry + 14, 100, 36), r.Kills.ToString(), S.Body);
                GUI.Label(new Rect(x + ox + 760, ry + 14, 100, 36), r.Deaths.ToString(), S.Body);
                GUI.Label(new Rect(x + ox + 880, ry + 14, 100, 36), (r.Deaths == 0 ? r.Kills : (float)r.Kills / r.Deaths).ToString("0.00"), S.Body);
                GUI.Label(new Rect(x + ox + 1000, ry + 14, 120, 36), r.Streak.ToString(), S.Body);
                var counts = r.Medals.GroupBy(m => m).Select(gr => gr.Count() > 1 ? $"{gr.Key} x{gr.Count()}" : gr.Key);
                GUI.Label(new Rect(x + ox + 1150, ry + 14, 340, 36), string.Join("  ", counts), S.GoldText);
                GUI.color = g;
                ry += 72;
                i++;
            }
            if (S.Btn(new Rect(S.DesignW / 2f - 150, S.DesignH - 130, 300, 64), "CONTINUE", S.BigButton, 14f)) _visible = false;
            GUI.Label(new Rect(0, S.DesignH - 56, S.DesignW, 30), "Back to the lobby", S.SmallCenter);
            GUI.color = Color.white;
            GUI.matrix = saved;
        }
    }
}
