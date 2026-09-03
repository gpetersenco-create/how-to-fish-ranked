namespace HowToFish1v1.Core
{
    public enum MatchMode : byte { OneVOne = 0, TwoVTwo = 1, ThreeVThree = 2, FreeForAll = 3, Trickshot = 4 }

    public static class MatchModes
    {
        public static readonly MatchMode[] All = { MatchMode.OneVOne, MatchMode.TwoVTwo, MatchMode.ThreeVThree, MatchMode.FreeForAll, MatchMode.Trickshot };

        public static string Name(MatchMode m)
        {
            switch (m)
            {
                case MatchMode.TwoVTwo: return "2v2";
                case MatchMode.ThreeVThree: return "3v3";
                case MatchMode.FreeForAll: return "Free-for-all";
                case MatchMode.Trickshot: return "Trickshot";
                default: return "1v1";
            }
        }

        public static bool IsFfa(MatchMode m) => m == MatchMode.FreeForAll;
        /// <summary>One-player practice modes: no teams, no rank change.</summary>
        public static bool IsSolo(MatchMode m) => m == MatchMode.Trickshot;
        /// <summary>Modes where a death respawns the player in place instead of ending a round.</summary>
        public static bool RespawnsInPlace(MatchMode m) => IsFfa(m) || IsSolo(m);

        /// <summary>Players per team in team modes; 0 for free-for-all.</summary>
        public static int TeamSize(MatchMode m)
        {
            switch (m)
            {
                case MatchMode.TwoVTwo: return 2;
                case MatchMode.ThreeVThree: return 3;
                case MatchMode.FreeForAll: return 0;
                case MatchMode.Trickshot: return 1;
                default: return 1;
            }
        }

        /// <summary>Team modes only need two players (one per side); the team size is a cap, so 2v2 can run as 2v1.</summary>
        public static int MinPlayers(MatchMode m) => IsSolo(m) ? 1 : 2;
        public static int MaxPlayers(MatchMode m) => IsSolo(m) ? 1 : (IsFfa(m) ? 8 : TeamSize(m) * 2);
    }
}
