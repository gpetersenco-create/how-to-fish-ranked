using System.Linq;
using HowToFish1v1.Core;
using HowToFish1v1.Match;
using UnityEngine;
using S = HowToFish1v1.UI.RankedStyles;

namespace HowToFish1v1.UI
{
    /// <summary>Hold Tab during a match: players with rank, kills, deaths and K/D, grouped by team (or ordered by kills in free-for-all).</summary>
    public static class Scoreboard
    {
        public static bool Visible => ModState.IsActive && ClientMatchView.HasState && !ModState.PanelOpen && Input.GetKey(KeyCode.Tab);

        public static void Draw()
        {
            if (!Visible) return;
            var saved = S.BeginCanvas();
            var s = ClientMatchView.Latest;
            bool ffa = ClientMatchView.IsFfa;
            float w = 1100, x = (S.DesignW - w) / 2f, y = 120;
            int rows = ClientMatchView.Players.Length + (ffa ? 1 : 2);
            float h = 110 + rows * 52 + 40;
            S.Box(new Rect(x, y, w, h), S.Bg);
            GUI.DrawTexture(new Rect(x, y, w, 6), S.Gold);

            string map = ArenaLayout.MapNames[((s.MapIndex % ArenaLayout.MapCount) + ArenaLayout.MapCount) % ArenaLayout.MapCount];
            string title = ffa ? $"FREE-FOR-ALL   first to {s.KillsToWin}" : $"{MatchModes.Name((MatchMode)s.Mode).ToUpperInvariant()}   round {s.Round}   {s.TeamScoreA} - {s.TeamScoreB}";
            GUI.Label(new Rect(x + 30, y + 20, w - 60, 40), title, S.H1);
            GUI.Label(new Rect(x + 30, y + 20, w - 60, 40), map.ToUpperInvariant(), S.SmallRight);
            float ry = y + 72;
            Header(x, ry, w); ry += 38;

            if (ffa)
            {
                foreach (var p in ClientMatchView.Players.OrderByDescending(p => p.Kills).ThenBy(p => p.Deaths)) { Row(x, ry, w, p, ffa); ry += 52; }
            }
            else
            {
                foreach (int team in new[] { 0, 1 })
                {
                    S.Box(new Rect(x + 20, ry, w - 40, 44), S.Panel);
                    GUI.DrawTexture(new Rect(x + 20, ry, 6, 44), team == 0 ? S.Gold : S.PanelHover);
                    GUI.Label(new Rect(x + 40, ry + 4, 400, 36), ClientMatchView.TeamLabel(team).ToUpperInvariant(), S.H2);
                    GUI.Label(new Rect(x + w - 160, ry + 4, 120, 36), (team == 0 ? s.TeamScoreA : s.TeamScoreB).ToString(), S.H1Center);
                    ry += 52;
                    foreach (var p in ClientMatchView.Players.Where(p => p.Team == team).OrderByDescending(p => p.Kills)) { Row(x, ry, w, p, ffa); ry += 52; }
                }
            }
            GUI.matrix = saved;
        }

        private static void Header(float x, float y, float w)
        {
            GUI.Label(new Rect(x + 130, y, 400, 30), "PLAYER", S.Small);
            GUI.Label(new Rect(x + 560, y, 200, 30), "RANK", S.Small);
            GUI.Label(new Rect(x + 790, y, 80, 30), "KILLS", S.SmallCenter);
            GUI.Label(new Rect(x + 880, y, 80, 30), "DEATHS", S.SmallCenter);
            GUI.Label(new Rect(x + 970, y, 80, 30), "K/D", S.SmallCenter);
        }

        private static void Row(float x, float y, float w, Net.Proto2.PlayerEntry p, bool ffa)
        {
            bool me = p.Id == ModState.LocalOwnerId;
            GUI.DrawTexture(new Rect(x + 20, y, w - 40, 46), me ? S.PanelLight : S.Panel);
            int tier = RankService.Ladder.TierIndex(p.RankPoints);
            S.Emblem(x + 80, y + 3, 40, tier);
            GUI.Label(new Rect(x + 130, y + 6, 420, 34), p.Name + (me ? "  (you)" : ""), me ? S.GoldText : S.Body);
            GUI.Label(new Rect(x + 560, y + 6, 220, 34), RankService.Ladder.TierName(p.RankPoints), S.Small);
            GUI.Label(new Rect(x + 790, y + 6, 80, 34), p.Kills.ToString(), S.BodyCenter);
            GUI.Label(new Rect(x + 880, y + 6, 80, 34), p.Deaths.ToString(), S.BodyCenter);
            float kd = p.Deaths == 0 ? p.Kills : (float)p.Kills / p.Deaths;
            GUI.Label(new Rect(x + 970, y + 6, 80, 34), kd.ToString("0.0"), S.BodyCenter);
        }
    }
}
