using System;
using HarmonyLib;
using UnityEngine;

namespace HowToFish1v1.Patches
{
    /// <summary>Testing only: logs every damage application on the host while AutoSoloMatch is on.</summary>
    [HarmonyPatch]
    internal static class DebugPatches
    {
        [HarmonyPatch(typeof(PlayerVitals), nameof(PlayerVitals.TakeDamage))]
        [HarmonyPrefix]
        private static void LogDamage(PlayerVitals __instance, int amount)
        {
            if (!ModState.IsActive || !Plugin.Cfg.AutoSoloMatch.Value) return;
            Plugin.Log.LogInfo($"TakeDamage {amount} hp={__instance.Health} fullness={__instance.Fullness} poison={__instance._syncedPoison.Value} fire={__instance._syncedFire.Value}\n{Environment.StackTrace}");
        }
    }
}
