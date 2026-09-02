using HarmonyLib;
using UnityEngine;

namespace HowToFish1v1.Patches
{
    /// <summary>
    /// The game's TeleportPlayer RPC lands on the owner as a deferred rigidbody MovePosition, which loses the race against
    /// island unloads and the anti-drown logic. While a match is active, use the instant path (the one respawn uses).
    /// </summary>
    [HarmonyPatch]
    internal static class TeleportPatches
    {
        [HarmonyPatch(typeof(Player), "RpcLogic___RPCTeleport___2734710480")]
        [HarmonyPrefix]
        private static bool InstantTeleport(Player __instance, Vector3 __1, float __2)
        {
            if (!ModState.InstantTeleports) return true;
            __instance.LocalTeleport(__1, __2, true);
            return false;
        }
    }
}
