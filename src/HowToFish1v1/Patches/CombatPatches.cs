using HarmonyLib;
using UnityEngine;

namespace HowToFish1v1.Patches
{
    /// <summary>Force friendly fire on and remove the game's 0.25x player-vs-player damage scale while a match is active.</summary>
    [HarmonyPatch]
    internal static class CombatPatches
    {
        [HarmonyPatch(typeof(ServerSettings), nameof(ServerSettings.UseFriendlyFire), MethodType.Getter)]
        [HarmonyPostfix]
        private static void ForceFriendlyFire(ref bool __result)
        {
            if (ModState.IsActive) __result = true;
        }

        /// <summary>Set by the knife and ricochets around their own LocalHit calls: send exactly this damage, do not replace it.</summary>
        public static bool SendingRaw;

        // LocalHit multiplies damage by _playerDamageMultiplier (0.25) and the server damage multiplier. Replace the
        // game's damage with the gun's fixed ranked damage and pre-scale so the value that reaches the server is exact.
        [HarmonyPatch(typeof(PlayerVitals), nameof(PlayerVitals.LocalHit))]
        [HarmonyPrefix]
        private static void ScalePlayerDamage(PlayerVitals __instance, Player playerWhoHit, ref int damage, bool fromNpc)
        {
            if (!ModState.IsActive || fromNpc) return;
            float gameScale = Traverse.Create(__instance).Field<float>("_playerDamageMultiplier").Value;
            if (gameScale <= 0f) gameScale = 0.25f;
            float serverScale = 1f;
            try { serverScale = ServerSettings.DamageMultiplier; } catch (System.Exception) { }
            if (serverScale <= 0f) serverScale = 1f;
            int want = damage;
            if (!SendingRaw)
            {
                string gun = playerWhoHit && playerWhoHit.Holding ? Match.LoadoutService.DisplayName(playerWhoHit.Holding.HeldItem) : "";
                want = HowToFish1v1.Core.GunBalance.DamageFor(gun);
            }
            damage = Mathf.RoundToInt(want / gameScale / serverScale);
        }
    }
}
