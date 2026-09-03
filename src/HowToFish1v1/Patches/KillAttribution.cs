using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HowToFish1v1.Patches
{
    /// <summary>
    /// Server only: remembers who last hit each player so a death can be credited. The record is written BEFORE the game
    /// applies the hit, because the death (and our kill event) happens inside the hit itself.
    /// </summary>
    [HarmonyPatch]
    internal static class KillAttribution
    {
        private static readonly Dictionary<int, int> _lastAttacker = new Dictionary<int, int>();

        // RpcLogic___HitPlayer___2449261505(Player victim, int damage, Vector3 force, Vector3 pos, byte type, Player attacker)
        [HarmonyPatch(typeof(Server), "RpcLogic___HitPlayer___2449261505")]
        [HarmonyPrefix]
        private static void RememberAttacker(Player __0, int __1, Vector3 __3, Player __5)
        {
            if (!ModState.IsActive || !__0 || !__5 || __1 <= 0) return;
            _lastAttacker[__0.OwnerId] = __5.OwnerId;
            try { Match.AntiCheat.OnHit(__5, __0, __1, __3); } catch (System.Exception e) { Plugin.Log.LogDebug("anti-cheat: " + e.Message); }
        }

        /// <summary>Owner id of the last player that hit this player, or -1. Clears the record.</summary>
        public static int Take(int victimOwnerId)
        {
            if (!_lastAttacker.TryGetValue(victimOwnerId, out int killer)) return -1;
            _lastAttacker.Remove(victimOwnerId);
            return killer;
        }

        public static void Clear() => _lastAttacker.Clear();
    }
}
