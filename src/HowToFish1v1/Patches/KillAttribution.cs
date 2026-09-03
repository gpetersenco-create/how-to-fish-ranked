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
        private static void RememberAttacker(Player __0, ref int __1, Vector3 __3, Player __5)
        {
            if (!ModState.IsActive || !__0 || !__5 || __1 <= 0) return;
            _lastAttacker[__0.OwnerId] = __5.OwnerId;
            int reported = __1;
            // The host decides the damage: fixed per gun, knife and ricochet values recognised, anything else replaced.
            try
            {
                string gun = __5.Holding ? Match.LoadoutService.DisplayName(__5.Holding.HeldItem) : "";
                float dist = __5.Transform && __0.Transform ? Vector3.Distance(__5.Transform.position, __0.Transform.position) : 99f;
                __1 = HowToFish1v1.Core.GunBalance.Authoritative(gun, reported, dist);
            }
            catch (System.Exception) { }
            try { Match.AntiCheat.OnHit(__5, __0, reported, __3); } catch (System.Exception e) { Plugin.Log.LogDebug("anti-cheat: " + e.Message); }
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
