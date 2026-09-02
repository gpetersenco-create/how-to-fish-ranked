using System;
using HowToFish1v1.Core;
using UnityEngine;

namespace HowToFish1v1
{
    /// <summary>Per-peer view of the mode used by patches and UI. Set by ClientMatchView (all peers) and HostMatchController (host).</summary>
    public static class ModState
    {
        public static MatchPhase Phase = MatchPhase.Inactive;
        public static bool PanelOpen;

        /// <summary>Unscaled time until which player teleports use the instant path even when the mode is inactive (island return).</summary>
        public static float ForceInstantTeleportUntil = -1f;

        public static bool InstantTeleports => IsActive || Time.unscaledTime < ForceInstantTeleportUntil;

        /// <summary>Owner id of the local player, or -1 when not in a game.</summary>
        public static int LocalOwnerId => Player.LocalPlayer ? Player.LocalPlayer.OwnerId : -1;

        public static bool IsActive => Phase != MatchPhase.Inactive;

        public static bool FreezeInputs =>
            PanelOpen || Phase == MatchPhase.Countdown || Phase == MatchPhase.RoundEnd || Phase == MatchPhase.MatchEnd;

        /// <summary>Which side a player is on this round; null if unknown. Set by ClientMatchView.</summary>
        public static Func<int, Side?> SideLookup;

        /// <summary>World spawn for a side. Set by ArenaBuilder when built.</summary>
        public static Func<Side, (Vector3 pos, float yaw)?> SpawnLookup;

        public static event Action<Player> KillDetected;

        public static void RaiseKill(Player p) => KillDetected?.Invoke(p);

        public static bool TryGetSpawn(int ownerId, out Vector3 pos, out float yaw)
        {
            pos = Vector3.zero; yaw = 0f;
            var side = SideLookup?.Invoke(ownerId);
            if (side == null) return false;
            var s = SpawnLookup?.Invoke(side.Value);
            if (s == null) return false;
            pos = s.Value.pos; yaw = s.Value.yaw;
            return true;
        }

        public static void Reset()
        {
            Phase = MatchPhase.Inactive;
            PanelOpen = false;
            SideLookup = null;
        }
    }
}
