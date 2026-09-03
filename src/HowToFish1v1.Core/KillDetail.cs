using System.Collections.Generic;

namespace HowToFish1v1.Core
{
    /// <summary>What a kill produced: whether it counted, the killer's streak afterwards, and the medals it earned.</summary>
    public struct KillDetail
    {
        public bool Accepted;
        public bool Credited;
        public int Streak;
        public bool OneShotGranted;
        public List<string> Medals;

        public string MedalText => Medals == null || Medals.Count == 0 ? "" : string.Join(",", Medals);
    }

    /// <summary>Killstreak thresholds and the medals the host hands out.</summary>
    public static class Streaks
    {
        public const int Uav = 3;
        public const int ExtraMag = 5;
        public const int OneShot = 7;
        public const double MultiKillWindow = 4.0;

        public const string FirstBlood = "FIRST BLOOD";
        public const string Comeback = "COMEBACK";
        public const string Firehorn = "FIREHORN";     // ricochet kill
        public const string Shank = "SHANK";           // knife kill
        public const string Fragged = "FRAGGED";       // grenade kill
        public const string Airborne = "AIRBORNE";     // killer was in the air
        public const string DoubleKill = "DOUBLE KILL";
        public const string TripleKill = "TRIPLE KILL";
        public const string Rampage = "RAMPAGE";       // four or more in the window
        public const string Streak3 = "KILLSTREAK 3: UAV";
        public const string Streak5 = "KILLSTREAK 5: FRESH MAG";
        public const string Streak7 = "KILLSTREAK 7: ONE SHOT";

        public static string StreakName(int streak)
        {
            switch (streak)
            {
                case Uav: return Streak3;
                case ExtraMag: return Streak5;
                case OneShot: return Streak7;
                default: return null;
            }
        }
    }
}
