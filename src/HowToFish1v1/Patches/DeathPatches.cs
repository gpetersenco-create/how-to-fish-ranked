using HarmonyLib;
using UnityEngine;

namespace HowToFish1v1.Patches
{
    [HarmonyPatch]
    internal static class DeathPatches
    {
        // Server-side death funnel: report the kill to the host controller.
        [HarmonyPatch(typeof(PlayerDying), nameof(PlayerDying.ServerDie))]
        [HarmonyPostfix]
        private static void OnServerDie(PlayerDying __instance)
        {
            if (!ModState.IsActive) return;
            var player = Traverse.Create(__instance).Field<Player>("_player").Value;
            if (player) ModState.RaiseKill(player);
        }

        // Respawn lands on the player's assigned arena pad instead of the island spawn.
        [HarmonyPatch(typeof(PlayerDying), nameof(PlayerDying.ResurrectEffect))]
        [HarmonyPrefix]
        private static void OverrideSpawn(PlayerDying __instance, bool respawned, ref (Vector3, float, bool) __state)
        {
            __state = (SpawnManager.PlayerSpawnPos, SpawnManager.PlayerSpawnRot, false);
            if (!ModState.IsActive || !respawned) return;
            var player = Traverse.Create(__instance).Field<Player>("_player").Value;
            if (player && ModState.TryGetSpawn(player.OwnerId, out var pos, out var yaw))
            {
                SpawnManager.PlayerSpawnPos = pos;
                SpawnManager.PlayerSpawnRot = yaw;
                __state.Item3 = true;
            }
        }

        [HarmonyPatch(typeof(PlayerDying), nameof(PlayerDying.ResurrectEffect))]
        [HarmonyPostfix]
        private static void RestoreSpawn((Vector3, float, bool) __state)
        {
            if (!__state.Item3) return;
            SpawnManager.PlayerSpawnPos = __state.Item1;
            SpawnManager.PlayerSpawnRot = __state.Item2;
        }

        // Kill cam: after the game's death camera orbits the ragdoll, override it to follow the killer.
        [HarmonyPatch(typeof(PlayerDeathCam), "LateUpdate")]
        [HarmonyPostfix]
        private static void KillCamFollow(PlayerDeathCam __instance)
        {
            if (ModState.IsActive) Match.KillCam.ApplyDeathCam(__instance);
        }

        // Final killcam while alive: override the player camera after it has positioned itself.
        [HarmonyPatch(typeof(PlayerCamera), "Update")]
        [HarmonyPostfix]
        private static void FinalKillCam(PlayerCamera __instance)
        {
            if (ModState.IsActive && Match.KillCam.IsFinal && __instance.Owner.IsLocalClient) Match.KillCam.ApplyPlayerCam(__instance);
        }

        // Tab opens the fish journal in the base game; during matches Tab is the scoreboard.
        [HarmonyPatch(typeof(PlayerThinking), "ThinkingInput")]
        [HarmonyPrefix]
        private static bool NoJournal() => !ModState.IsActive;

        // The "hold to give up" respawn would drop the inventory and move the boat; the host resets players instead.
        [HarmonyPatch(typeof(PlayerDying), "LocalRespawn")]
        [HarmonyPrefix]
        private static bool BlockGiveUp() => !ModState.IsActive;
    }
}
