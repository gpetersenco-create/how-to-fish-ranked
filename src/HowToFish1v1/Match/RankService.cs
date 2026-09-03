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
        [Serializable]
        public class HistoryEntry { public string Mode; public string Map; public bool Won; public int Delta; public int Kills; public int Deaths; public string When; }
        [Serializable] public class SeasonEntry { public int Season; public int Points; public string Tier; public int Wins; public int Losses; }

        [Serializable]
        private class RankEntry
        {
            public string SteamId; public int Points; public int Wins; public int Losses; public int Kills; public int Deaths; public int Peak;
            public List<HistoryEntry> History = new List<HistoryEntry>();
            public int Season = 0;
            public List<SeasonEntry> Seasons = new List<SeasonEntry>();
        }

        [Serializable] private class RankFile { public List<RankEntry> Entries = new List<RankEntry>(); public List<Leaderboard.Entry> Known = new List<Leaderboard.Entry>(); }

        public static IReadOnlyList<Leaderboard.Entry> KnownPlayers => _file.Known ?? (_file.Known = new List<Leaderboard.Entry>());
        public static string LocalId => LocalSteamId();

        /// <summary>Remembers another player's reported rank points; returns true if anything changed.</summary>
        public static bool RecordSeen(string steamId, string name, int points)
        {
            if (string.IsNullOrEmpty(steamId) || steamId == LocalSteamId()) return false;
            if (_file.Known == null) _file.Known = new List<Leaderboard.Entry>();
            var e = _file.Known.Find(k => k.SteamId == steamId);
            string when = DateTime.Now.ToString("MMM d");
            if (e == null) { _file.Known.Add(new Leaderboard.Entry { SteamId = steamId, Name = name ?? "", Points = points, LastSeen = when }); return true; }
            bool changed = e.Points != points || e.Name != (name ?? "") || e.LastSeen != when;
            e.Points = points; e.Name = name ?? ""; e.LastSeen = when;
            return changed;
        }

        public static void SaveNow() => Save();

        private static RankFile _file;
        private static RankEntry _me;
        private static string _path;

        public static RankLadder Ladder { get; private set; }
        public static int Points => Me.Points;
        public static int Wins => Me.Wins;
        public static int Losses => Me.Losses;
        public static int Kills => Me.Kills;
        public static int Deaths => Me.Deaths;
        public static int Peak => Me.Peak;
        public static int MatchesPlayed => Me.Wins + Me.Losses;
        public static float WinRate => MatchesPlayed == 0 ? 0f : (float)Me.Wins / MatchesPlayed;
        public static float KdRatio => Me.Deaths == 0 ? Me.Kills : (float)Me.Kills / Me.Deaths;
        public static IReadOnlyList<HistoryEntry> History => Me.History;
        public static string RankName => Ladder.TierName(Points);
        public static string LastResultText { get; private set; } = "";
        public static string LastSeasonText { get; private set; } = "";
        public static IReadOnlyList<SeasonEntry> SeasonHistory => Me.Seasons ?? (Me.Seasons = new List<SeasonEntry>());

        /// <summary>A new season has started: archive the old standing, reset the points, tell the leaderboard.</summary>
        public static void RollSeason()
        {
            int now = Seasons.Current;
            if (Me.Season == now) return;
            if (Me.Season > 0 && (Me.Points > 0 || Me.Wins + Me.Losses > 0))
            {
                if (Me.Seasons == null) Me.Seasons = new List<SeasonEntry>();
                var entry = new SeasonEntry { Season = Me.Season, Points = Me.Points, Tier = Ladder.TierName(Me.Points), Wins = Me.Wins, Losses = Me.Losses };
                Me.Seasons.Insert(0, entry);
                LastSeasonText = $"Season {entry.Season} ended at {entry.Tier} ({entry.Points} RP).";
                Plugin.Instance.StartCoroutine(CloudRanks.ReportSeason(entry.Season, entry.Points, entry.Tier));
                Plugin.Log.LogInfo(LastSeasonText);
            }
            Me.Points = 0;
            Me.Season = now;
            Save();
        }

        public static void Init()
        {
            Ladder = new RankLadder(Plugin.Cfg.RankNames.Value, Plugin.Cfg.RankPointsPerTier.Value);
            _path = Path.Combine(Paths.ConfigPath, "HowToFish1v1.ranks.json");
            Load();
            RollSeason();
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
                if (_me.History == null) _me.History = new List<HistoryEntry>();
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
        public static string ApplyResult(bool won, bool ffa, int kills = 0, int deaths = 0, string mode = "", string map = "")
        {
            int before = Points;
            int after = Ladder.Apply(before, won, ffa);
            Me.Points = after;
            if (won) Me.Wins++; else Me.Losses++;
            Me.Kills += Math.Max(0, kills);
            Me.Deaths += Math.Max(0, deaths);
            Me.Peak = Math.Max(Me.Peak, after);
            Me.History.Insert(0, new HistoryEntry { Mode = mode, Map = map, Won = won, Delta = after - before, Kills = kills, Deaths = deaths, When = DateTime.Now.ToString("MMM d HH:mm") });
            while (Me.History.Count > 8) Me.History.RemoveAt(Me.History.Count - 1);
            Save();
            int delta = after - before;
            string sign = delta >= 0 ? "+" : "";
            string tierBefore = Ladder.TierName(before), tierAfter = Ladder.TierName(after);
            string change = tierBefore == tierAfter ? "" : (after > before ? "  RANK UP!" : "  rank down");
            LastResultText = $"{sign}{delta} points: {tierAfter}{change}";
            Plugin.Log.LogInfo($"Rank: {before} -> {after} ({tierAfter}) W{Me.Wins} L{Me.Losses} K{Me.Kills} D{Me.Deaths}");
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
