namespace HowToFish1v1.Core
{
    public enum MatchMode : byte { OneVOne = 0, TwoVTwo = 1, ThreeVThree = 2, FreeForAll = 3 }

    public static class MatchModes
    {
        public static readonly MatchMode[] All = { MatchMode.OneVOne, MatchMode.TwoVTwo, MatchMode.ThreeVThree, MatchMode.FreeForAll };

        public static string Name(MatchMode m)
        {
            switch (m)
            {
                case MatchMode.TwoVTwo: return "2v2";
                case MatchMode.ThreeVThree: return "3v3";
                case MatchMode.FreeForAll: return "Free-for-all";
                default: return "1v1";
            }
        }

        public static bool IsFfa(MatchMode m) => m == MatchMode.FreeForAll;

        /// <summary>Players per team in team modes; 0 for free-for-all.</summary>
        public static int TeamSize(MatchMode m)
        {
            switch (m)
            {
                case MatchMode.TwoVTwo: return 2;
                case MatchMode.ThreeVThree: return 3;
                case MatchMode.FreeForAll: return 0;
                default: return 1;
            }
        }

        public static int MinPlayers(MatchMode m) => IsFfa(m) ? 2 : TeamSize(m) * 2;
        public static int MaxPlayers(MatchMode m) => IsFfa(m) ? 8 : TeamSize(m) * 2;
    }
}
