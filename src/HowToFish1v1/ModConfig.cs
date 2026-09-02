using BepInEx.Configuration;
using UnityEngine;

namespace HowToFish1v1
{
    public sealed class ModConfig
    {
        public ConfigEntry<KeyCode> PanelKey;
        public ConfigEntry<int> RoundsToWin;
        public ConfigEntry<float> CountdownSeconds;
        public ConfigEntry<float> DamageMultiplier;
        public ConfigEntry<int> MaxLoadoutGuns;
        public ConfigEntry<bool> SoloDebug;
        public ConfigEntry<bool> AutoHostOffline;
        public ConfigEntry<bool> AutoSoloMatch;
        public ConfigEntry<int> AutoSoloMap;

        public ModConfig(ConfigFile file)
        {
            PanelKey = file.Bind("General", "PanelKey", KeyCode.F5, "Key that opens/closes the 1v1 panel.");
            RoundsToWin = file.Bind("Rules", "RoundsToWin", 6, "Round wins needed to take the match.");
            CountdownSeconds = file.Bind("Rules", "CountdownSeconds", 3f, "Freeze time before each round goes live.");
            DamageMultiplier = file.Bind("Rules", "DamageMultiplier", 1f, "Player-vs-player damage scale. 1.0 = full weapon damage (the game normally uses 0.25).");
            MaxLoadoutGuns = file.Bind("Rules", "MaxLoadoutGuns", 2, "How many guns each player may pick.");
            SoloDebug = file.Bind("Debug", "SoloDebug", false, "Allow starting a match with only one player, for testing.");
            AutoHostOffline = file.Bind("Debug", "AutoHostOffline", false, "Testing only: automatically host an offline session a few seconds after the main menu appears.");
            AutoSoloMatch = file.Bind("Debug", "AutoSoloMatch", false, "Testing only (needs SoloDebug): script a solo match on the host and log every step.");
            AutoSoloMap = file.Bind("Debug", "AutoSoloMap", 0, "Testing only: map index the scripted solo match uses.");
        }
    }
}
