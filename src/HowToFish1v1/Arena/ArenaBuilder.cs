using HarmonyLib;
using HowToFish1v1.Core;
using UnityEngine;

namespace HowToFish1v1.Arena
{
    /// <summary>Builds an arena from ArenaLayout on this peer. Deterministic, so every peer produces identical geometry.</summary>
    public static class ArenaBuilder
    {
        private const string RootName = "HTF1v1_Arena";
        private static GameObject _root;
        private static ArenaLayout _layout;

        public static bool IsBuilt => _root != null;
        public static int MapIndex { get; private set; } = -1;
        public static Vector3 Origin { get; private set; }

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
                if (b.Kind == BoxKind.Invisible)
                {
                    Object.Destroy(go.GetComponent<MeshRenderer>());
                }
                else
                {
                    go.GetComponent<MeshRenderer>().sharedMaterial = ArenaMaterials.For(b.Kind);
                }
            }

            // The game expects an Island to exist (spawn waits on Island.CurIsland).
            _root.AddComponent<Island>();
            Island.IslandSize = Mathf.Max(_layout.HalfWidth, _layout.HalfDepth) + 10f;
            Island.IslandPos = Origin;

            var left = Spawn(Side.Left);
            SpawnManager.PlayerSpawnPos = left.pos;
            SpawnManager.PlayerSpawnRot = left.yaw;
            ModState.SpawnLookup = side => Spawn(side);
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

        public static (Vector3 pos, float yaw) Spawn(Side side)
        {
            var l = _layout ?? ArenaLayout.Create();
            var s = side == Side.Left ? l.Left : l.Right;
            // Layout spawns are feet positions; the player's rigidbody is teleported by its center, so lift it clear of the pad
            // and let it drop. Overlapping the pad collider would fling the player with depenetration.
            return (Origin + new Vector3(s.X, s.Y + 1.6f, s.Z), s.Yaw);
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
