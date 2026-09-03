using System.Collections.Generic;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>Keeps the last few seconds of every player's head position and rotation so a kill can be replayed.</summary>
    public static class Recorder
    {
        public struct Sample { public float T; public Vector3 Pos; public Quaternion Rot; }

        private const float KeepSeconds = 12f;
        private static readonly Dictionary<int, List<Sample>> _tracks = new Dictionary<int, List<Sample>>();

        /// <summary>Call every frame.</summary>
        public static void Update()
        {
            if (!ModState.IsActive) { if (_tracks.Count > 0) _tracks.Clear(); return; }
            float now = Time.unscaledTime;
            foreach (var p in PlayerManager.Players)
            {
                if (!p) continue;
                var head = p.CamObject ? p.CamObject : p.Transform;
                if (!head) continue;
                if (!_tracks.TryGetValue(p.OwnerId, out var list)) { list = new List<Sample>(1024); _tracks[p.OwnerId] = list; }
                list.Add(new Sample { T = now, Pos = head.position, Rot = head.rotation });
                int drop = 0;
                while (drop < list.Count && now - list[drop].T > KeepSeconds) drop++;
                if (drop > 0) list.RemoveRange(0, drop);
            }
        }

        /// <summary>Interpolated head pose of a player at an unscaled time; false if nothing recorded around that time.</summary>
        public static bool TryGet(int ownerId, float t, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            if (!_tracks.TryGetValue(ownerId, out var list) || list.Count == 0) return false;
            if (t <= list[0].T) { pos = list[0].Pos; rot = list[0].Rot; return true; }
            var last = list[list.Count - 1];
            if (t >= last.T) { pos = last.Pos; rot = last.Rot; return true; }
            int lo = 0, hi = list.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (list[mid].T <= t) lo = mid; else hi = mid;
            }
            var a = list[lo]; var b = list[hi];
            float f = Mathf.InverseLerp(a.T, b.T, t);
            pos = Vector3.Lerp(a.Pos, b.Pos, f);
            rot = Quaternion.Slerp(a.Rot, b.Rot, f);
            return true;
        }
    }
}
