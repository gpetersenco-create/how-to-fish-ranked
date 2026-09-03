using System;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// Ranked seasons: a fixed calendar of four-week seasons from 1 September 2026. When a new season starts, each
    /// player's rank points are archived (locally and to the leaderboard's season table) and reset to zero; lifetime
    /// wins, losses and kills stay. The emblem shows the season number and the leaderboard shows last season's top players.
    /// </summary>
    public static class Seasons
    {
        public static readonly DateTime Epoch = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        public static int LengthDays => Math.Max(7, Plugin.Cfg.SeasonLengthDays.Value);

        public static int Current
        {
            get
            {
                double days = (DateTime.UtcNow - Epoch).TotalDays;
                return days < 0 ? 1 : 1 + (int)Math.Floor(days / LengthDays);
            }
        }

        public static DateTime EndOfCurrent => Epoch.AddDays((double)Current * LengthDays);

        public static string TimeLeftText
        {
            get
            {
                var left = EndOfCurrent - DateTime.UtcNow;
                if (left.TotalDays >= 2) return $"{(int)left.TotalDays} days left";
                if (left.TotalHours >= 1) return $"{(int)left.TotalHours} hours left";
                return "ends soon";
            }
        }

        public static string Name(int season) => "Season " + season;
    }
}
