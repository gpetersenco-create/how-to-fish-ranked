namespace HowToFish1v1.Core
{
    public sealed class MatchState
    {
        public MatchPhase Phase = MatchPhase.Inactive;
        public int Round;
        public PlayerSlot A = new PlayerSlot();
        public PlayerSlot B = new PlayerSlot();
        public bool AIsLeft = true;
        public double PhaseEndsAt;
        public int LastRoundWinnerId = -1;
        public int MatchWinnerId = -1;
        public string StatusText = "";
        public bool ArenaBuilt;

        public int PresentCount => (A.IsPresent ? 1 : 0) + (B.IsPresent ? 1 : 0);

        public PlayerSlot Slot(int id)
        {
            if (id == -1) return null;
            if (A.Id == id) return A;
            if (B.Id == id) return B;
            return null;
        }

        public PlayerSlot Other(int id)
        {
            if (A.Id == id) return B.IsPresent ? B : null;
            if (B.Id == id) return A.IsPresent ? A : null;
            return null;
        }

        public Side SideFor(int id)
        {
            bool isA = A.Id == id;
            return (isA == AIsLeft) ? Side.Left : Side.Right;
        }

        public bool IsRoundPhase => Phase == MatchPhase.Countdown || Phase == MatchPhase.Live || Phase == MatchPhase.RoundEnd || Phase == MatchPhase.MatchEnd;
    }
}
