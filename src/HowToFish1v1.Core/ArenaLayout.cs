using System;
using System.Collections.Generic;

namespace HowToFish1v1.Core
{
    public enum BoxKind { Concrete, Rust, Steel, Invisible, Wood, Brick, Yellow, Red, Blue, White }

    public struct ArenaBox
    {
        public string Name;
        public float X, Y, Z;      // center, relative to arena origin; Y=0 is floor top
        public float SX, SY, SZ;   // full size
        public float RotX, RotZ;   // Euler degrees
        public BoxKind Kind;
    }

    /// <summary>A practice target on the trickshot map; moving bots patrol between the two points.</summary>
    public struct ArenaBot
    {
        public float X, Y, Z, X2, Y2, Z2;   // Y above the floor: hovering targets float
        public bool Moving;
    }

    /// <summary>A bird circling above the trickshot map.</summary>
    public struct ArenaBird
    {
        public float X, Z, Radius, Height, Speed, Phase;
    }

    public struct ArenaSpawn
    {
        public float X, Y, Z;
        public float Yaw;          // degrees around Y; 90 faces +X, 270 faces -X
    }

    /// <summary>
    /// A 1v1 arena made of boxes. X is the long axis (spawn to spawn), Z the short axis, Y up with the floor top at 0.
    /// Every map is built by a static factory and must satisfy ArenaLayoutTests (symmetry, bounds, spawn cover).
    /// </summary>
    public sealed class ArenaLayout
    {
        public const float CeilingY = 12f;
        private const float Ramp3over6 = 26.565f; // atan(3/6)
        private const float Ramp2over4 = 26.565f; // atan(2/4)
        private const float Ramp3over4 = 36.87f;  // atan(3/4)

        public static readonly string[] MapNames = { "Rust", "Nuketown", "Shipment", "Killhouse", "Trickshot Tower" };
        public static int MapCount => MapNames.Length;
        public const int TrickshotIndex = 4;
        /// <summary>Maps built for one player (no facing spawn pads, no symmetry).</summary>
        public static bool IsSoloMap(int mapIndex) => ((mapIndex % MapCount) + MapCount) % MapCount == TrickshotIndex;

        public string Name { get; private set; }
        public float HalfWidth { get; private set; }
        public float HalfDepth { get; private set; }
        public IReadOnlyList<ArenaBox> Boxes => _boxes;
        public ArenaSpawn Left { get; private set; }
        public ArenaSpawn Right { get; private set; }
        /// <summary>Free-for-all spawns: the two pads plus four spread points, all facing the map center.</summary>
        public IReadOnlyList<ArenaSpawn> FfaSpawns => _ffa;
        public IReadOnlyList<ArenaBot> Bots => _bots;
        public IReadOnlyList<ArenaBird> Birds => _birds;
        /// <summary>Height of the invisible ceiling (higher on the trickshot map).</summary>
        public float Ceiling { get; private set; } = CeilingY;

        private readonly List<ArenaBox> _boxes = new List<ArenaBox>();
        private readonly List<ArenaSpawn> _ffa = new List<ArenaSpawn>();
        private readonly List<ArenaBot> _bots = new List<ArenaBot>();
        private readonly List<ArenaBird> _birds = new List<ArenaBird>();

        /// <summary>Pad position for the index-th of count teammates: spaced 2 m apart along Z on the same pad.</summary>
        public ArenaSpawn TeamSpawn(Side side, int index, int count)
        {
            var pad = side == Side.Left ? Left : Right;
            float spacing = 2f;
            float z = pad.Z + (index - (count - 1) / 2f) * spacing;
            return new ArenaSpawn { X = pad.X, Y = pad.Y, Z = z, Yaw = pad.Yaw };
        }

        /// <summary>Yaw (degrees) that looks from (x, z) toward the origin.</summary>
        public static float YawToCenter(float x, float z) => (float)(Math.Atan2(-x, -z) * 180.0 / Math.PI);

        private void FfaPoint(float x, float z)
        {
            _ffa.Add(new ArenaSpawn { X = x, Y = 0.4f, Z = z, Yaw = YawToCenter(x, z) });
        }

        /// <summary>Builds the map with the given index (wraps around), so any byte from the network is safe.</summary>
        public static ArenaLayout Create(int mapIndex = 0)
        {
            int i = ((mapIndex % MapCount) + MapCount) % MapCount;
            switch (i)
            {
                case 1: return Nuketown();
                case 2: return Shipment();
                case 3: return Killhouse();
                case 4: return Trickshot();
                default: return Rust();
            }
        }

        // ------------------------------------------------------------------ Rust: tower, containers, walkways

        public static ArenaLayout Rust()
        {
            var l = new ArenaLayout { Name = "Rust", HalfWidth = 20f, HalfDepth = 14f };
            l.Floor();
            l.SpawnPads(17f, shieldOffset: 4.5f);

            // Central tower: 4 pillars, a solid core so the middle does not give a spawn-to-spawn line, slab, parapets
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    l.Add("Pillar", 3.5f * sx, 1.5f, 3.5f * sz, 1, 3, 1, BoxKind.Concrete);
            l.Add("TowerCore", 0, 1.5f, 0, 2, 3, 2, BoxKind.Concrete);
            l.Add("TowerSlab", 0, 3.15f, 0, 8, 0.3f, 8, BoxKind.Concrete);
            foreach (float s in new[] { -1f, 1f })
            {
                l.Add("ParapetX", 3.75f * s, 3.8f, 0, 0.5f, 1, 8, BoxKind.Steel);
                foreach (float sx in new[] { -1f, 1f })
                    l.Add("ParapetZ", 2.5f * sx, 3.8f, 3.75f * s, 3, 1, 0.5f, BoxKind.Steel);
                // Ramp: high end at tower edge (z = 4s), low end at z = 10s. +RotX tilts the +Z end down.
                l.Add("Ramp", 0, 1.5f, 7f * s, 2, 0.3f, 6.7f, BoxKind.Concrete, rotX: Ramp3over6 * s);
            }

            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    l.Add("Container", 9f * sx, 1.3f, 7f * sz, 2.4f, 2.6f, 6, BoxKind.Rust);
                    l.Add("Crate", 5f * sx, 0.75f, 10.5f * sz, 1.5f, 1.5f, 1.5f, BoxKind.Steel);
                    l.Add("Crate", 13f * sx, 0.75f, 5f * sz, 1.5f, 1.5f, 1.5f, BoxKind.Steel);
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
                    l.Add("Stairs", 14f * sx, 1f, 12.5f * s, 4.47f, 0.3f, 3, BoxKind.Concrete, rotZ: -Ramp2over4 * sx);
            }
            l.Perimeter();
            l.FfaPoints(13f, 9f);
            return l;
        }

        // ------------------------------------------------------------------ Nuketown: two houses, a bus, cars, fences

        public static ArenaLayout Nuketown()
        {
            var l = new ArenaLayout { Name = "Nuketown", HalfWidth = 27f, HalfDepth = 17f };
            l.Floor();
            l.SpawnPads(23.5f, shieldOffset: 0f); // the house is the spawn cover

            foreach (float s in new[] { -1f, 1f }) l.House(14f * s, s);

            // Street furniture. Point-symmetric like the real map.
            l.Add("Bus", 0, 1.75f, 0, 3, 3.5f, 10, BoxKind.Yellow);
            l.Add("BusBumperN", 0, 0.9f, 5.4f, 2.6f, 1.2f, 0.8f, BoxKind.Steel);
            l.Add("BusBumperS", 0, 0.9f, -5.4f, 2.6f, 1.2f, 0.8f, BoxKind.Steel);
            l.Add("Car", 5f, 0.75f, 5f, 2, 1.5f, 4.5f, BoxKind.Red);
            l.Add("Car", -5f, 0.75f, -5f, 2, 1.5f, 4.5f, BoxKind.Blue);
            l.Add("Fence", 0, 0.6f, 10f, 16, 1.2f, 0.2f, BoxKind.Wood);
            l.Add("Fence", 0, 0.6f, -10f, 16, 1.2f, 0.2f, BoxKind.Wood);
            l.Add("Mailbox", 7f, 0.6f, -8f, 0.6f, 1.2f, 0.6f, BoxKind.Blue);
            l.Add("Mailbox", -7f, 0.6f, 8f, 0.6f, 1.2f, 0.6f, BoxKind.Blue);
            l.Add("Crate", 4f, 0.5f, -12f, 1, 1, 1, BoxKind.Wood);
            l.Add("Crate", -4f, 0.5f, 12f, 1, 1, 1, BoxKind.Wood);
            l.Perimeter();
            l.FfaPoints(14f, 9f);
            return l;
        }

        /// <summary>Two-story house centered at cx. Back wall (toward the spawn) has a door; front has a door and upstairs windows.</summary>
        private void House(float cx, float dir)
        {
            const float hx = 5f, hz = 6f, t = 0.3f;
            float back = cx + hx * dir, front = cx - hx * dir;
            string n = dir < 0 ? "HouseL" : "HouseR";

            Add(n + "Floor", cx, 0.15f, 0, 2 * hx, 0.3f, 2 * hz, BoxKind.Wood);
            // Back wall: ground door gap z in [-1,1]; upper solid
            Add(n + "Back", back, 1.5f, 3.5f, t, 3, 5, BoxKind.White);
            Add(n + "Back", back, 1.5f, -3.5f, t, 3, 5, BoxKind.White);
            Add(n + "BackUp", back, 4.5f, 0, t, 3, 2 * hz, BoxKind.White);
            // Front wall: ground door gap in the middle; upstairs two windows (sill 3..4, opening 4..5, lintel 5..6)
            Add(n + "Front", front, 1.5f, 3.5f, t, 3, 5, BoxKind.White);
            Add(n + "Front", front, 1.5f, -3.5f, t, 3, 5, BoxKind.White);
            Add(n + "Sill", front, 3.5f, 3.5f, t, 1, 5, BoxKind.White);
            Add(n + "Sill", front, 3.5f, -3.5f, t, 1, 5, BoxKind.White);
            Add(n + "Lintel", front, 5.5f, 0, t, 1, 2 * hz, BoxKind.White);
            Add(n + "Mullion", front, 4.5f, 0, t, 1, 2, BoxKind.White);
            // Side walls: ground has a side door gap x in [cx-1, cx+1]; upper solid
            foreach (float sz in new[] { -1f, 1f })
            {
                Add(n + "Side", cx - 3f, 1.5f, hz * sz, 4, 3, t, BoxKind.White);
                Add(n + "Side", cx + 3f, 1.5f, hz * sz, 4, 3, t, BoxKind.White);
                Add(n + "SideUp", cx, 4.5f, hz * sz, 2 * hx, 3, t, BoxKind.White);
            }
            // Second floor: covers z in [-6,2] fully; z in [2,6] except the stair top near the back wall
            Add(n + "Upper", cx, 3.15f, -2f, 2 * hx, 0.3f, 8, BoxKind.Wood);
            Add(n + "Upper", cx - 1.5f * dir, 3.15f, 4f, 7, 0.3f, 4, BoxKind.Wood);
            // Interior stairs along Z near the back wall: ground at z=6 up to y=3 at z=2. +RotX tilts the +Z end down.
            Add(n + "Stairs", cx + 3.5f * dir, 1.5f, 4f, 2, 0.3f, 5, BoxKind.Wood, rotX: Ramp3over4);
            // Upstairs railing at the open stair edge
            Add(n + "Rail", cx - 1.5f * dir, 3.8f, 2f, 7, 1, 0.2f, BoxKind.Wood);
            // Roof
            Add(n + "Roof", cx, 6.15f, 0, 2 * hx + 0.6f, 0.3f, 2 * hz + 0.6f, BoxKind.Brick);
        }

        // ------------------------------------------------------------------ Shipment: container yard

        public static ArenaLayout Shipment()
        {
            var l = new ArenaLayout { Name = "Shipment", HalfWidth = 18f, HalfDepth = 13f };
            l.Floor();
            l.SpawnPads(15f, shieldOffset: 0f);

            foreach (float s in new[] { -1f, 1f })
            {
                // Spawn cover container, long axis along Z, 4.5 m in front of each pad
                l.Add("Container", 10.5f * s, 1.3f, 0, 2.4f, 2.6f, 6, BoxKind.Rust);
                foreach (float sz in new[] { -1f, 1f })
                {
                    l.Add("Container", 5f * s, 1.3f, 4.5f * sz, 6, 2.6f, 2.4f, BoxKind.Rust);      // along X
                    l.Add("Container", 11f * s, 1.3f, 8.5f * sz, 2.4f, 2.6f, 6, BoxKind.Steel);    // along Z
                    l.Add("Crate", 8f * s, 0.6f, 4.5f * sz, 1.2f, 1.2f, 1.2f, BoxKind.Wood);
                }
                // Stacked second tier on the inner X containers
                l.Add("ContainerTop", 5f * s, 3.9f, 4.5f, 6, 2.6f, 2.4f, BoxKind.Rust);
            }
            l.Add("Container", 0, 1.3f, 0, 2.4f, 2.6f, 6, BoxKind.Steel);
            l.Add("Crate", 0, 0.6f, 9f, 1.2f, 1.2f, 1.2f, BoxKind.Wood);
            l.Add("Crate", 0, 0.6f, -9f, 1.2f, 1.2f, 1.2f, BoxKind.Wood);
            l.Perimeter();
            l.FfaPoints(5f, 9f);
            return l;
        }

        // ------------------------------------------------------------------ Killhouse: walls and doorways

        public static ArenaLayout Killhouse()
        {
            var l = new ArenaLayout { Name = "Killhouse", HalfWidth = 18f, HalfDepth = 12f };
            l.Floor();
            l.SpawnPads(15f, shieldOffset: 4.5f);

            foreach (float s in new[] { -1f, 1f })
            {
                // Outer wall line at x = 10.5 with doorways at z in [3,5.5] and [-5.5,-3]
                l.Add("Wall", 10.5f * s, 1.5f, 8.75f * s, 0.3f, 3, 6.5f, BoxKind.Concrete);
                l.Add("Wall", 10.5f * s, 1.5f, -8.75f * s, 0.3f, 3, 6.5f, BoxKind.Concrete);
                // Inner rooms: a wall along X at z = +/-4 from x = 2..8, and a short return
                l.Add("Wall", 5f * s, 1.5f, 4f, 6, 3, 0.3f, BoxKind.Concrete);
                l.Add("Wall", 5f * s, 1.5f, -4f, 6, 3, 0.3f, BoxKind.Concrete);
                l.Add("Wall", 2f * s, 1.5f, 6.5f * s, 0.3f, 3, 5, BoxKind.Concrete);
                l.Add("LowWall", 6f * s, 0.6f, 0, 3, 1.2f, 0.3f, BoxKind.Steel);
                l.Add("Crate", 9f * s, 0.6f, -6.5f * s, 1.2f, 1.2f, 1.2f, BoxKind.Wood);
            }
            l.Add("Core", 0, 1f, 0, 2, 2, 2, BoxKind.Steel);
            l.Add("Core", 0, 1.5f, 8f, 2, 3, 2, BoxKind.Concrete);
            l.Add("Core", 0, 1.5f, -8f, 2, 3, 2, BoxKind.Concrete);
            l.Perimeter();
            l.FfaPoints(5f, 9f);
            return l;
        }

        /// <summary>The two pads plus the four (+/-x, +/-z) quadrant points.</summary>
        private void FfaPoints(float x, float z)
        {
            _ffa.Clear();
            _ffa.Add(Left);
            _ffa.Add(Right);
            FfaPoint(-x, -z);
            FfaPoint(x, z);
            FfaPoint(-x, z);
            FfaPoint(x, -z);
        }

        // ------------------------------------------------------------------ shared pieces

        private void Floor()
        {
            Add("Floor", 0, -0.5f, 0, 2 * HalfWidth, 1, 2 * HalfDepth, BoxKind.Concrete);
        }

        /// <summary>Pads at +/-x facing each other, a back wall behind each, and optionally a shield wall in front.</summary>
        private void SpawnPads(float x, float shieldOffset)
        {
            Left = new ArenaSpawn { X = -x, Y = 0.4f, Z = 0f, Yaw = 90f };
            Right = new ArenaSpawn { X = x, Y = 0.4f, Z = 0f, Yaw = 270f };
            foreach (float s in new[] { -1f, 1f })
            {
                string side = s < 0 ? "L" : "R";
                Add("SpawnPad" + side, x * s, 0.1f, 0, 6, 0.2f, 6, BoxKind.Steel);
                Add("SpawnWall" + side, (x + 2.75f) * s, 1f, 0, 0.5f, 2f, 6, BoxKind.Concrete);
                if (shieldOffset > 0f)
                    Add("SpawnShield" + side, (x - shieldOffset) * s, 1.3f, 0, 0.5f, 2.6f, 6, BoxKind.Concrete);
            }
        }

        private void Perimeter()
        {
            Add("WallN", 0, 3, HalfDepth + 0.25f, 2 * HalfWidth + 0.5f, 6, 0.5f, BoxKind.Invisible);
            Add("WallS", 0, 3, -(HalfDepth + 0.25f), 2 * HalfWidth + 0.5f, 6, 0.5f, BoxKind.Invisible);
            Add("WallE", HalfWidth + 0.25f, 3, 0, 0.5f, 6, 2 * HalfDepth + 0.5f, BoxKind.Invisible);
            Add("WallW", -(HalfWidth + 0.25f), 3, 0, 0.5f, 6, 2 * HalfDepth + 0.5f, BoxKind.Invisible);
            Add("Ceiling", 0, Ceiling, 0, 2 * HalfWidth + 1, 0.5f, 2 * HalfDepth + 1, BoxKind.Invisible);
        }

        // ------------------------------------------------------------------ Trickshot Tower: one high perch, targets below

        public static ArenaLayout Trickshot()
        {
            var l = new ArenaLayout { Name = "Trickshot Tower", HalfWidth = 36f, HalfDepth = 36f, Ceiling = 44f };
            l.Floor();
            // The perch: a tall block at the south edge with a railed deck on top. You spawn on the deck.
            l.Add("Tower", 0, 11f, -28f, 8, 22, 8, BoxKind.Concrete);
            l.Add("TowerDeck", 0, 22.2f, -28f, 10, 0.4f, 10, BoxKind.Steel);
            l.Add("TowerRailBack", 0, 22.95f, -33f, 10, 1.1f, 0.25f, BoxKind.Steel);
            l.Add("TowerRailL", -5f, 22.95f, -28f, 0.25f, 1.1f, 10, BoxKind.Steel);
            l.Add("TowerRailR", 5f, 22.95f, -28f, 0.25f, 1.1f, 10, BoxKind.Steel);
            l.Add("TowerStripe", 0, 11f, -23.95f, 2, 22, 0.1f, BoxKind.Yellow);
            // A lower platform for variety and some cover on the ground.
            l.Add("MidPlatform", -24f, 3f, 22f, 8, 6, 8, BoxKind.Concrete);
            l.Add("MidRail", -24f, 6.5f, 26f, 8, 1f, 0.25f, BoxKind.Steel);
            l.Add("ContainerA", -16f, 1.3f, -2f, 2.4f, 2.6f, 6, BoxKind.Rust);
            l.Add("ContainerB", 18f, 1.3f, 8f, 6, 2.6f, 2.4f, BoxKind.Blue);
            l.Add("ContainerC", 6f, 1.3f, 30f, 2.4f, 2.6f, 6, BoxKind.Red);
            l.Add("LowWall", 0, 1f, 16f, 12, 2, 0.5f, BoxKind.Brick);
            l.Add("Crate1", 8f, 0.75f, 10f, 1.5f, 1.5f, 1.5f, BoxKind.Wood);
            l.Add("Crate2", -8f, 0.75f, 20f, 1.5f, 1.5f, 1.5f, BoxKind.Wood);
            l.Add("Crate3", 14f, 0.75f, 26f, 1.5f, 1.5f, 1.5f, BoxKind.Steel);
            l.Add("Crate4", -26f, 0.75f, 6f, 1.5f, 1.5f, 1.5f, BoxKind.Wood);
            l.Add("Crate5", 26f, 0.75f, -6f, 1.5f, 1.5f, 1.5f, BoxKind.Steel);
            l.Perimeter();
            l.Left = new ArenaSpawn { X = 0, Y = 22.6f, Z = -28f, Yaw = 0f };
            l.Right = l.Left;
            l._ffa.Add(l.Left);
            // Targets: standing ones spread over the field, patrolling ones crossing the open lanes.
            l._bots.Add(new ArenaBot { X = 0, Z = 6 });
            l._bots.Add(new ArenaBot { X = -12, Z = 14 });
            l._bots.Add(new ArenaBot { X = 14, Z = 18 });
            l._bots.Add(new ArenaBot { X = 4, Z = 28 });
            l._bots.Add(new ArenaBot { X = -22, Z = 2 });
            l._bots.Add(new ArenaBot { X = 24, Z = 2 });
            l._bots.Add(new ArenaBot { X = -14, Z = 32 });
            l._bots.Add(new ArenaBot { X = -10, Z = 24, X2 = 10, Z2 = 24, Moving = true });
            l._bots.Add(new ArenaBot { X = 20, Z = -4, X2 = 20, Z2 = 30, Moving = true });
            l._bots.Add(new ArenaBot { X = -30, Z = 10, X2 = -8, Z2 = 32, Moving = true });
            l._bots.Add(new ArenaBot { X = 30, Z = 14, X2 = 12, Z2 = -8, Moving = true });
            // Hovering targets at different heights, one of them drifting up and down across the field.
            l._bots.Add(new ArenaBot { X = 10, Y = 7, Z = 12 });
            l._bots.Add(new ArenaBot { X = -18, Y = 11, Z = 6 });
            l._bots.Add(new ArenaBot { X = 20, Y = 15, Z = 24 });
            l._bots.Add(new ArenaBot { X = -6, Y = 9, Z = 30 });
            l._bots.Add(new ArenaBot { X = -14, Y = 4, Z = 10, X2 = -14, Y2 = 16, Z2 = 26, Moving = true });
            l._bots.Add(new ArenaBot { X = 26, Y = 12, Z = 0, X2 = 4, Y2 = 6, Z2 = 8, Moving = true });
            // Birds circling overhead.
            l._birds.Add(new ArenaBird { X = 0, Z = 8, Radius = 14, Height = 12, Speed = 7, Phase = 0f });
            l._birds.Add(new ArenaBird { X = -10, Z = 20, Radius = 9, Height = 17, Speed = 6, Phase = 2.1f });
            l._birds.Add(new ArenaBird { X = 14, Z = 14, Radius = 11, Height = 8, Speed = 8, Phase = 4.0f });
            l._birds.Add(new ArenaBird { X = 6, Z = 2, Radius = 20, Height = 20, Speed = 9, Phase = 1.0f });
            return l;
        }

        private void Add(string name, float x, float y, float z, float sx, float sy, float sz, BoxKind kind, float rotX = 0, float rotZ = 0)
        {
            _boxes.Add(new ArenaBox { Name = name, X = x, Y = y, Z = z, SX = sx, SY = sy, SZ = sz, RotX = rotX, RotZ = rotZ, Kind = kind });
        }

        /// <summary>True if the segment from a to b passes through any visible box (axis-aligned bounds; rotation ignored).</summary>
        public bool SegmentHitsCover(float ax, float ay, float az, float bx, float by, float bz)
        {
            foreach (var b in _boxes)
            {
                if (b.Kind == BoxKind.Invisible || b.Name == "Floor") continue;
                float minX = b.X - b.SX / 2, maxX = b.X + b.SX / 2;
                float minY = b.Y - b.SY / 2, maxY = b.Y + b.SY / 2;
                float minZ = b.Z - b.SZ / 2, maxZ = b.Z + b.SZ / 2;
                if (SegmentIntersectsAabb(ax, ay, az, bx, by, bz, minX, minY, minZ, maxX, maxY, maxZ)) return true;
            }
            return false;
        }

        private static bool SegmentIntersectsAabb(float ax, float ay, float az, float bx, float by, float bz,
            float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
        {
            float t0 = 0f, t1 = 1f;
            if (!Slab(ax, bx - ax, minX, maxX, ref t0, ref t1)) return false;
            if (!Slab(ay, by - ay, minY, maxY, ref t0, ref t1)) return false;
            if (!Slab(az, bz - az, minZ, maxZ, ref t0, ref t1)) return false;
            return true;
        }

        private static bool Slab(float start, float delta, float min, float max, ref float t0, ref float t1)
        {
            if (Math.Abs(delta) < 1e-6f) return start >= min && start <= max;
            float ta = (min - start) / delta, tb = (max - start) / delta;
            if (ta > tb) { float tmp = ta; ta = tb; tb = tmp; }
            t0 = Math.Max(t0, ta);
            t1 = Math.Min(t1, tb);
            return t0 <= t1;
        }
    }
}
