using HarmonyLib;

namespace HowToFish1v1.Patches
{
    /// <summary>Turns off world systems that make no sense inside the arena. Every prefix returns false only while a match is active.</summary>
    [HarmonyPatch]
    internal static class SuppressionPatches
    {
        private static bool Skip() => !ModState.IsActive;

        [HarmonyPatch(typeof(CreatureManager), "TickUpdate")] [HarmonyPrefix] private static bool NoFish() => Skip();
        [HarmonyPatch(typeof(BirdManager), "ServerTickUpdate")] [HarmonyPrefix] private static bool NoBirdTick() => Skip();
        [HarmonyPatch(typeof(BirdManager), "AddFlyingBird")] [HarmonyPrefix] private static bool NoBirds() => Skip();
        [HarmonyPatch(typeof(AlbatrossSpawner), "TickUpdate")] [HarmonyPrefix] private static bool NoAlbatross() => Skip();
        [HarmonyPatch(typeof(BossManager), "InitializeBossFight")] [HarmonyPrefix] private static bool NoBoss() => Skip();
        [HarmonyPatch(typeof(NPCManager), "AddNpc")] [HarmonyPrefix] private static bool NoNpc() => Skip();
        [HarmonyPatch(typeof(ItemSpawner), "Start")] [HarmonyPrefix] private static bool NoLoot() => Skip();
        [HarmonyPatch(typeof(IslandSpawner), "OnTriggerEnter")] [HarmonyPrefix] private static bool NoIslandHop() => Skip();
        [HarmonyPatch(typeof(TutorialManager), "AddTutorial")] [HarmonyPrefix] private static bool NoTutorial() => Skip();
        [HarmonyPatch(typeof(PlayerVitals), "LowerFullnessTick")] [HarmonyPrefix] private static bool NoHunger() => Skip();
        [HarmonyPatch(typeof(AutoSaver), "Start")] [HarmonyPrefix] private static bool NoAutosave() => Skip();

        // The island's "you can't progress if you leave your friends" zone fires its exit event when the island unloads.
        [HarmonyPatch(typeof(PlayerUI), "ToggleIslandWarning")] [HarmonyPrefix]
        private static bool NoIslandWarning(bool to) => !(to && (ModState.IsActive || ModState.RankedSession));
    }
}
