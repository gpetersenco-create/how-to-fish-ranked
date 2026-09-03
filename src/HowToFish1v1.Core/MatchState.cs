using System.Collections.Generic;
using System.Linq;

namespace HowToFish1v1.Core
{
    public sealed class MatchState
    {
        public const int MaxPlayers = 8;

        public MatchPhase Phase = MatchPhase.Inactive;
        public MatchMode Mode = MatchMode.OneVOne;
        public int Round;
        public int MatchNumber;
        public List<PlayerSlot> Players = new List<PlayerSlot>();
        public int[] TeamScore = new int[2];
        /// <summary>Team 0 spawns on the left pad when true; swaps every round.</summary>
        public bool TeamAIsLeft = true;
        public double PhaseEndsAt;
        public int LastRoundWinnerTeam = -1;
        public int MatchWinnerTeam = -1;
        /// <summary>Free-for-all winner (owner id); -1 otherwise.</summary>
        public int MatchWinnerId = -1;
        public string StatusText = "";
        public bool ArenaBuilt;
        /// <summary>Set once the first kill of the match has been made (first blood medal).</summary>
        public bool FirstBloodDone;
        public int MapIndex;
        public int BuiltMapIndex = -1;

        public int PresentCount => Players.Count;

        public PlayerSlot Slot(int id) => id == -1 ? null : Players.FirstOrDefault(p => p.Id == id);

        public IEnumerable<PlayerSlot> TeamMembers(int team) => Players.Where(p => p.Team == team);

        public int TeamCount(int team) => Players.Count(p => p.Team == team);

        public Side SideFor(int id)
        {
            var s = Slot(id);
            int team = s?.Team ?? 0;
            return ((team == 0) == TeamAIsLeft) ? Side.Left : Side.Right;
        }

        /// <summary>Position of the player within its team's pad line-up: index and team size.</summary>
        public (int index, int count) TeamSlot(int id)
        {
            var s = Slot(id);
            if (s == null) return (0, 1);
            var members = TeamMembers(s.Team).ToList();
            return (members.IndexOf(s), members.Count);
        }

        public int TeamOf(int id) => Slot(id)?.Team ?? -1;

        public bool IsRoundPhase => Phase == MatchPhase.Countdown || Phase == MatchPhase.Live || Phase == MatchPhase.RoundEnd || Phase == MatchPhase.MatchEnd;

        public bool IsFfa => MatchModes.IsFfa(Mode);
    }
}
