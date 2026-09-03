using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// Keeps the last seconds of everything a killcam needs: every player's head pose, held gun and (for other players)
    /// body rig, every creature's pose, shots and aim state. Rigs are recorded as the poses of their mesh parts and bones,
    /// so a replay copy rigged to stand-in bones moves exactly as the original did.
    /// </summary>
    public static class Recorder
    {
        public struct Sample { public float T; public Vector3 Pos; public Quaternion Rot; }

        /// <summary>The mesh parts of a hierarchy plus the distinct bones of its skinned parts, in a stable order shared with the replay copy.</summary>
        public sealed class RigParts
        {
            public Renderer[] Rends = Array.Empty<Renderer>();
            public Transform[] Bones = Array.Empty<Transform>();
            public int[][] BoneMap = Array.Empty<int[]>();   // per renderer: indices into Bones, or null when not skinned
        }

        /// <summary>Poses of a rig's parts and bones in some space (head space for guns, world for bodies).</summary>
        public struct RigSample
        {
            public Vector3[] Pos; public Quaternion[] Rot; public bool[] On;
            public Vector3[] BonePos; public Quaternion[] BoneRot;
            public bool Valid => Pos != null;
        }

        public struct GunSample
        {
            public float T;
            public Item Item;
            public Vector3 RootPos; public Quaternion RootRot;
            public Vector3 FirePos; public Quaternion FireRot; public bool HasFire;
            public RigSample Rig;
        }

        public struct BodySample { public float T; public RigSample Rig; }

        /// <summary>One mesh part of a creature snapshot, relative to the creature's root.</summary>
        public struct ActorPart { public Mesh Mesh; public Material[] Mats; public Vector3 Pos; public Quaternion Rot; public Vector3 Scale; public bool Owned; }

        /// <summary>A creature (bird, fish...) seen during the match: its pose over time plus a one-time mesh snapshot.</summary>
        public sealed class ActorTrack
        {
            public string Name = "";
            public readonly List<Sample> Samples = new List<Sample>(512);
            public List<ActorPart> Parts = new List<ActorPart>();
        }

        /// <summary>A standing player body captured from another player, used to stand in for the local player (who has no third-person body).</summary>
        public sealed class MannequinData
        {
            public RigParts Parts;
            public RigSample Pose;       // in the body root's space
            public float HeadHeight;     // head above the body root
            public Player Source;
        }

        public const float KeepSeconds = 20f;
        public const int MaxBones = 240;

        private static readonly Dictionary<int, List<Sample>> _tracks = new Dictionary<int, List<Sample>>();
        private static readonly Dictionary<int, List<GunSample>> _guns = new Dictionary<int, List<GunSample>>();
        private static readonly Dictionary<int, List<BodySample>> _bodies = new Dictionary<int, List<BodySample>>();
        private static readonly Dictionary<int, RigParts> _bodyParts = new Dictionary<int, RigParts>();
        private static readonly Dictionary<int, List<float>> _shots = new Dictionary<int, List<float>>();
        public struct Shot { public float T; public Vector3 Hit; public bool HasHit; }
        public struct KnifeSwing { public float T; public byte Skin; }
        private static readonly Dictionary<int, List<KnifeSwing>> _knives = new Dictionary<int, List<KnifeSwing>>();

        public static void RecordKnife(int ownerId, byte skin)
        {
            if (!ModState.IsActive) return;
            if (!_knives.TryGetValue(ownerId, out var list)) { list = new List<KnifeSwing>(); _knives[ownerId] = list; }
            list.Add(new KnifeSwing { T = Time.unscaledTime, Skin = skin });
            while (list.Count > 0 && Time.unscaledTime - list[0].T > KeepSeconds) list.RemoveAt(0);
        }

        /// <summary>The knife swing in progress at time t, if any.</summary>
        public static bool KnifeAt(int ownerId, float t, float swingSeconds, out KnifeSwing swing)
        {
            swing = default;
            if (!_knives.TryGetValue(ownerId, out var list)) return false;
            foreach (var s in list) if (t >= s.T && t <= s.T + swingSeconds) { swing = s; return true; }
            return false;
        }
        private static readonly Dictionary<int, List<Shot>> _shotHits = new Dictionary<int, List<Shot>>();
        private static readonly Dictionary<int, List<(float t, bool ads)>> _aim = new Dictionary<int, List<(float, bool)>>();
        private static readonly Dictionary<Component, RigParts> _rigCache = new Dictionary<Component, RigParts>();
        private static float _nextCacheClear;
        private static readonly Dictionary<int, ActorTrack> _actors = new Dictionary<int, ActorTrack>();
        private static Creature[] _creatures = Array.Empty<Creature>();
        private static float _nextCreatureScan, _nextMannequin;

        public static IEnumerable<KeyValuePair<int, ActorTrack>> Actors => _actors;
        private static readonly List<Transform> _extras = new List<Transform>();
        /// <summary>Mod-made things worth replaying (trickshot bots).</summary>
        public static IReadOnlyList<Transform> LiveExtras => _extras;
        public static void RegisterActor(Transform t) { if (t && !_extras.Contains(t)) _extras.Add(t); }
        public static void UnregisterActor(Transform t) { _extras.Remove(t); }
        /// <summary>Creatures currently in the world (refreshed twice a second).</summary>
        public static Creature[] LiveCreatures => _creatures;
        public static MannequinData Mannequin { get; private set; }
        public static RigParts BodyPartsOf(int ownerId) => _bodyParts.TryGetValue(ownerId, out var p) ? p : null;

        // ------------------------------------------------------------------ shots / aim

        /// <summary>A weapon held by this player fired (seen on every client through the game's shoot effects).</summary>
        public static void RecordShot(int ownerId) => RecordShot(ownerId, Vector3.zero, false);

        /// <summary>A shot plus where its aim line landed, for the replay's tracer.</summary>
        public static void RecordShot(int ownerId, Vector3 hit, bool hasHit)
        {
            if (!ModState.IsActive) return;
            if (!_shots.TryGetValue(ownerId, out var list)) { list = new List<float>(); _shots[ownerId] = list; }
            list.Add(Time.unscaledTime);
            while (list.Count > 0 && Time.unscaledTime - list[0] > KeepSeconds) list.RemoveAt(0);
            if (!_shotHits.TryGetValue(ownerId, out var hits)) { hits = new List<Shot>(); _shotHits[ownerId] = hits; }
            hits.Add(new Shot { T = Time.unscaledTime, Hit = hit, HasHit = hasHit });
            while (hits.Count > 0 && Time.unscaledTime - hits[0].T > KeepSeconds) hits.RemoveAt(0);
        }

        /// <summary>Shots the player fired in the (t0, t1] window, with their landing points.</summary>
        public static void ShotsBetween(int ownerId, float t0, float t1, List<Shot> into)
        {
            if (!_shotHits.TryGetValue(ownerId, out var list)) return;
            foreach (var s in list) if (s.T > t0 && s.T <= t1) into.Add(s);
        }

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

        // ------------------------------------------------------------------ rigs

        public static RigParts PartsOf(Component root)
        {
            if (!root) return new RigParts();
            if (!_rigCache.TryGetValue(root, out var parts) || parts == null)
            {
                parts = new RigParts();
                parts.Rends = root.GetComponentsInChildren<Renderer>(true).Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
                var bones = new List<Transform>();
                var index = new Dictionary<Transform, int>();
                parts.BoneMap = new int[parts.Rends.Length][];
                for (int i = 0; i < parts.Rends.Length; i++)
                {
                    if (!(parts.Rends[i] is SkinnedMeshRenderer smr) || smr.bones == null || smr.bones.Length == 0) continue;
                    var map = new int[smr.bones.Length];
                    bool ok = true;
                    for (int j = 0; j < map.Length; j++)
                    {
                        var b = smr.bones[j];
                        if (!b) { map[j] = -1; continue; }
                        if (!index.TryGetValue(b, out int k))
                        {
                            if (bones.Count >= MaxBones) { ok = false; break; }
                            k = bones.Count; bones.Add(b); index[b] = k;
                        }
                        map[j] = k;
                    }
                    parts.BoneMap[i] = ok ? map : null;
                }
                parts.Bones = bones.ToArray();
                _rigCache[root] = parts;
            }
            return parts;
        }

        /// <summary>Current poses of a rig's parts and bones, relative to <paramref name="space"/> (or the world when null).</summary>
        public static RigSample Capture(RigParts parts, Transform space)
        {
            int n = parts.Rends.Length, nb = parts.Bones.Length;
            var s = new RigSample { Pos = new Vector3[n], Rot = new Quaternion[n], On = new bool[n], BonePos = new Vector3[nb], BoneRot = new Quaternion[nb] };
            Quaternion inv = space ? Quaternion.Inverse(space.rotation) : Quaternion.identity;
            Vector3 origin = space ? space.position : Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                var r = parts.Rends[i];
                if (!r) continue;
                s.Pos[i] = inv * (r.transform.position - origin);
                s.Rot[i] = inv * r.transform.rotation;
                s.On[i] = r.enabled && r.gameObject.activeInHierarchy;
            }
            for (int i = 0; i < nb; i++)
            {
                var b = parts.Bones[i];
                if (!b) continue;
                s.BonePos[i] = inv * (b.position - origin);
                s.BoneRot[i] = inv * b.rotation;
            }
            return s;
        }

        private static RigSample Lerp(RigSample a, RigSample b, float f)
        {
            if (!a.Valid || !b.Valid || a.Pos.Length != b.Pos.Length || a.BonePos.Length != b.BonePos.Length) return a;
            int n = a.Pos.Length, nb = a.BonePos.Length;
            var m = new RigSample { Pos = new Vector3[n], Rot = new Quaternion[n], On = a.On, BonePos = new Vector3[nb], BoneRot = new Quaternion[nb] };
            for (int i = 0; i < n; i++) { m.Pos[i] = Vector3.Lerp(a.Pos[i], b.Pos[i], f); m.Rot[i] = Quaternion.Slerp(a.Rot[i], b.Rot[i], f); }
            for (int i = 0; i < nb; i++) { m.BonePos[i] = Vector3.Lerp(a.BonePos[i], b.BonePos[i], f); m.BoneRot[i] = Quaternion.Slerp(a.BoneRot[i], b.BoneRot[i], f); }
            return m;
        }

        // ------------------------------------------------------------------ per frame

        /// <summary>Call every frame.</summary>
        public static void Update()
        {
            if (!ModState.IsActive)
            {
                if (_tracks.Count > 0) { _tracks.Clear(); _shots.Clear(); _shotHits.Clear(); _knives.Clear(); _aim.Clear(); _guns.Clear(); _bodies.Clear(); _bodyParts.Clear(); _rigCache.Clear(); Mannequin = null; }
                if (_actors.Count > 0) { foreach (var tr in _actors.Values) DestroyParts(tr); _actors.Clear(); _creatures = Array.Empty<Creature>(); }
                return;
            }
            float now = Time.unscaledTime;
            if (now >= _nextCacheClear) { _nextCacheClear = now + 2f; _rigCache.Clear(); }   // attachments / outfits can change the part list
            RecordCreatures(now);
            foreach (var p in PlayerManager.Players)
            {
                if (!p) continue;
                var head = p.CamObject ? p.CamObject : p.Transform;
                if (!head) continue;
                if (!_tracks.TryGetValue(p.OwnerId, out var list)) { list = new List<Sample>(1024); _tracks[p.OwnerId] = list; }
                list.Add(new Sample { T = now, Pos = head.position, Rot = head.rotation });
                Prune(list, now, s => s.T);

                // Held gun, in head space.
                if (!_guns.TryGetValue(p.OwnerId, out var glist)) { glist = new List<GunSample>(1024); _guns[p.OwnerId] = glist; }
                var item = p.Holding ? p.Holding.HeldItem : null;
                var gs = new GunSample { T = now, Item = item };
                if (item)
                {
                    gs.RootPos = head.InverseTransformPoint(item.transform.position);
                    gs.RootRot = Quaternion.Inverse(head.rotation) * item.transform.rotation;
                    Transform fp = null;
                    try { if (item is Weapon w && w.Attachments) fp = w.Attachments.FirePoint; } catch (Exception) { }
                    if (fp)
                    {
                        gs.HasFire = true;
                        gs.FirePos = head.InverseTransformPoint(fp.position);
                        gs.FireRot = Quaternion.Inverse(head.rotation) * fp.rotation;
                    }
                    gs.Rig = Capture(PartsOf(item), head);
                }
                glist.Add(gs);
                Prune(glist, now, s => s.T);

                // Other players' bodies, in world space (the local player has no third-person body).
                bool remote = p.Owner != null && !p.Owner.IsLocalClient && p.Transform;
                if (remote)
                {
                    var parts = PartsOf(p.Transform);
                    _bodyParts[p.OwnerId] = parts;
                    if (!_bodies.TryGetValue(p.OwnerId, out var blist)) { blist = new List<BodySample>(1024); _bodies[p.OwnerId] = blist; }
                    blist.Add(new BodySample { T = now, Rig = Capture(parts, null) });
                    Prune(blist, now, s => s.T);
                }
            }
            if (now >= _nextMannequin) { _nextMannequin = now + 4f; CaptureMannequin(); }
        }

        /// <summary>Snapshot a standing other player in their root's space; preferably one who is not moving.</summary>
        private static void CaptureMannequin()
        {
            Player best = null; float bestSpeed = float.MaxValue;
            foreach (var p in PlayerManager.Players)
            {
                if (!p || p.Owner == null || p.Owner.IsLocalClient || !p.Transform || p.Dying.IsDead) continue;
                float speed = 0f;
                if (_tracks.TryGetValue(p.OwnerId, out var list) && list.Count >= 2)
                {
                    var a = list[list.Count - 2]; var b = list[list.Count - 1];
                    speed = b.T > a.T ? (b.Pos - a.Pos).magnitude / (b.T - a.T) : 0f;
                }
                if (speed < bestSpeed) { bestSpeed = speed; best = p; }
            }
            if (!best) return;
            if (Mannequin != null && Mannequin.Source == best && bestSpeed > 0.5f) return;   // keep the calmer earlier capture
            var parts = PartsOf(best.Transform);
            if (parts.Rends.Length == 0) return;
            var head = best.CamObject ? best.CamObject : best.Transform;
            Mannequin = new MannequinData { Parts = parts, Pose = Capture(parts, best.Transform), HeadHeight = head.position.y - best.Transform.position.y, Source = best };
        }

        /// <summary>Birds and fish are what people shoot at besides each other; without this they are missing from the replay.</summary>
        private static void RecordCreatures(float now)
        {
            if (now >= _nextCreatureScan)
            {
                _nextCreatureScan = now + 0.5f;
                try { _creatures = UnityEngine.Object.FindObjectsByType<Creature>(FindObjectsSortMode.None); } catch (Exception) { _creatures = Array.Empty<Creature>(); }
            }
            foreach (var c in _creatures)
            {
                if (!c || !c.gameObject.activeInHierarchy) continue;
                Track(c.transform, now);
            }
            _extras.RemoveAll(t => !t);
            foreach (var t in _extras) if (t.gameObject.activeInHierarchy) Track(t, now);
            List<int> dead = null;
            foreach (var kv in _actors)
            {
                var s = kv.Value.Samples;
                if (s.Count == 0 || now - s[s.Count - 1].T > KeepSeconds) { (dead ??= new List<int>()).Add(kv.Key); }
            }
            if (dead != null) foreach (var id in dead) { DestroyParts(_actors[id]); _actors.Remove(id); }
        }

        private static void Track(Transform root, float now)
        {
            int id = root.GetInstanceID();
            if (!_actors.TryGetValue(id, out var tr))
            {
                tr = new ActorTrack { Name = root.name };
                Snapshot(root, tr);
                if (tr.Parts.Count == 0) return;
                _actors[id] = tr;
            }
            tr.Samples.Add(new Sample { T = now, Pos = root.position, Rot = root.rotation });
            Prune(tr.Samples, now, s => s.T);
        }

        private static void Snapshot(Transform c, ActorTrack tr)
        {
            var root = c;
            foreach (var r in c.GetComponentsInChildren<Renderer>(false))
            {
                if (!r || !r.enabled) continue;
                var part = new ActorPart
                {
                    Mats = r.sharedMaterials,
                    Pos = Quaternion.Inverse(root.rotation) * (r.transform.position - root.position),
                    Rot = Quaternion.Inverse(root.rotation) * r.transform.rotation,
                    Scale = r.transform.lossyScale
                };
                if (r is SkinnedMeshRenderer smr)
                {
                    var mesh = new Mesh();
                    try { smr.BakeMesh(mesh); } catch (Exception) { UnityEngine.Object.Destroy(mesh); continue; }
                    part.Mesh = mesh; part.Owned = true; part.Scale = Vector3.one;
                }
                else if (r is MeshRenderer)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (!mf || !mf.sharedMesh) continue;
                    part.Mesh = mf.sharedMesh;
                }
                else continue;
                tr.Parts.Add(part);
            }
        }

        private static void DestroyParts(ActorTrack tr)
        {
            foreach (var p in tr.Parts) if (p.Owned && p.Mesh) UnityEngine.Object.Destroy(p.Mesh);
            tr.Parts.Clear();
        }

        private static void Prune<T>(List<T> list, float now, Func<T, float> time)
        {
            int drop = 0;
            while (drop < list.Count && now - time(list[drop]) > KeepSeconds) drop++;
            if (drop > 0) list.RemoveRange(0, drop);
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

        // ------------------------------------------------------------------ queries

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

        /// <summary>Held item and its rig (head space) at time t; interpolated when the neighbouring samples hold the same item.</summary>
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
            if (a.Item != b.Item) return true;
            float f = Mathf.InverseLerp(a.T, b.T, t);
            result = new GunSample
            {
                T = t, Item = a.Item,
                RootPos = Vector3.Lerp(a.RootPos, b.RootPos, f), RootRot = Quaternion.Slerp(a.RootRot, b.RootRot, f),
                HasFire = a.HasFire && b.HasFire, FirePos = Vector3.Lerp(a.FirePos, b.FirePos, f), FireRot = Quaternion.Slerp(a.FireRot, b.FireRot, f),
                Rig = Lerp(a.Rig, b.Rig, f)
            };
            return true;
        }

        /// <summary>World-space body rig of another player at time t.</summary>
        public static bool TryGetBody(int ownerId, float t, out RigSample rig)
        {
            rig = default;
            if (!_bodies.TryGetValue(ownerId, out var list) || list.Count == 0) return false;
            if (t < list[0].T - 0.05f || t > list[list.Count - 1].T + 0.15f) return false;
            if (t <= list[0].T) { rig = list[0].Rig; return true; }
            var last = list[list.Count - 1];
            if (t >= last.T) { rig = last.Rig; return true; }
            int lo = Lower(list, t, s => s.T);
            var a = list[lo]; var b = list[lo + 1];
            rig = Lerp(a.Rig, b.Rig, Mathf.InverseLerp(a.T, b.T, t));
            return true;
        }

        /// <summary>Pose of a recorded creature at time t; false when it was not in the world then.</summary>
        public static bool TryGetActor(ActorTrack tr, float t, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            var list = tr.Samples;
            if (list.Count == 0) return false;
            if (t < list[0].T - 0.05f || t > list[list.Count - 1].T + 0.15f) return false;
            if (t <= list[0].T) { pos = list[0].Pos; rot = list[0].Rot; return true; }
            var last = list[list.Count - 1];
            if (t >= last.T) { pos = last.Pos; rot = last.Rot; return true; }
            int lo = Lower(list, t, s => s.T);
            var a = list[lo]; var b = list[lo + 1];
            if (b.T - a.T > 0.5f) { pos = a.Pos; rot = a.Rot; return true; }   // gap: creature was hidden / off-screen, hold
            float f = Mathf.InverseLerp(a.T, b.T, t);
            pos = Vector3.Lerp(a.Pos, b.Pos, f);
            rot = Quaternion.Slerp(a.Rot, b.Rot, f);
            return true;
        }
    }
}
