namespace HowToFish1v1.Core
{
    public sealed class MatchRules
    {
        public int RoundsToWin = 6;
        public int KillsToWin = 10;
        public double CountdownSeconds = 3;
        public double RoundEndSeconds = 2;
        public double MatchEndSeconds = 5;
        public double FfaRespawnSeconds = 3;
        public int MaxLoadoutGuns = 2;
        // Search and Destroy
        public double RoundSeconds = 90;
        public double PlantSeconds = 4;
        public double DefuseSeconds = 6;
        public double BombSeconds = 40;
        public bool SoloDebug = false;
    }
}
