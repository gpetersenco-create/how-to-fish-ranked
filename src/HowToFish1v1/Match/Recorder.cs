using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// Keeps the last seconds of every player's head pose, held gun (which item and where each of its parts sat relative
    /// to the head), shots and aim state so a kill can be replayed from the killer's eyes.
    /// </summary>
    public static class Recorder
    {
        public struct Sample { public float T; public Vector3 Pos; public Quaternion Rot; }

        /// <summary>Held item and its parts, in head space, so the replay reproduces sway, aim, recoil, reloads and swaps.</summary>
        public struct GunSample
        {
            public float T;
            public Item Item;
            public Vector3 RootPos; public Quaternion RootRot;
            public Vector3[] Pos; public Quaternion[] Rot; public bool[] On;
        }

        public const float KeepSeconds = 20f;
        private static readonly Dictionary<int, List<Sample>> _tracks = new Dictionary<int, List<Sample>>();
        private static readonly Dictionary<int, List<GunSample>> _guns = new Dictionary<int, List<GunSample>>();
        private static readonly Dictionary<int, List<float>> _shots = new Dictionary<int, List<float>>();
        private static readonly Dictionary<Item, Renderer[]> _rendCache = new Dictionary<Item, Renderer[]>();
        private static float _nextCacheClear;

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

        /// <summary>Time of the player's last shot at or before t, or -1 if none was recorded.</summary>
        public static float LastShotBefore(int ownerId, float t)
        {
            if (!_shots.TryGetValue(ownerId, out var list)) return -1f;
            float best = -1f;
            foreach (var s in list) if (s <= t + 0.25f && s > best) best = s;
            return best;
        }

        /// <summary>True if the player fired in the (t0, t1] window.</summary>
        public static bool FiredBetween(int ownerId, float t0, float t1)
        {
            if (!_shots.TryGetValue(ownerId, out var list)) return false;
            foreach (var t in list) if (t > t0 && t <= t1) return true;
            return false;
        }

        /// <summary>The mesh parts of an item, in a stable order shared by the recording and the replay copy.</summary>
        public static Renderer[] RenderersOf(Item item)
        {
            if (!item) return Array.Empty<Renderer>();
            if (!_rendCache.TryGetValue(item, out var list) || list == null)
            {
                list = item.GetComponentsInChildren<Renderer>(true).Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
                _rendCache[item] = list;
            }
            return list;
        }

        /// <summary>Call every frame.</summary>
        public static void Update()
        {
            if (!ModState.IsActive)
            {
                if (_tracks.Count > 0) { _tracks.Clear(); _shots.Clear(); _aim.Clear(); _guns.Clear(); _rendCache.Clear(); }
                return;
            }
            float now = Time.unscaledTime;
            if (now >= _nextCacheClear) { _nextCacheClear = now + 2f; _rendCache.Clear(); }   // attachments can change the part list
            foreach (var p in PlayerManager.Players)
            {
                if (!p) continue;
                var head = p.CamObject ? p.CamObject : p.Transform;
                if (!head) continue;
                if (!_tracks.TryGetValue(p.OwnerId, out var list)) { list = new List<Sample>(1024); _tracks[p.OwnerId] = list; }
                list.Add(new Sample { T = now, Pos = head.position, Rot = head.rotation });
                Prune(list, now, s => s.T);

                if (!_guns.TryGetValue(p.OwnerId, out var glist)) { glist = new List<GunSample>(1024); _guns[p.OwnerId] = glist; }
                var item = p.Holding ? p.Holding.HeldItem : null;
                var gs = new GunSample { T = now, Item = item };
                if (item)
                {
                    var rends = RenderersOf(item);
                    int n = rends.Length;
                    gs.RootPos = head.InverseTransformPoint(item.transform.position);
                    gs.RootRot = Quaternion.Inverse(head.rotation) * item.transform.rotation;
                    gs.Pos = new Vector3[n]; gs.Rot = new Quaternion[n]; gs.On = new bool[n];
                    for (int i = 0; i < n; i++)
                    {
                        var r = rends[i];
                        if (!r) continue;
                        gs.Pos[i] = head.InverseTransformPoint(r.transform.position);
                        gs.Rot[i] = Quaternion.Inverse(head.rotation) * r.transform.rotation;
                        gs.On[i] = r.enabled && r.gameObject.activeInHierarchy;
                    }
                }
                glist.Add(gs);
                Prune(glist, now, s => s.T);
            }
        }

        private static void Prune<T>(List<T> list, float now, Func<T, float> time)
        {
            int drop = 0;
            while (drop < list.Count && now - time(list[drop]) > KeepSeconds) drop++;
            if (drop > 0) list.RemoveRange(0, drop);
        }

        /// <summary>Interpolated head pose of a player at an unscaled time; false if nothing recorded around that time.</summary>
        public static bool TryGet(int ownerId, float t, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            if (!_tracks.TryGetValue(ownerId, out var list) || list.Count == 0) return false;
            if (t <= list[0].T) { pos = list[0].Pos; rot = list[0].Rot; return true; }
            var last = list[list.Count - 1];
            if (t >= last.T) { pos = last.Pos; rot = last.Rot; return true; }
            int lo = Lower(list, t, s => s.T);
            var a = list[lo]; var b = list[lo + 1];
            float f = Mathf.InverseLerp(a.T, b.T, t);
            pos = Vector3.Lerp(a.Pos, b.Pos, f);
            rot = Quaternion.Slerp(a.Rot, b.Rot, f);
            return true;
        }

        /// <summary>Held item and part poses (head space) at time t; parts are interpolated when the neighbouring samples hold the same item.</summary>
        public static bool TryGetGun(int ownerId, float t, out GunSample result)
        {
            result = default;
            if (!_guns.TryGetValue(ownerId, out var list) || list.Count == 0) return false;
            if (t <= list[0].T) { result = list[0]; return true; }
            var last = list[list.Count - 1];
            if (t >= last.T) { result = last; return true; }
            int lo = Lower(list, t, s => s.T);
            var a = list[lo]; var b = list[lo + 1];
            result = a;
            if (a.Item != b.Item || a.Pos == null || b.Pos == null || a.Pos.Length != b.Pos.Length) return true;
            float f = Mathf.InverseLerp(a.T, b.T, t);
            int n = a.Pos.Length;
            var mixed = new GunSample
            {
                T = t, Item = a.Item,
                RootPos = Vector3.Lerp(a.RootPos, b.RootPos, f), RootRot = Quaternion.Slerp(a.RootRot, b.RootRot, f),
                Pos = new Vector3[n], Rot = new Quaternion[n], On = a.On
            };
            for (int i = 0; i < n; i++)
            {
                mixed.Pos[i] = Vector3.Lerp(a.Pos[i], b.Pos[i], f);
                mixed.Rot[i] = Quaternion.Slerp(a.Rot[i], b.Rot[i], f);
            }
            result = mixed;
            return true;
        }

        private static int Lower<T>(List<T> list, float t, Func<T, float> time)
        {
            int lo = 0, hi = list.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (time(list[mid]) <= t) lo = mid; else hi = mid;
            }
            return lo;
        }
    }
}
