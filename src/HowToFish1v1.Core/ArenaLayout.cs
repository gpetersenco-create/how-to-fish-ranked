using System;
using System.Collections.Generic;

namespace HowToFish1v1.Core
{
    public enum BoxKind { Concrete, Rust, Steel, Invisible }

    public struct ArenaBox
    {
        public string Name;
        public float X, Y, Z;      // center, relative to arena origin; Y=0 is floor top
        public float SX, SY, SZ;   // full size
        public float RotX, RotZ;   // Euler degrees
        public BoxKind Kind;
    }

    public struct ArenaSpawn
    {
        public float X, Y, Z;
        public float Yaw;          // degrees around Y; 90 faces +X, 270 faces -X
    }

    /// <summary>Symmetric Rust/Shipment-style 1v1 arena. X is the long axis (spawn to spawn).</summary>
    public sealed class ArenaLayout
    {
        public const float HalfWidth = 20f;
        public const float HalfDepth = 14f;
        public const float CeilingY = 12f;

        private const float RampAngle = 26.565f; // atan(3/6) and atan(2/4)

        public IReadOnlyList<ArenaBox> Boxes => _boxes;
        public ArenaSpawn Left { get; private set; }
        public ArenaSpawn Right { get; private set; }

        private readonly List<ArenaBox> _boxes = new List<ArenaBox>();

        public static ArenaLayout Create()
        {
            var l = new ArenaLayout();
            l.Left = new ArenaSpawn { X = -17f, Y = 0.4f, Z = 0f, Yaw = 90f };
            l.Right = new ArenaSpawn { X = 17f, Y = 0.4f, Z = 0f, Yaw = 270f };

            // Floor
            l.Add("Floor", 0, -0.5f, 0, 40, 1, 28, BoxKind.Concrete);

            // Spawn pads + back walls
            foreach (float s in new[] { -1f, 1f })
            {
                string side = s < 0 ? "L" : "R";
                l.Add("SpawnPad" + side, 17f * s, 0.1f, 0, 6, 0.2f, 6, BoxKind.Steel);
                l.Add("SpawnWall" + side, 19.75f * s, 1f, 0, 0.5f, 2f, 6, BoxKind.Concrete);
            }

            // Central tower: 4 pillars, slab, parapets (Z-side parapets split to leave ramp gaps)
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    l.Add("Pillar", 3.5f * sx, 1.5f, 3.5f * sz, 1, 3, 1, BoxKind.Concrete);
            l.Add("TowerSlab", 0, 3.15f, 0, 8, 0.3f, 8, BoxKind.Concrete);
            foreach (float s in new[] { -1f, 1f })
            {
                l.Add("ParapetX", 3.75f * s, 3.8f, 0, 0.5f, 1, 8, BoxKind.Steel);
                foreach (float sx in new[] { -1f, 1f })
                    l.Add("ParapetZ", 2.5f * sx, 3.8f, 3.75f * s, 3, 1, 0.5f, BoxKind.Steel);
                // Ramp: high end at tower edge (z = 4s), low end at z = 10s. +RotX tilts the +Z end down.
                l.Add("Ramp", 0, 1.5f, 7f * s, 2, 0.3f, 6.7f, BoxKind.Concrete, rotX: RampAngle * s);
            }

            // Containers (long axis along Z)
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    l.Add("Container", 9f * sx, 1.3f, 7f * sz, 2.4f, 2.6f, 6, BoxKind.Rust);

            // Crates
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    l.Add("Crate", 5f * sx, 0.75f, 10.5f * sz, 1.5f, 1.5f, 1.5f, BoxKind.Steel);
                    l.Add("Crate", 13f * sx, 0.75f, 4f * sz, 1.5f, 1.5f, 1.5f, BoxKind.Steel);
                }

            // Side walkways with inner parapet, pillars, and stairs at both ends
            foreach (float s in new[] { -1f, 1f })
            {
                l.Add("Walkway", 0, 2f, 12.5f * s, 24, 0.3f, 3, BoxKind.Concrete);
                l.Add("WalkParapet", 0, 2.65f, 11.25f * s, 24, 1, 0.5f, BoxKind.Steel);
                foreach (float px in new[] { -11f, 0f, 11f })
                    l.Add("WalkPillar", px, 1f, 12.5f * s, 0.5f, 2, 0.5f, BoxKind.Concrete);
                foreach (float sx in new[] { -1f, 1f })
                    // Stairs: high end at x = 12sx, low end at x = 16sx. +RotZ raises the +X end, so the +X stairs need -angle.
                    l.Add("Stairs", 14f * sx, 1f, 12.5f * s, 4.47f, 0.3f, 3, BoxKind.Concrete, rotZ: -RampAngle * sx);
            }

            // Perimeter: 4 invisible walls + ceiling
            l.Add("WallN", 0, 3, HalfDepth + 0.25f, 40.5f, 6, 0.5f, BoxKind.Invisible);
            l.Add("WallS", 0, 3, -(HalfDepth + 0.25f), 40.5f, 6, 0.5f, BoxKind.Invisible);
            l.Add("WallE", HalfWidth + 0.25f, 3, 0, 0.5f, 6, 28.5f, BoxKind.Invisible);
            l.Add("WallW", -(HalfWidth + 0.25f), 3, 0, 0.5f, 6, 28.5f, BoxKind.Invisible);
            l.Add("Ceiling", 0, CeilingY, 0, 41, 0.5f, 29, BoxKind.Invisible);
            return l;
        }

        private void Add(string name, float x, float y, float z, float sx, float sy, float sz, BoxKind kind, float rotX = 0, float rotZ = 0)
        {
            _boxes.Add(new ArenaBox { Name = name, X = x, Y = y, Z = z, SX = sx, SY = sy, SZ = sz, RotX = rotX, RotZ = rotZ, Kind = kind });
        }
    }
}
