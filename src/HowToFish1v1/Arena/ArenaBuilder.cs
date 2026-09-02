using HowToFish1v1.Core;
using UnityEngine;

namespace HowToFish1v1.Arena
{
    /// <summary>Builds the arena from ArenaLayout on this peer. Deterministic, so every peer produces identical geometry.</summary>
    public static class ArenaBuilder
    {
        private const string RootName = "HTF1v1_Arena";
        private static GameObject _root;
        private static ArenaLayout _layout;

        public static bool IsBuilt => _root != null;
        public static Vector3 Origin { get; private set; }

        public static void Build()
        {
            if (IsBuilt) return;
            _layout = ArenaLayout.Create();
            float waterY = WaterManager.WaterHeight;
            Origin = new Vector3(600f, waterY + 4f, 600f);

            _root = new GameObject(RootName);
            _root.transform.position = Origin;
            int layer = FirstLayer(GameInfo.LevelLayer);

            foreach (var b in _layout.Boxes)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = b.Name;
                go.layer = layer;
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
            Island.IslandSize = 30f;
            Island.IslandPos = Origin;

            var left = Spawn(Side.Left);
            SpawnManager.PlayerSpawnPos = left.pos;
            SpawnManager.PlayerSpawnRot = left.yaw;
            ModState.SpawnLookup = side => Spawn(side);
            Plugin.Log.LogInfo($"Arena built at {Origin} on layer {layer} with {_layout.Boxes.Count} boxes");
        }

        public static void Destroy()
        {
            if (!IsBuilt) return;
            Object.Destroy(_root);
            _root = null;
            _layout = null;
            ModState.SpawnLookup = null;
            Plugin.Log.LogInfo("Arena destroyed");
        }

        public static (Vector3 pos, float yaw) Spawn(Side side)
        {
            var l = _layout ?? ArenaLayout.Create();
            var s = side == Side.Left ? l.Left : l.Right;
            return (Origin + new Vector3(s.X, s.Y, s.Z), s.Yaw);
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
