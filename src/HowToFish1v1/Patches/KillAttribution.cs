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
        private static readonly Dictionary<int, (HowToFish1v1.Core.KillKind kind, bool airborne)> _lastHit = new Dictionary<int, (HowToFish1v1.Core.KillKind, bool)>();

        // RpcLogic___HitPlayer___2449261505(Player victim, int damage, Vector3 force, Vector3 pos, byte type, Player attacker)
        [HarmonyPatch(typeof(Server), "RpcLogic___HitPlayer___2449261505")]
        [HarmonyPrefix]
        private static void RememberAttacker(Player __0, ref int __1, Vector3 __3, Player __5)
        {
            if (!ModState.IsActive || !__0 || !__5 || __1 <= 0) return;
            _lastAttacker[__0.OwnerId] = __5.OwnerId;
            int reported = __1;
            // The host decides the damage: fixed per gun, knife and ricochet values recognised, anything else replaced.
            var kind = HowToFish1v1.Core.KillKind.Bullet;
            bool airborne = false;
            try
            {
                string gun = __5.Holding ? Match.LoadoutService.DisplayName(__5.Holding.HeldItem) : "";
                float dist = __5.Transform && __0.Transform ? Vector3.Distance(__5.Transform.position, __0.Transform.position) : 99f;
                int authoritative = HowToFish1v1.Core.GunBalance.Authoritative(gun, reported, dist);
                if (authoritative == HowToFish1v1.Core.GunBalance.KnifeDamage) kind = HowToFish1v1.Core.KillKind.Knife;
                else if (authoritative == HowToFish1v1.Core.GunBalance.RicochetDamageFor(gun) && authoritative != HowToFish1v1.Core.GunBalance.DamageFor(gun)) kind = HowToFish1v1.Core.KillKind.Ricochet;
                // Mode rules: one in the chamber (every bullet kills) and the one-shot killstreak.
                var machine = Plugin.Host?.Machine;
                if (machine != null && kind == HowToFish1v1.Core.KillKind.Bullet)
                {
                    if (HowToFish1v1.Core.MatchModes.OneBullet(machine.State.Mode)) authoritative = HowToFish1v1.Core.GunBalance.Health;
                    var slot = machine.State.Slot(__5.OwnerId);
                    if (slot != null && slot.OneShot) authoritative = HowToFish1v1.Core.GunBalance.Health;
                }
                __1 = authoritative;
                try { airborne = __5.Movement && !__5.Movement.Grounded; } catch (System.Exception) { }
            }
            catch (System.Exception) { }
            _lastHit[__0.OwnerId] = (kind, airborne);
            try { Match.AntiCheat.OnHit(__5, __0, reported, __3); } catch (System.Exception e) { Plugin.Log.LogDebug("anti-cheat: " + e.Message); }
        }

        /// <summary>Owner id of the last player that hit this player, or -1. Clears the record.</summary>
        public static int Take(int victimOwnerId)
        {
            if (!_lastAttacker.TryGetValue(victimOwnerId, out int killer)) return -1;
            _lastAttacker.Remove(victimOwnerId);
            return killer;
        }

        /// <summary>Attacker plus how the last hit was dealt; clears the record.</summary>
        public static (int killer, HowToFish1v1.Core.KillKind kind, bool airborne) TakeDetail(int victimOwnerId)
        {
            int killer = Take(victimOwnerId);
            var kind = HowToFish1v1.Core.KillKind.Bullet; bool air = false;
            if (_lastHit.TryGetValue(victimOwnerId, out var h)) { kind = h.kind; air = h.airborne; _lastHit.Remove(victimOwnerId); }
            return (killer, kind, air);
        }

        public static void Clear() { _lastAttacker.Clear(); _lastHit.Clear(); }
    }
}
