using BepInEx.Configuration;
using HowToFish1v1.Core;
using UnityEngine;

namespace HowToFish1v1
{
    public sealed class ModConfig
    {
        public ConfigEntry<KeyCode> PanelKey;
        public ConfigEntry<int> RoundsToWin;
        public ConfigEntry<int> KillsToWin;
        public ConfigEntry<float> CountdownSeconds;
        public ConfigEntry<float> FfaRespawnSeconds;
        public ConfigEntry<float> RoundEndSeconds;
        public ConfigEntry<float> DamageMultiplier;
        public ConfigEntry<int> MaxLoadoutGuns;
        public ConfigEntry<string> RankNames;
        public ConfigEntry<int> RankPointsPerTier;
        public ConfigEntry<bool> ShareRank;
        public ConfigEntry<string> LeaderboardUrl;
        public ConfigEntry<bool> AutoUpdate;
        public ConfigEntry<string> UpdateManifestUrl;
        public ConfigEntry<bool> SoloDebug;
        public ConfigEntry<bool> AutoHostOffline;
        public ConfigEntry<bool> AutoSoloMatch;
        public ConfigEntry<int> AutoSoloMap;
        public ConfigEntry<int> AutoSoloMode;
        public ConfigEntry<bool> DumpMenu;

        public ModConfig(ConfigFile file)
        {
            PanelKey = file.Bind("General", "PanelKey", KeyCode.F5, "Key that opens/closes the match panel inside a session.");
            RoundsToWin = file.Bind("Rules", "RoundsToWin", 6, "Round wins needed to take a 1v1 / 2v2 / 3v3 match.");
            KillsToWin = file.Bind("Rules", "KillsToWin", 10, "Kills needed to win a free-for-all.");
            CountdownSeconds = file.Bind("Rules", "CountdownSeconds", 3f, "Freeze time before each round goes live.");
            FfaRespawnSeconds = file.Bind("Rules", "FfaRespawnSeconds", 7f, "Seconds before a free-for-all respawn (the killcam plays during this).");
            RoundEndSeconds = file.Bind("Rules", "RoundEndSeconds", 7f, "Pause after a round ends before the next countdown (the killcam plays during this).");
            DamageMultiplier = file.Bind("Rules", "DamageMultiplier", 1f, "Player-vs-player damage scale. 1.0 = full weapon damage (the game normally uses 0.25).");
            MaxLoadoutGuns = file.Bind("Rules", "MaxLoadoutGuns", 2, "How many guns each player may pick.");
            RankNames = file.Bind("Ranks", "RankNames", RankLadder.DefaultNames, "Comma-separated rank names from lowest to highest.");
            RankPointsPerTier = file.Bind("Ranks", "PointsPerTier", 100, "Points per rank tier. Win +20, loss -10 (free-for-all loss -5).");
            ShareRank = file.Bind("Leaderboard", "ShareRank", true, "Report your Steam id, name and rank stats to the global leaderboard, and show it in the Ranked menu.");
            LeaderboardUrl = file.Bind("Leaderboard", "DatabaseUrl", "https://how-to-fish-ranked-default-rtdb.firebaseio.com", "Global leaderboard database URL.");
            AutoUpdate = file.Bind("Updates", "AutoUpdate", true, "Check for a newer mod version at startup and install it for the next launch.");
            UpdateManifestUrl = file.Bind("Updates", "ManifestUrl", "https://raw.githubusercontent.com/gpetersenco-create/how-to-fish-ranked/main/updates/manifest.json", "Where the updater looks for the latest version.");
            SoloDebug = file.Bind("Debug", "SoloDebug", false, "Allow starting a match with only one player, for testing.");
            AutoHostOffline = file.Bind("Debug", "AutoHostOffline", false, "Testing only: automatically host an offline session a few seconds after the main menu appears.");
            AutoSoloMatch = file.Bind("Debug", "AutoSoloMatch", false, "Testing only (needs SoloDebug): script a solo match on the host and log every step.");
            AutoSoloMap = file.Bind("Debug", "AutoSoloMap", 0, "Testing only: map index the scripted solo match uses.");
            AutoSoloMode = file.Bind("Debug", "AutoSoloMode", 0, "Testing only: mode the scripted solo match uses (0=1v1, 1=2v2, 2=3v3, 3=FFA).");
            DumpMenu = file.Bind("Debug", "DumpMenu", false, "Testing only: log the main menu UI hierarchy once.");
        }
    }
}
