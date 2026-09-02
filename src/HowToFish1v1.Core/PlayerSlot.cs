using System;

namespace HowToFish1v1.Core
{
    public sealed class PlayerSlot
    {
        public int Id = -1;
        public string Name = "";
        public byte[] Loadout = Array.Empty<byte>();
        public bool Ready;
        public bool HasMod;
        /// <summary>0 or 1 in team modes; ignored in free-for-all.</summary>
        public int Team;
        public int Kills;
        public int Deaths;
        public bool DeadThisRound;
        public int RankPoints;

        public bool IsPresent => Id != -1;
    }
}
