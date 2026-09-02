using HarmonyLib;

namespace HowToFish1v1.Patches
{
    [HarmonyPatch]
    internal static class InputPatches
    {
        [HarmonyPatch(typeof(Player), nameof(Player.BlockInputs), MethodType.Getter)]
        [HarmonyPostfix]
        private static void FreezeDuringCountdown(ref bool __result)
        {
            if (ModState.FreezeInputs) __result = true;
        }

        // Keep the cursor free while the 1v1 panel is open, whatever the game asks for.
        [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ToggleMouse))]
        [HarmonyPrefix]
        private static void UnlockForPanel(ref bool unlock)
        {
            if (ModState.PanelOpen) unlock = true;
        }
    }
}
