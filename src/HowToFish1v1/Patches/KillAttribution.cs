using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HowToFish1v1.Patches
{
    /// <summary>Server only: remembers who landed the killing blow so free-for-all can credit the kill.</summary>
    [HarmonyPatch]
    internal static class KillAttribution
    {
        private static readonly Dictionary<int, int> _lastAttacker = new Dictionary<int, int>();

        // RpcLogic___HitPlayer___2449261505(Player victim, int damage, Vector3 force, Vector3 pos, byte type, Player attacker)
        [HarmonyPatch(typeof(Server), "RpcLogic___HitPlayer___2449261505")]
        [HarmonyPostfix]
        private static void RememberAttacker(Player __0, Player __5)
        {
            if (!ModState.IsActive || !__0 || !__5) return;
            if (__0.Vitals.Health <= 0) _lastAttacker[__0.OwnerId] = __5.OwnerId;
        }

        /// <summary>Owner id of the last attacker that brought this player to zero health, or -1. Clears the record.</summary>
        public static int Take(int victimOwnerId)
        {
            if (!_lastAttacker.TryGetValue(victimOwnerId, out int killer)) return -1;
            _lastAttacker.Remove(victimOwnerId);
            return killer;
        }

        public static void Clear() => _lastAttacker.Clear();
    }
}
