using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// Announcer lines. Drop files named announcer_&lt;key&gt;.mp3 / .wav / .ogg into the plugin folder and they play at
    /// the matching moment; without a file a short synthesized chime plays instead. Keys: round, round1..round6, final,
    /// victory, defeat, firstblood, streak3, streak5, streak7, trickshot, oitc.
    /// </summary>
    public static class Announcer
    {
        private static readonly Dictionary<string, AudioClip> _files = new Dictionary<string, AudioClip>();
        private static readonly Dictionary<string, AudioClip> _synth = new Dictionary<string, AudioClip>();
        private static AudioSource _source;
        private static bool _loaded;
        private const int Rate = 44100;

        public static IEnumerator LoadFiles()
        {
            if (_loaded) yield break;
            _loaded = true;
            string dir = Path.GetDirectoryName(typeof(Announcer).Assembly.Location);
            foreach (var key in new[] { "round", "round1", "round2", "round3", "round4", "round5", "round6", "final", "victory", "defeat", "firstblood", "streak3", "streak5", "streak7", "trickshot", "oitc" })
            {
                foreach (var ext in new[] { ".wav", ".ogg", ".mp3" })
                {
                    string path = Path.Combine(dir, "announcer_" + key + ext);
                    if (!File.Exists(path)) continue;
                    var type = ext == ".wav" ? AudioType.WAV : ext == ".ogg" ? AudioType.OGGVORBIS : AudioType.MPEG;
                    using (var req = UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace(Path.DirectorySeparatorChar, '/'), type))
                    {
                        yield return req.SendWebRequest();
                        if (req.result == UnityWebRequest.Result.Success)
                        {
                            var clip = DownloadHandlerAudioClip.GetContent(req);
                            if (clip && clip.length > 0f) { _files[key] = clip; Plugin.Log.LogInfo($"Announcer: loaded {key}{ext}"); }
                        }
                    }
                    break;
                }
            }
        }

        private static void Ensure()
        {
            if (_source) return;
            var go = new GameObject("HTF1v1_Announcer");
            Object.DontDestroyOnLoad(go);
            _source = go.AddComponent<AudioSource>();
            _source.spatialBlend = 0f;
            _source.playOnAwake = false;
            _source.priority = 0;
            try
            {
                var global = Traverse.Create(typeof(AudioManager)).Field<AudioSource>("_globalSource").Value;
                if (global) _source.outputAudioMixerGroup = global.outputAudioMixerGroup;
            }
            catch (System.Exception) { }
        }

        public static void Play(string key)
        {
            if (!Plugin.Cfg.Announcer.Value) return;
            Ensure();
            AudioClip clip;
            if (!_files.TryGetValue(key, out clip))
            {
                // "round3" falls back to the generic "round" file.
                if (key.StartsWith("round") && !_files.TryGetValue("round", out clip)) clip = null;
                if (!clip) clip = Synth(key);
            }
            if (clip) _source.PlayOneShot(clip, 0.9f);
        }

        /// <summary>Built-in chimes per kind of moment.</summary>
        private static AudioClip Synth(string key)
        {
            string kind = key.StartsWith("round") ? "round" : key.StartsWith("streak") ? "streak" : key;
            if (_synth.TryGetValue(kind, out var c) && c) return c;
            float[] notes; float noteLen;
            switch (kind)
            {
                case "victory": notes = new[] { 523f, 659f, 784f, 1047f }; noteLen = 0.16f; break;
                case "defeat": notes = new[] { 392f, 330f, 262f }; noteLen = 0.22f; break;
                case "final": notes = new[] { 440f, 440f, 660f }; noteLen = 0.14f; break;
                case "firstblood": notes = new[] { 660f, 880f }; noteLen = 0.12f; break;
                case "streak": notes = new[] { 587f, 740f, 880f }; noteLen = 0.11f; break;
                case "trickshot": notes = new[] { 784f, 988f, 1175f, 1568f }; noteLen = 0.12f; break;
                default: notes = new[] { 494f, 659f }; noteLen = 0.13f; break;
            }
            int n = (int)(Rate * (notes.Length * noteLen + 0.25f));
            var data = new float[n];
            for (int i = 0; i < notes.Length; i++)
            {
                int start = (int)(Rate * noteLen * i);
                for (int j = 0; j < (int)(Rate * (noteLen + 0.25f)) && start + j < n; j++)
                {
                    float t = (float)j / Rate;
                    float env = Mathf.Min(1f, t / 0.008f) * Mathf.Exp(-t * 7f);
                    float s = Mathf.Sin(2f * Mathf.PI * notes[i] * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * notes[i] * 2f * t) * 0.15f;
                    data[start + j] += s * env * 0.5f;
                }
            }
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(data[i], -1f, 1f);
            c = AudioClip.Create("HTF1v1_Announce_" + kind, n, 1, Rate, false);
            c.SetData(data, 0);
            _synth[kind] = c;
            return c;
        }
    }
}
