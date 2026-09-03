namespace HowToFish1v1.Core
{
    public enum MatchMode : byte
    {
        OneVOne = 0, TwoVTwo = 1, ThreeVThree = 2, FreeForAll = 3, Trickshot = 4,
        OneInTheChamber = 5, SniperOnly = 6, KnifeOnly = 7
    }

    /// <summary>How a kill was dealt, for medals and killstreak rules.</summary>
    public enum KillKind : byte { Bullet = 0, Knife = 1, Ricochet = 2, Other = 3 }

    public static class MatchModes
    {
        public static readonly MatchMode[] All =
        {
            MatchMode.OneVOne, MatchMode.TwoVTwo, MatchMode.ThreeVThree, MatchMode.FreeForAll,
            MatchMode.OneInTheChamber, MatchMode.SniperOnly, MatchMode.KnifeOnly, MatchMode.Trickshot
        };

        public static string Name(MatchMode m)
        {
            switch (m)
            {
                case MatchMode.TwoVTwo: return "2v2";
                case MatchMode.ThreeVThree: return "3v3";
                case MatchMode.FreeForAll: return "Free-for-all";
                case MatchMode.Trickshot: return "Trickshot";
                case MatchMode.OneInTheChamber: return "One in the Chamber";
                case MatchMode.SniperOnly: return "Sniper Only";
                case MatchMode.KnifeOnly: return "Knife Only";
                default: return "1v1";
            }
        }

        /// <summary>Kill-count modes with respawns: free-for-all and its variants.</summary>
        public static bool IsFfa(MatchMode m) => m == MatchMode.FreeForAll || m == MatchMode.OneInTheChamber || m == MatchMode.SniperOnly || m == MatchMode.KnifeOnly;
        /// <summary>One-player practice modes: no teams, no rank change.</summary>
        public static bool IsSolo(MatchMode m) => m == MatchMode.Trickshot;
        /// <summary>Modes where a death respawns the player in place instead of ending a round.</summary>
        public static bool RespawnsInPlace(MatchMode m) => IsFfa(m) || IsSolo(m);

        /// <summary>
        /// Gun restriction: null means the players' own loadouts, "" means no guns at all, otherwise a name fragment the
        /// only allowed gun must contain (every player gets exactly that gun).
        /// </summary>
        public static string GunFilter(MatchMode m)
        {
            switch (m)
            {
                case MatchMode.OneInTheChamber: return "pistol";
                case MatchMode.SniperOnly: return "snip";
                case MatchMode.KnifeOnly: return "";
                default: return null;
            }
        }

        /// <summary>One bullet in the gun, one more per kill, no reloading, every bullet kills.</summary>
        public static bool OneBullet(MatchMode m) => m == MatchMode.OneInTheChamber;

        /// <summary>Players per team in team modes; 0 for free-for-all.</summary>
        public static int TeamSize(MatchMode m)
        {
            switch (m)
            {
                case MatchMode.TwoVTwo: return 2;
                case MatchMode.ThreeVThree: return 3;
                case MatchMode.Trickshot: return 1;
                default: return IsFfa(m) ? 0 : 1;
            }
        }

        /// <summary>Team modes only need two players (one per side); the team size is a cap, so 2v2 can run as 2v1.</summary>
        public static int MinPlayers(MatchMode m) => IsSolo(m) ? 1 : 2;
        public static int MaxPlayers(MatchMode m) => IsSolo(m) ? 1 : (IsFfa(m) ? 8 : TeamSize(m) * 2);
    }
}
