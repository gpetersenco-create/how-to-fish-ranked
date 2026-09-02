using FishNet.Broadcast;

namespace HowToFish1v1.Net
{
    public struct HelloBroadcast : IBroadcast
    {
        public string ModVersion;
    }

    public struct LoadoutBroadcast : IBroadcast
    {
        public byte[] ItemIds;
        public bool Ready;
        public int RankPoints;
    }

    public struct ArenaBroadcast : IBroadcast
    {
        public bool Build;
        public byte ReturnIsland;
        public byte MapIndex;
    }

    public struct PlayerEntry
    {
        public int Id;
        public string Name;
        public byte Team;
        public int Kills;
        public bool Ready;
        public bool HasMod;
        public int RankPoints;
        public byte[] Loadout;
    }

    public struct MatchStateBroadcast : IBroadcast
    {
        public byte Phase;
        public byte Mode;
        public int Round;
        public int MatchNumber;
        public int TeamScoreA;
        public int TeamScoreB;
        public bool TeamAIsLeft;
        public uint PhaseEndsAtTick;
        public int LastRoundWinnerTeam;
        public int MatchWinnerTeam;
        public int MatchWinnerId;
        public string StatusText;
        public byte MapIndex;
        public int KillsToWin;
        public int RoundsToWin;
        public PlayerEntry[] Players;
    }
}
