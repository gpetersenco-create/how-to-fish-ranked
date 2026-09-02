using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using HowToFish1v1.Core;
using Steamworks;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>Local rank storage, keyed by Steam id, in BepInEx/config/HowToFish1v1.ranks.json.</summary>
    public static class RankService
    {
        [Serializable] private class RankEntry { public string SteamId; public int Points; public int Wins; public int Losses; }
        [Serializable] private class RankFile { public List<RankEntry> Entries = new List<RankEntry>(); }

        private static RankFile _file;
        private static RankEntry _me;
        private static string _path;

        public static RankLadder Ladder { get; private set; }
        public static int Points => Me.Points;
        public static int Wins => Me.Wins;
        public static int Losses => Me.Losses;
        public static string RankName => Ladder.TierName(Points);
        public static string LastResultText { get; private set; } = "";

        public static void Init()
        {
            Ladder = new RankLadder(Plugin.Cfg.RankNames.Value, Plugin.Cfg.RankPointsPerTier.Value);
            _path = Path.Combine(Paths.ConfigPath, "HowToFish1v1.ranks.json");
            Load();
        }

        private static RankEntry Me
        {
            get
            {
                if (_me != null) return _me;
                string id = LocalSteamId();
                _me = _file.Entries.Find(e => e.SteamId == id);
                if (_me == null)
                {
                    _me = new RankEntry { SteamId = id };
                    _file.Entries.Add(_me);
                }
                return _me;
            }
        }

        private static string LocalSteamId()
        {
            try
            {
                var id = SteamUser.GetSteamID().m_SteamID;
                if (id != 0) return id.ToString();
            }
            catch (Exception) { }
            return "local";
        }

        /// <summary>Applies one match result and returns a banner line such as "+20 points: Bottom Feeder (rank up!)".</summary>
        public static string ApplyResult(bool won, bool ffa)
        {
            int before = Points;
            int after = Ladder.Apply(before, won, ffa);
            Me.Points = after;
            if (won) Me.Wins++; else Me.Losses++;
            Save();
            int delta = after - before;
            string sign = delta >= 0 ? "+" : "";
            string tierBefore = Ladder.TierName(before), tierAfter = Ladder.TierName(after);
            string change = tierBefore == tierAfter ? "" : (after > before ? "  RANK UP!" : "  rank down");
            LastResultText = $"{sign}{delta} points: {tierAfter}{change}";
            Plugin.Log.LogInfo($"Rank: {before} -> {after} ({tierAfter}) W{Me.Wins} L{Me.Losses}");
            return LastResultText;
        }

        private static void Load()
        {
            _file = new RankFile();
            _me = null;
            try
            {
                if (File.Exists(_path))
                {
                    var loaded = JsonUtility.FromJson<RankFile>(File.ReadAllText(_path));
                    if (loaded != null && loaded.Entries != null) _file = loaded;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not read ranks file: " + e.Message);
            }
        }

        private static void Save()
        {
            try
            {
                File.WriteAllText(_path, JsonUtility.ToJson(_file, true));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not write ranks file: " + e.Message);
            }
        }
    }
}
