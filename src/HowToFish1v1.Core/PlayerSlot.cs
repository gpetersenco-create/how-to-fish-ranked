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
        public int Score;
        public bool DeadThisRound;

        public bool IsPresent => Id != -1;

        public void Clear()
        {
            Id = -1; Name = ""; Loadout = Array.Empty<byte>();
            Ready = false; HasMod = false; Score = 0; DeadThisRound = false;
        }
    }
}
