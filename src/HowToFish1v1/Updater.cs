using System;
using System.Collections;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.Networking;

namespace HowToFish1v1
{
    /// <summary>
    /// Self-updater. On startup it fetches a small manifest, and when a newer version is listed it downloads the new
    /// plugin files next to the current ones and swaps them in (the running DLLs are renamed to .old, which Windows
    /// allows while they are loaded). The new version runs on the next launch; a banner on the main menu says so.
    /// </summary>
    public static class Updater
    {
        [Serializable] private class ManifestFile { public string name; public string url; }
        [Serializable] private class Manifest { public string version; public string notes; public ManifestFile[] files; }

        public static string Status { get; private set; } = "";
        public static string PendingVersion { get; private set; } = "";
        public static bool UpdateInstalled => !string.IsNullOrEmpty(PendingVersion);
        public static bool Checking { get; private set; }

        private static string PluginDir => Path.GetDirectoryName(typeof(Updater).Assembly.Location);

        /// <summary>Removes leftovers from a previous update, then checks the manifest.</summary>
        public static IEnumerator Run()
        {
            CleanOld();
            if (!Plugin.Cfg.AutoUpdate.Value) yield break;
            string url = Plugin.Cfg.UpdateManifestUrl.Value?.Trim();
            if (string.IsNullOrEmpty(url)) yield break;

            Checking = true;
            Status = "Checking for updates...";
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Status = "";
                    Plugin.Log.LogInfo("Update check failed: " + req.error);
                    Checking = false;
                    yield break;
                }
                Manifest m = null;
                try { m = JsonUtility.FromJson<Manifest>(req.downloadHandler.text); }
                catch (Exception e) { Plugin.Log.LogWarning("Bad update manifest: " + e.Message); }
                if (m == null || string.IsNullOrEmpty(m.version) || m.files == null || m.files.Length == 0)
                {
                    Status = ""; Checking = false; yield break;
                }
                if (CompareVersions(m.version, Plugin.Version) <= 0)
                {
                    Status = "";
                    Plugin.Log.LogInfo($"Mod is up to date ({Plugin.Version}; latest {m.version})");
                    Checking = false;
                    yield break;
                }
                Plugin.Log.LogInfo($"Update available: {m.version} (running {Plugin.Version})");
                yield return Download(m);
            }
            Checking = false;
        }

        private static IEnumerator Download(Manifest m)
        {
            string dir = PluginDir;
            var staged = new System.Collections.Generic.List<(string final, string temp)>();
            foreach (var f in m.files)
            {
                if (string.IsNullOrEmpty(f.name) || string.IsNullOrEmpty(f.url) || f.name.Contains("..") || f.name.Contains("/") || f.name.Contains("\\"))
                    continue;
                Status = $"Downloading update {m.version}: {f.name}";
                using (var req = UnityWebRequest.Get(f.url))
                {
                    req.timeout = 60;
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        Status = "Update download failed: " + req.error;
                        Plugin.Log.LogWarning(Status);
                        foreach (var s in staged) TryDelete(s.temp);
                        yield break;
                    }
                    string temp = Path.Combine(dir, f.name + ".download");
                    try { File.WriteAllBytes(temp, req.downloadHandler.data); }
                    catch (Exception e)
                    {
                        Status = "Update write failed: " + e.Message;
                        Plugin.Log.LogWarning(Status);
                        foreach (var s in staged) TryDelete(s.temp);
                        yield break;
                    }
                    staged.Add((Path.Combine(dir, f.name), temp));
                }
            }
            // Swap: rename the loaded files out of the way, then move the downloads into place.
            try
            {
                foreach (var s in staged)
                {
                    string old = s.final + ".old";
                    TryDelete(old);
                    if (File.Exists(s.final)) File.Move(s.final, old);
                    File.Move(s.temp, s.final);
                }
                PendingVersion = m.version;
                Status = $"Mod updated to {m.version}. Restart the game to use it.";
                Plugin.Log.LogInfo(Status + (string.IsNullOrEmpty(m.notes) ? "" : " Notes: " + m.notes));
            }
            catch (Exception e)
            {
                Status = "Update could not be installed: " + e.Message;
                Plugin.Log.LogWarning(Status);
                foreach (var s in staged) TryDelete(s.temp);
            }
        }

        private static void CleanOld()
        {
            try
            {
                foreach (var f in Directory.GetFiles(PluginDir, "*.old")) TryDelete(f);
                foreach (var f in Directory.GetFiles(PluginDir, "*.download")) TryDelete(f);
            }
            catch (Exception) { }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
        }

        /// <summary>Numeric dotted compare: 0.2.10 &gt; 0.2.9. Returns &gt;0 when a is newer.</summary>
        public static int CompareVersions(string a, string b)
        {
            var pa = (a ?? "").Split('.'); var pb = (b ?? "").Split('.');
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                int.TryParse(i < pa.Length ? pa[i].Trim() : "0", out int x);
                int.TryParse(i < pb.Length ? pb[i].Trim() : "0", out int y);
                if (x != y) return x.CompareTo(y);
            }
            return 0;
        }

        /// <summary>Main-menu banner drawn on the design canvas.</summary>
        public static void Draw()
        {
            if (!MainMenuManager.IsInMenu || string.IsNullOrEmpty(Status)) return;
            var saved = UI.RankedStyles.BeginCanvas();
            float w = UI.RankedStyles.DesignW;
            float h = UpdateInstalled ? 64f : 44f;
            UnityEngine.GUI.DrawTexture(new Rect(0, 0, w, h), UpdateInstalled ? UI.RankedStyles.Gold : UI.RankedStyles.PanelLight);
            var style = UpdateInstalled ? new GUIStyle(UI.RankedStyles.H2) { alignment = TextAnchor.MiddleCenter } : UI.RankedStyles.SmallCenter;
            if (UpdateInstalled) style.normal.textColor = new Color(0.08f, 0.08f, 0.1f);
            UnityEngine.GUI.Label(new Rect(0, 0, w - (UpdateInstalled ? 260f : 0f), h), Status, style);
            if (UpdateInstalled && UI.RankedStyles.Btn(new Rect(w - 250f, 10f, 220f, 44f), "QUIT GAME TO APPLY", UI.RankedStyles.Button))
                Application.Quit();
            UnityEngine.GUI.matrix = saved;
        }
    }
}
