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

        // LocalHit multiplies damage by _playerDamageMultiplier (0.25) for player hits. Pre-scale so the net result is damage * cfg.
        [HarmonyPatch(typeof(PlayerVitals), nameof(PlayerVitals.LocalHit))]
        [HarmonyPrefix]
        private static void ScalePlayerDamage(PlayerVitals __instance, ref int damage, bool fromNpc)
        {
            if (!ModState.IsActive || fromNpc) return;
            float gameScale = Traverse.Create(__instance).Field<float>("_playerDamageMultiplier").Value;
            if (gameScale <= 0f) gameScale = 0.25f;
            float want = Mathf.Max(0f, Plugin.Cfg.DamageMultiplier.Value);
            damage = Mathf.RoundToInt(damage * want / gameScale);
        }
    }
}
