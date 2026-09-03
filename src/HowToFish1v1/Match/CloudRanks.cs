using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.Networking;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// Global leaderboard backend: a Firebase Realtime Database reached over plain HTTPS. Each install signs in anonymously
    /// once (Firebase Auth REST), keeps its refresh token locally, and may only write its own record at /players/{uid};
    /// the record carries the Steam id, name and rank stats. Reads are public. Disabled when ShareRank is off.
    /// </summary>
    public static class CloudRanks
    {
        [Serializable] private class Record { public string steamId; public string name; public int points; public int wins; public int losses; public int kills; public int deaths; public string updated; public string version; public int season; }
        [Serializable] private class SeasonRecord { public string steamId; public string name; public int points; public string tier; }
        public static List<Leaderboard.Entry> HallOfFame { get; private set; } = new List<Leaderboard.Entry>();
        private static float _nextHall;
        private static bool _hallBusy;
        [Serializable] private class AuthFile { public string uid; public string refreshToken; }
        [Serializable] private class SignUpResponse { public string idToken; public string refreshToken; public string localId; public string expiresIn; }
        [Serializable] private class RefreshResponse { public string id_token; public string refresh_token; public string user_id; public string expires_in; }

        public static List<Leaderboard.Entry> Top { get; private set; } = new List<Leaderboard.Entry>();
        public static bool Enabled => Plugin.Cfg.ShareRank.Value && !string.IsNullOrEmpty(BaseUrl) && !string.IsNullOrEmpty(ApiKey);
        public static string Status { get; private set; } = "";
        public static bool HasData { get; private set; }

        private static string BaseUrl => (Plugin.Cfg.LeaderboardUrl.Value ?? "").Trim().TrimEnd('/');
        private static string ApiKey => (Plugin.Cfg.FirebaseApiKey.Value ?? "").Trim();
        private static string AuthPath => Path.Combine(Paths.ConfigPath, "HowToFish1v1.auth.json");

        private static string _uid, _refreshToken, _idToken;
        private static float _tokenExpiresAt = -1f;
        private static float _nextRefresh;
        private static bool _busy, _authBusy;

        // ------------------------------------------------------------------ auth

        /// <summary>Signs in anonymously (once per install) or refreshes the short-lived id token. Sets _idToken on success.</summary>
        private static IEnumerator EnsureAuth()
        {
            if (!string.IsNullOrEmpty(_idToken) && Time.unscaledTime < _tokenExpiresAt) yield break;
            if (_authBusy) yield break;
            _authBusy = true;
            try
            {
                if (string.IsNullOrEmpty(_refreshToken)) LoadAuth();
                if (!string.IsNullOrEmpty(_refreshToken))
                {
                    var body = Encoding.UTF8.GetBytes($"grant_type=refresh_token&refresh_token={UnityWebRequest.EscapeURL(_refreshToken)}");
                    using (var req = new UnityWebRequest($"https://securetoken.googleapis.com/v1/token?key={ApiKey}", "POST"))
                    {
                        req.uploadHandler = new UploadHandlerRaw(body);
                        req.downloadHandler = new DownloadHandlerBuffer();
                        req.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
                        req.timeout = 15;
                        yield return req.SendWebRequest();
                        if (req.result == UnityWebRequest.Result.Success)
                        {
                            var r = JsonUtility.FromJson<RefreshResponse>(req.downloadHandler.text);
                            if (r != null && !string.IsNullOrEmpty(r.id_token))
                            {
                                _idToken = r.id_token; _uid = r.user_id;
                                if (!string.IsNullOrEmpty(r.refresh_token)) _refreshToken = r.refresh_token;
                                _tokenExpiresAt = Time.unscaledTime + 50f * 60f;
                                SaveAuth();
                                yield break;
                            }
                        }
                        Plugin.Log.LogInfo("Leaderboard sign-in refresh failed; signing in again");
                        _refreshToken = null;
                    }
                }
                using (var req = new UnityWebRequest($"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={ApiKey}", "POST"))
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{\"returnSecureToken\":true}"));
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.SetRequestHeader("Content-Type", "application/json");
                    req.timeout = 15;
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        Plugin.Log.LogInfo("Leaderboard sign-in failed: " + req.error);
                        yield break;
                    }
                    var s = JsonUtility.FromJson<SignUpResponse>(req.downloadHandler.text);
                    if (s == null || string.IsNullOrEmpty(s.idToken)) yield break;
                    _idToken = s.idToken; _refreshToken = s.refreshToken; _uid = s.localId;
                    _tokenExpiresAt = Time.unscaledTime + 50f * 60f;
                    SaveAuth();
                    Plugin.Log.LogInfo("Leaderboard: signed in anonymously");
                }
            }
            finally
            {
                _authBusy = false;
            }
        }

        private static void LoadAuth()
        {
            try
            {
                if (!File.Exists(AuthPath)) return;
                var a = JsonUtility.FromJson<AuthFile>(File.ReadAllText(AuthPath));
                if (a != null) { _uid = a.uid; _refreshToken = a.refreshToken; }
            }
            catch (Exception) { }
        }

        private static void SaveAuth()
        {
            try { File.WriteAllText(AuthPath, JsonUtility.ToJson(new AuthFile { uid = _uid, refreshToken = _refreshToken })); }
            catch (Exception e) { Plugin.Log.LogWarning("Could not save leaderboard sign-in: " + e.Message); }
        }

        // ------------------------------------------------------------------ report / refresh

        /// <summary>Uploads this player's current stats. Call after a match result and at startup.</summary>
        public static IEnumerator Report()
        {
            if (!Enabled) yield break;
            string steamId = RankService.LocalId;
            if (string.IsNullOrEmpty(steamId) || steamId == "local") yield break;
            yield return EnsureAuth();
            if (string.IsNullOrEmpty(_idToken) || string.IsNullOrEmpty(_uid)) yield break;
            string name = "";
            try { name = Steamworks.SteamFriends.GetPersonaName(); } catch (Exception) { }
            var rec = new Record
            {
                steamId = steamId, name = name, points = RankService.Points, wins = RankService.Wins, losses = RankService.Losses,
                kills = RankService.Kills, deaths = RankService.Deaths, updated = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"), version = Plugin.Version,
                season = Seasons.Current
            };
            var body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(rec));
            using (var req = new UnityWebRequest($"{BaseUrl}/players/{_uid}.json?auth={_idToken}", "PUT"))
            {
                req.uploadHandler = new UploadHandlerRaw(body);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) Plugin.Log.LogInfo("Leaderboard report failed: " + req.error + " " + req.downloadHandler.text);
                else Plugin.Log.LogInfo("Leaderboard: reported " + RankService.Points + " RP");
            }
        }

        /// <summary>Archives a finished season's final standing for this player at /seasons/{n}/{uid}.</summary>
        public static IEnumerator ReportSeason(int season, int points, string tier)
        {
            if (!Enabled) yield break;
            string steamId = RankService.LocalId;
            if (string.IsNullOrEmpty(steamId) || steamId == "local") yield break;
            yield return EnsureAuth();
            if (string.IsNullOrEmpty(_idToken) || string.IsNullOrEmpty(_uid)) yield break;
            string name = "";
            try { name = Steamworks.SteamFriends.GetPersonaName(); } catch (Exception) { }
            var body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new SeasonRecord { steamId = steamId, name = name, points = points, tier = tier }));
            using (var req = new UnityWebRequest($"{BaseUrl}/seasons/{season}/{_uid}.json?auth={_idToken}", "PUT"))
            {
                req.uploadHandler = new UploadHandlerRaw(body);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = 15;
                yield return req.SendWebRequest();
                Plugin.Log.LogInfo(req.result == UnityWebRequest.Result.Success ? $"Season {season} archived: {points} RP" : "Season archive failed: " + req.error);
            }
        }

        /// <summary>Last season's top players.</summary>
        public static IEnumerator RefreshHallOfFame()
        {
            if (!Enabled || _hallBusy || Time.unscaledTime < _nextHall || Seasons.Current <= 1) yield break;
            _hallBusy = true;
            _nextHall = Time.unscaledTime + 300f;
            using (var req = UnityWebRequest.Get($"{BaseUrl}/seasons/{Seasons.Current - 1}.json?orderBy=%22points%22&limitToLast=10"))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    try { HallOfFame = Parse(req.downloadHandler.text); } catch (Exception e) { Plugin.Log.LogDebug("Hall of fame: " + e.Message); }
                }
            }
            _hallBusy = false;
        }

        /// <summary>Fetches the top entries by points. Firebase returns a JSON object keyed by uid; the Steam id is a field.</summary>
        public static IEnumerator Refresh(bool force = false)
        {
            if (!Enabled || _busy) yield break;
            if (!force && Time.unscaledTime < _nextRefresh) yield break;
            _busy = true;
            _nextRefresh = Time.unscaledTime + 60f;
            Status = "Loading global leaderboard...";
            using (var req = UnityWebRequest.Get($"{BaseUrl}/players.json?orderBy=%22points%22&limitToLast=60"))
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

        // Minimal parser for {"uid":{"steamId":"..","name":"..","points":N,...},...}; avoids a JSON library dependency.
        private static List<Leaderboard.Entry> Parse(string json)
        {
            var best = new Dictionary<string, Leaderboard.Entry>();
            if (string.IsNullOrEmpty(json) || json.Trim() == "null") return new List<Leaderboard.Entry>();
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
                var rec = JsonUtility.FromJson<Record>(json.Substring(start, i - start));
                if (rec != null)
                {
                    string sid = string.IsNullOrEmpty(rec.steamId) ? key : rec.steamId;
                    // One player may have several installs; keep the best record per Steam id.
                    if (!best.TryGetValue(sid, out var e) || rec.points > e.Points)
                        best[sid] = new Leaderboard.Entry { SteamId = sid, Name = rec.name ?? "", Points = rec.points, LastSeen = ShortDate(rec.updated) };
                }
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == ',') i++;
            }
            return best.Values.OrderByDescending(e => e.Points).ToList();
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
