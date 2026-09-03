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
        private static readonly Dictionary<int, List<float>> _shots = new Dictionary<int, List<float>>();

        /// <summary>A weapon held by this player fired (seen on every client through the game's shoot effects).</summary>
        public static void RecordShot(int ownerId)
        {
            if (!ModState.IsActive) return;
            if (!_shots.TryGetValue(ownerId, out var list)) { list = new List<float>(); _shots[ownerId] = list; }
            list.Add(Time.unscaledTime);
            while (list.Count > 0 && Time.unscaledTime - list[0] > KeepSeconds) list.RemoveAt(0);
        }

        private static readonly Dictionary<int, List<(float t, bool ads)>> _aim = new Dictionary<int, List<(float, bool)>>();

        public static void RecordAim(int ownerId, bool ads)
        {
            if (!_aim.TryGetValue(ownerId, out var list)) { list = new List<(float, bool)>(); _aim[ownerId] = list; }
            list.Add((Time.unscaledTime, ads));
            while (list.Count > 64) list.RemoveAt(0);
        }

        /// <summary>Was the player aiming down sights at time t? (last reported state before t)</summary>
        public static bool AdsAt(int ownerId, float t)
        {
            if (!_aim.TryGetValue(ownerId, out var list)) return false;
            bool ads = false;
            foreach (var e in list) { if (e.t <= t) ads = e.ads; else break; }
            return ads;
        }

        /// <summary>True if the player fired in the (t0, t1] window.</summary>
        public static bool FiredBetween(int ownerId, float t0, float t1)
        {
            if (!_shots.TryGetValue(ownerId, out var list)) return false;
            foreach (var t in list) if (t > t0 && t <= t1) return true;
            return false;
        }

        /// <summary>Call every frame.</summary>
        public static void Update()
        {
            if (!ModState.IsActive) { if (_tracks.Count > 0) { _tracks.Clear(); _shots.Clear(); _aim.Clear(); } return; }
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
