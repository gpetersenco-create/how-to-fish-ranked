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
        /// <summary>Charm hanging off the gun (unused since 0.2.39, kept for wire compatibility).</summary>
        public byte Charm = 0;
        /// <summary>Map this player voted for in the lobby; -1 for no vote.</summary>
        public int Vote = -1;

        // Killstreaks and medals (host side).
        public int Streak;                 // kills since the last death
        public int BestStreak;
        public bool OneShot;               // killstreak 7 reward: every hit kills, until death
        public int DeathsSinceKill;        // for the comeback medal
        public double LastKillAt = -100;   // for double / triple kills
        public int MultiKill;              // kills inside the multi-kill window
        public int Medals;                 // medals earned this match

        public bool IsPresent => Id != -1;

        public void ResetMatchStats()
        {
            Kills = 0; Deaths = 0; Streak = 0; BestStreak = 0; OneShot = false; DeathsSinceKill = 0; LastKillAt = -100; MultiKill = 0; Medals = 0;
        }
    }
}
