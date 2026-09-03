using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// Leaderboard of everyone this player has met in ranked lobbies. Ranks live on each player's own PC (there is no
    /// server), so this board is built from the rank points other players report when they are in a lobby with you,
    /// plus your own. Persisted with the ranks file.
    /// </summary>
    public static class Leaderboard
    {
        [Serializable] public class Entry { public string SteamId; public string Name; public int Points; public string LastSeen; }

        private static float _nextPoll;

        /// <summary>Call every frame: records the rank points of everyone in the current lobby once a second.</summary>
        public static void Update()
        {
            if (!ModState.IsActive || !ClientMatchView.HasState || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 1f;
            bool changed = false;
            foreach (var e in ClientMatchView.Players)
            {
                if (!e.HasMod) continue;
                var player = PlayerManager.Players.FirstOrDefault(p => p && p.OwnerId == e.Id);
                string steamId = player ? player.SteamID.ToString() : "";
                if (string.IsNullOrEmpty(steamId) || steamId == "0") continue;
                changed |= RankService.RecordSeen(steamId, e.Name, e.RankPoints);
            }
            if (changed) RankService.SaveNow();
        }

        /// <summary>True when the global (online) leaderboard is the source of the list.</summary>
        public static bool IsGlobal => CloudRanks.Enabled && CloudRanks.HasData;

        /// <summary>Top N players by points, the local player included: the global board when available, else players met locally.</summary>
        public static List<Entry> Top(int n)
        {
            var list = IsGlobal
                ? CloudRanks.Top.Select(e => new Entry { SteamId = e.SteamId, Name = e.Name, Points = e.Points, LastSeen = e.LastSeen }).ToList()
                : RankService.KnownPlayers.Select(e => new Entry { SteamId = e.SteamId, Name = e.Name, Points = e.Points, LastSeen = e.LastSeen }).ToList();
            string me = RankService.LocalId;
            if (!list.Any(k => k.SteamId == me))
                list.Add(new Entry { SteamId = me, Name = LocalName(), Points = RankService.Points, LastSeen = "now" });
            else
            {
                var mine = list.First(k => k.SteamId == me);
                mine.Points = RankService.Points; mine.Name = LocalName(); mine.LastSeen = "now";
            }
            return list.OrderByDescending(k => k.Points).ThenBy(k => k.Name).Take(n).ToList();
        }

        private static string LocalName()
        {
            try { return Player.LocalPlayer ? Player.LocalPlayer.SteamName : Steamworks.SteamFriends.GetPersonaName(); }
            catch (Exception) { return "You"; }
        }
    }
}
