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
    }

    public struct ArenaBroadcast : IBroadcast
    {
        public bool Build;
        public byte ReturnIsland;
        public byte MapIndex;
    }

    public struct MatchStateBroadcast : IBroadcast
    {
        public byte Phase;
        public int Round;
        public int AId; public string AName; public int AScore; public bool AReady; public bool AHasMod; public byte[] ALoadout;
        public int BId; public string BName; public int BScore; public bool BReady; public bool BHasMod; public byte[] BLoadout;
        public bool AIsLeft;
        public uint PhaseEndsAtTick;
        public int LastRoundWinnerId;
        public int MatchWinnerId;
        public string StatusText;
        public byte MapIndex;
    }
}
