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

    /// <summary>Client to host: my aim-down-sights state changed.</summary>
    public struct AimBroadcast : IBroadcast
    {
        public bool Ads;
    }

    /// <summary>Host to all: a player's aim-down-sights state (for killcam replays).</summary>
    /// <summary>Client to host: I swung the knife (skin index for the replay copy).</summary>
    public struct KnifeBroadcast : IBroadcast { public byte Skin; }

    /// <summary>Host to everyone: this player swung the knife.</summary>
    public struct KnifeStateBroadcast : IBroadcast { public int OwnerId; public byte Skin; }

    public struct AimStateBroadcast : IBroadcast
    {
        public int OwnerId;
        public bool Ads;
    }

    public struct KillFeedBroadcast : IBroadcast
    {
        public string Killer;
        public string Victim;
        public bool Suicide;
        public int KillerId;
        public int VictimId;
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
        /// <summary>Mod version this player reported ("" if none), so the lobby can say who needs to update.</summary>
        public string ModVersion;
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
        /// <summary>Timing rules so clients can fit the killcam into the time they have (0 when the host predates them).</summary>
        public float RespawnSeconds;
        public float RoundEndSeconds;
        public float MatchEndSeconds;
    }
}
