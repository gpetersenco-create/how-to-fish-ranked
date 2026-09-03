using System.Collections.Generic;
using HarmonyLib;
using HowToFish1v1.Core;
using UnityEngine;

namespace HowToFish1v1.Arena
{
    /// <summary>What a piece of arena is made of, for ricochets.</summary>
    public sealed class ArenaSurface : MonoBehaviour
    {
        public BoxKind Kind;
        public bool IsFloor;
        /// <summary>Metal and concrete bounce bullets; wood, brick and the ground swallow them.</summary>
        public bool Bounces => !IsFloor && (Kind == BoxKind.Concrete || Kind == BoxKind.Steel || Kind == BoxKind.Rust
            || Kind == BoxKind.Yellow || Kind == BoxKind.Red || Kind == BoxKind.Blue || Kind == BoxKind.White);
    }

    /// <summary>Builds an arena from ArenaLayout on this peer. Deterministic, so every peer produces identical geometry.</summary>
    public static class ArenaBuilder
    {
        private const string RootName = "HTF1v1_Arena";
        private const float SpawnLift = 1.6f;
        private static GameObject _root;
        private static ArenaLayout _layout;

        public static bool IsBuilt => _root != null;
        public static int MapIndex { get; private set; } = -1;
        public static Vector3 Origin { get; private set; }
        public static ArenaLayout Layout => _layout ?? ArenaLayout.Create();

        public static void Build(int mapIndex)
        {
            if (IsBuilt) return;
            _layout = ArenaLayout.Create(mapIndex);
            MapIndex = mapIndex;
            Origin = new Vector3(600f, SafeFloorHeight(), 600f);

            _root = new GameObject(RootName);
            _root.transform.position = Origin;
            int layer = FirstLayer(GameInfo.LevelLayer);

            foreach (var b in _layout.Boxes)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = b.Name;
                go.layer = layer;
                go.tag = "Level";
                go.transform.SetParent(_root.transform, false);
                go.transform.localPosition = new Vector3(b.X, b.Y, b.Z);
                go.transform.localRotation = Quaternion.Euler(b.RotX, 0f, b.RotZ);
                go.transform.localScale = new Vector3(b.SX, b.SY, b.SZ);
                var surf = go.AddComponent<ArenaSurface>();
                surf.Kind = b.Kind; surf.IsFloor = b.Name == "Floor";
                if (b.Kind == BoxKind.Invisible)
                {
                    Object.Destroy(go.GetComponent<MeshRenderer>());
                }
                else
                {
                    // Same collider (a scaled unit cube), but a mesh whose UVs tile in world metres.
                    go.GetComponent<MeshFilter>().sharedMesh = WorldUvBox.For(b.SX, b.SY, b.SZ);
                    go.GetComponent<MeshRenderer>().sharedMaterial = b.Name == "Floor" ? ArenaMaterials.Floor() : ArenaMaterials.For(b.Kind);
                }
            }

            // The game expects an Island to exist (spawn waits on Island.CurIsland).
            _root.AddComponent<Island>();
            Island.IslandSize = Mathf.Max(_layout.HalfWidth, _layout.HalfDepth) + 10f;
            Island.IslandPos = Origin;

            var left = Spawn(Side.Left, 0, 1);
            SpawnManager.PlayerSpawnPos = left.pos;
            SpawnManager.PlayerSpawnRot = left.yaw;
            ModState.SpawnLookup = (side, index, count) => Spawn(side, index, count);
            Plugin.Log.LogInfo($"Arena '{_layout.Name}' built at {Origin} on layer {layer} with {_layout.Boxes.Count} boxes");
        }

        public static void Destroy()
        {
            if (!IsBuilt) return;
            Object.Destroy(_root);
            _root = null;
            _layout = null;
            MapIndex = -1;
            ModState.SpawnLookup = null;
            Plugin.Log.LogInfo("Arena destroyed");
        }

        /// <summary>World spawn for the index-th of count teammates on a side. Lifted so the capsule never overlaps the pad.</summary>
        public static (Vector3 pos, float yaw) Spawn(Side side, int index, int count)
        {
            var s = Layout.TeamSpawn(side, index, count);
            return (Origin + new Vector3(s.X, s.Y + SpawnLift, s.Z), s.Yaw);
        }

        public static (Vector3 pos, float yaw) Spawn(Side side) => Spawn(side, 0, 1);

        /// <summary>World free-for-all spawns.</summary>
        public static List<(Vector3 pos, float yaw)> FfaSpawns()
        {
            var list = new List<(Vector3, float)>();
            foreach (var s in Layout.FfaSpawns)
                list.Add((Origin + new Vector3(s.X, s.Y + SpawnLift, s.Z), s.Yaw));
            return list;
        }

        /// <summary>
        /// Floor height that stays above every wave crest. The game treats a head below the wave-adjusted water height as
        /// drowning, so the mean water level is not enough; the crest amplitude comes from the water material.
        /// </summary>
        private static float SafeFloorHeight()
        {
            float mean = WaterManager.WaterHeight;
            float crest = 0f;
            try
            {
                var t = Traverse.Create(typeof(WaterManager));
                float h1 = Mathf.Abs(t.Field<float>("_waveHeight1").Value);
                float h2 = Mathf.Abs(t.Field<float>("_waveHeight2").Value);
                float scale = Mathf.Abs(t.Field<float>("_modelScale").Value);
                crest = (h1 + h2) * scale;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Could not read wave parameters: " + e.Message);
            }
            if (crest <= 0f || float.IsNaN(crest)) crest = 10f;
            float floor = mean + crest + 4f;
            Plugin.Log.LogInfo($"Water mean {mean:0.00}, wave crest up to {crest:0.00}, arena floor at {floor:0.00}");
            return floor;
        }

        private static int FirstLayer(LayerMask mask)
        {
            int v = mask.value;
            for (int i = 0; i < 32; i++)
                if ((v & (1 << i)) != 0) return i;
            return 0;
        }
    }
}
