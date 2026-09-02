using System;

namespace HowToFish1v1.Core
{
    /// <summary>Local rank ladder: a tier every PointsPerTier points, names supplied by config.</summary>
    public sealed class RankLadder
    {
        public const string DefaultNames =
            "Master Baiter,Bottom Feeder,Small Fry,Chum Chucker,Reel Deal,Hook Line and Sinker,Big Fish,Apex Angler,Kraken,Poseidon";

        public int PointsPerTier { get; }
        public string[] Names { get; }
        public int WinPoints { get; }
        public int LossPoints { get; }
        public int FfaLossPoints { get; }

        public RankLadder(string namesCsv = DefaultNames, int pointsPerTier = 100, int winPoints = 20, int lossPoints = 10, int ffaLossPoints = 5)
        {
            var names = (namesCsv ?? "").Split(',');
            var cleaned = Array.FindAll(Array.ConvertAll(names, n => n.Trim()), n => n.Length > 0);
            Names = cleaned.Length > 0 ? cleaned : DefaultNames.Split(',');
            PointsPerTier = Math.Max(1, pointsPerTier);
            WinPoints = Math.Max(0, winPoints);
            LossPoints = Math.Max(0, lossPoints);
            FfaLossPoints = Math.Max(0, ffaLossPoints);
        }

        public int TierIndex(int points) => Math.Min(Names.Length - 1, Math.Max(0, points) / PointsPerTier);

        public string TierName(int points) => Names[TierIndex(points)];

        /// <summary>Points still needed for the next tier, or 0 at the top.</summary>
        public int PointsToNext(int points)
        {
            int tier = TierIndex(points);
            if (tier >= Names.Length - 1) return 0;
            return (tier + 1) * PointsPerTier - Math.Max(0, points);
        }

        /// <summary>New point total after a match. Never below zero.</summary>
        public int Apply(int points, bool won, bool ffa)
        {
            int delta = won ? WinPoints : -(ffa ? FfaLossPoints : LossPoints);
            return Math.Max(0, points + delta);
        }
    }
}
