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

        /// <summary>True from hosting/joining through the Ranked menu until the connection drops; blocks saves for the whole session.</summary>
        public static bool RankedSession;

        /// <summary>Unscaled time until which player teleports use the instant path even when the mode is inactive (island return).</summary>
        public static float ForceInstantTeleportUntil = -1f;

        public static bool InstantTeleports => IsActive || Time.unscaledTime < ForceInstantTeleportUntil;

        /// <summary>Owner id of the local player, or -1 when not in a game.</summary>
        public static int LocalOwnerId => Player.LocalPlayer ? Player.LocalPlayer.OwnerId : -1;

        public static bool IsActive => Phase != MatchPhase.Inactive;

        public static bool BlockSaves => IsActive || RankedSession;

        public static bool FreezeInputs =>
            PanelOpen || Phase == MatchPhase.Countdown || Phase == MatchPhase.RoundEnd || Phase == MatchPhase.MatchEnd
            || Match.KillCam.UsesPlayerCam;   // watching a killcam while alive: no shooting or looking around

        /// <summary>Team pad slot for a player (side, index within team, team size); null in free-for-all or when unknown. Set by ClientMatchView.</summary>
        public static Func<int, (Side side, int index, int count)?> SpawnSlotLookup;

        /// <summary>World spawn for a pad slot. Set by ArenaBuilder when built.</summary>
        public static Func<Side, int, int, (Vector3 pos, float yaw)?> SpawnLookup;

        public static event Action<Player> KillDetected;

        public static void RaiseKill(Player p) => KillDetected?.Invoke(p);

        public static bool TryGetSpawn(int ownerId, out Vector3 pos, out float yaw)
        {
            pos = Vector3.zero; yaw = 0f;
            var slot = SpawnSlotLookup?.Invoke(ownerId);
            if (slot == null) return false;
            var s = SpawnLookup?.Invoke(slot.Value.side, slot.Value.index, slot.Value.count);
            if (s == null) return false;
            pos = s.Value.pos; yaw = s.Value.yaw;
            return true;
        }

        public static void Reset()
        {
            Phase = MatchPhase.Inactive;
            PanelOpen = false;
            RankedSession = false;
            SpawnSlotLookup = null;
        }
    }
}
