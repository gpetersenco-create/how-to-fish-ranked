using FishNet.Broadcast;

// FishNet keys broadcasts by the type's full name. Bumping this namespace whenever the wire layout changes makes
// older builds skip our packets cleanly (unknown key) instead of mis-parsing them and disconnecting the sender.
namespace HowToFish1v1.Net.Proto2
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
        /// <summary>Doubles as a hello: any loadout message proves the sender runs this mod version.</summary>
        public string ModVersion;
    }

    public struct KillFeedBroadcast : IBroadcast
    {
        public string Killer;
        public string Victim;
        public bool Suicide;
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
        public int Deaths;
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
