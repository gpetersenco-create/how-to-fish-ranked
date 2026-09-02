using HarmonyLib;

namespace HowToFish1v1.Patches
{
    /// <summary>No disk writes and no saved world clutter while a match is active.</summary>
    [HarmonyPatch]
    internal static class SavePatches
    {
        private static bool Skip()
        {
            if (!ModState.BlockSaves) return true;
            Plugin.Log.LogInfo("Save suppressed during 1v1");
            return false;
        }

        [HarmonyPatch(typeof(SaveSystem), "SaveServer")] [HarmonyPrefix] private static bool NoServerSave() => Skip();
        [HarmonyPatch(typeof(SaveSystem), "SaveLocal")] [HarmonyPrefix] private static bool NoLocalSave() => Skip();
        [HarmonyPatch(typeof(SaveSystem), "DeleteServer")] [HarmonyPrefix] private static bool NoDelete() => Skip();
        [HarmonyPatch(typeof(SaveManager), "LoadWorldItems")] [HarmonyPrefix] private static bool NoWorldItems() => !ModState.BlockSaves;
    }
}
