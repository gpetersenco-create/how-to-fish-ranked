using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// Global leaderboard backend: a Firebase Realtime Database reached over plain HTTPS. Each install reports its own
    /// Steam id, name and rank stats under /players/{steamId}; the leaderboard reads the top entries by points.
    /// Disabled when the URL is empty or the ShareRank option is off.
    /// </summary>
    public static class CloudRanks
    {
        [Serializable] private class Record { public string name; public int points; public int wins; public int losses; public int kills; public int deaths; public string updated; public string version; }

        public static List<Leaderboard.Entry> Top { get; private set; } = new List<Leaderboard.Entry>();
        public static bool Enabled => Plugin.Cfg.ShareRank.Value && !string.IsNullOrEmpty(BaseUrl);
        public static string Status { get; private set; } = "";
        public static bool HasData { get; private set; }

        private static string BaseUrl => (Plugin.Cfg.LeaderboardUrl.Value ?? "").Trim().TrimEnd('/');
        private static float _nextRefresh;
        private static bool _busy;

        /// <summary>Uploads this player's current stats. Call after a match result and at startup.</summary>
        public static IEnumerator Report()
        {
            if (!Enabled) yield break;
            string id = RankService.LocalId;
            if (string.IsNullOrEmpty(id) || id == "local") yield break;
            string name = "";
            try { name = Steamworks.SteamFriends.GetPersonaName(); } catch (Exception) { }
            var rec = new Record
            {
                name = name, points = RankService.Points, wins = RankService.Wins, losses = RankService.Losses,
                kills = RankService.Kills, deaths = RankService.Deaths, updated = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"), version = Plugin.Version
            };
            var body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(rec));
            using (var req = new UnityWebRequest($"{BaseUrl}/players/{id}.json", "PUT"))
            {
                req.uploadHandler = new UploadHandlerRaw(body);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) Plugin.Log.LogInfo("Leaderboard report failed: " + req.error);
                else Plugin.Log.LogInfo("Leaderboard: reported " + RankService.Points + " RP");
            }
        }

        /// <summary>Fetches the top 25 by points. Firebase returns a JSON object keyed by Steam id.</summary>
        public static IEnumerator Refresh(bool force = false)
        {
            if (!Enabled || _busy) yield break;
            if (!force && Time.unscaledTime < _nextRefresh) yield break;
            _busy = true;
            _nextRefresh = Time.unscaledTime + 60f;
            Status = "Loading global leaderboard...";
            using (var req = UnityWebRequest.Get($"{BaseUrl}/players.json?orderBy=%22points%22&limitToLast=25"))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Status = "Global leaderboard unavailable (" + req.error + ")";
                    Plugin.Log.LogInfo(Status);
                }
                else
                {
                    try
                    {
                        Top = Parse(req.downloadHandler.text);
                        HasData = true;
                        Status = "";
                    }
                    catch (Exception e)
                    {
                        Status = "Leaderboard data could not be read";
                        Plugin.Log.LogWarning(Status + ": " + e.Message);
                    }
                }
            }
            _busy = false;
        }

        // Minimal parser for {"steamid":{"name":"..","points":N,...},...}; avoids a JSON library dependency.
        private static List<Leaderboard.Entry> Parse(string json)
        {
            var list = new List<Leaderboard.Entry>();
            if (string.IsNullOrEmpty(json) || json.Trim() == "null") return list;
            int i = 0;
            Expect(json, ref i, '{');
            while (true)
            {
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] == '}') break;
                string key = ReadString(json, ref i);
                SkipWs(json, ref i); Expect(json, ref i, ':'); SkipWs(json, ref i);
                int start = i;
                SkipValue(json, ref i);
                string obj = json.Substring(start, i - start);
                var rec = JsonUtility.FromJson<Record>(obj);
                if (rec != null) list.Add(new Leaderboard.Entry { SteamId = key, Name = rec.name ?? "", Points = rec.points, LastSeen = ShortDate(rec.updated) });
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == ',') i++;
            }
            list.Sort((a, b) => b.Points.CompareTo(a.Points));
            return list;
        }

        private static string ShortDate(string updated)
        {
            if (string.IsNullOrEmpty(updated)) return "";
            if (DateTime.TryParse(updated, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var d)) return d.ToLocalTime().ToString("MMM d");
            return updated;
        }

        private static void SkipWs(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
        private static void Expect(string s, ref int i, char c) { SkipWs(s, ref i); if (i >= s.Length || s[i] != c) throw new FormatException($"expected '{c}' at {i}"); i++; }
        private static string ReadString(string s, ref int i)
        {
            Expect(s, ref i, '"');
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"') { if (s[i] == '\\' && i + 1 < s.Length) i++; sb.Append(s[i]); i++; }
            i++;
            return sb.ToString();
        }
        private static void SkipValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (s[i] == '{' || s[i] == '[')
            {
                int depth = 0; bool inStr = false;
                for (; i < s.Length; i++)
                {
                    char c = s[i];
                    if (inStr) { if (c == '\\') i++; else if (c == '"') inStr = false; continue; }
                    if (c == '"') inStr = true;
                    else if (c == '{' || c == '[') depth++;
                    else if (c == '}' || c == ']') { depth--; if (depth == 0) { i++; return; } }
                }
            }
            else if (s[i] == '"') { ReadString(s, ref i); }
            else { while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']') i++; }
        }
    }
}
