using System;
using System.Collections.Generic;
using System.Linq;
using HowToFish1v1.Core;
using Xunit;

namespace HowToFish1v1.Tests
{
    public class ArenaLayoutTests
    {
        private static bool Near(float a, float b) => Math.Abs(a - b) < 0.001f;

        public static IEnumerable<object[]> Maps() =>
            Enumerable.Range(0, ArenaLayout.MapCount).Select(i => new object[] { i });

        [Fact]
        public void MapIndexWrapsAndNamesMatch()
        {
            Assert.Equal(4, ArenaLayout.MapCount);
            for (int i = 0; i < ArenaLayout.MapCount; i++)
                Assert.Equal(ArenaLayout.MapNames[i], ArenaLayout.Create(i).Name);
            Assert.Equal(ArenaLayout.MapNames[1], ArenaLayout.Create(1 + ArenaLayout.MapCount).Name);
            Assert.Equal(ArenaLayout.MapNames[ArenaLayout.MapCount - 1], ArenaLayout.Create(-1).Name);
        }

        [Theory, MemberData(nameof(Maps))]
        public void HasFloorAndTwoSpawnsFarApart(int map)
        {
            var l = ArenaLayout.Create(map);
            Assert.Contains(l.Boxes, b => b.Name == "Floor");
            float dx = l.Right.X - l.Left.X;
            Assert.True(dx >= 28f, l.Name + ": spawns should be at least 28 m apart");
            Assert.True(Near(l.Left.Yaw, 90f) && Near(l.Right.Yaw, 270f), "spawns face each other");
        }

        [Theory, MemberData(nameof(Maps))]
        public void VisibleCoverIsSymmetric(int map)
        {
            var l = ArenaLayout.Create(map);
            var visible = l.Boxes.Where(b => b.Kind != BoxKind.Invisible && !Near(b.X, 0f)).ToList();
            Assert.NotEmpty(visible);
            foreach (var b in visible)
            {
                // Mirror across X (Z kept) or 180-degree rotation about Y (X and Z negated): both give fair sides.
                bool mirrored = visible.Any(o =>
                    Near(o.X, -b.X) && Near(o.Y, b.Y) && Near(o.Z, b.Z) &&
                    Near(o.SX, b.SX) && Near(o.SY, b.SY) && Near(o.SZ, b.SZ) &&
                    Near(o.RotX, b.RotX) && Near(o.RotZ, -b.RotZ));
                bool rotated = visible.Any(o =>
                    Near(o.X, -b.X) && Near(o.Y, b.Y) && Near(o.Z, -b.Z) &&
                    Near(o.SX, b.SX) && Near(o.SY, b.SY) && Near(o.SZ, b.SZ) &&
                    Near(o.RotX, -b.RotX) && Near(o.RotZ, -b.RotZ));
                Assert.True(mirrored || rotated, l.Name + ": no symmetric twin for " + b.Name + " at " + b.X + "," + b.Z);
            }
        }

        [Theory, MemberData(nameof(Maps))]
        public void EverythingVisibleFitsInsidePerimeter(int map)
        {
            var l = ArenaLayout.Create(map);
            foreach (var b in l.Boxes.Where(b => b.Kind != BoxKind.Invisible))
            {
                float halfX = b.SX / 2f + Math.Abs(b.SZ / 2f * (float)Math.Sin(b.RotZ * Math.PI / 180));
                float halfZ = b.SZ / 2f + Math.Abs(b.SZ / 2f * (float)Math.Sin(b.RotX * Math.PI / 180));
                Assert.True(Math.Abs(b.X) + halfX <= l.HalfWidth + 0.001f, l.Name + ": " + b.Name + " exceeds X bound");
                Assert.True(Math.Abs(b.Z) + halfZ <= l.HalfDepth + 0.001f, l.Name + ": " + b.Name + " exceeds Z bound");
                Assert.True(b.Y + b.SY / 2f <= ArenaLayout.CeilingY, l.Name + ": " + b.Name + " exceeds ceiling");
            }
        }

        [Theory, MemberData(nameof(Maps))]
        public void OnlyFloorGoesBelowGround(int map)
        {
            var l = ArenaLayout.Create(map);
            foreach (var b in l.Boxes.Where(b => b.Name != "Floor" && b.Kind != BoxKind.Invisible))
            {
                float bottom = b.Y - b.SY / 2f - Math.Abs(b.SZ / 2f * (float)Math.Sin(b.RotX * Math.PI / 180)) - Math.Abs(b.SX / 2f * (float)Math.Sin(b.RotZ * Math.PI / 180));
                Assert.True(bottom >= -0.5f, l.Name + ": " + b.Name + " is below ground");
            }
        }

        [Theory, MemberData(nameof(Maps))]
        public void SpawnsAreAboveTheirPads(int map)
        {
            var l = ArenaLayout.Create(map);
            var pads = l.Boxes.Where(b => b.Name.StartsWith("SpawnPad")).ToList();
            Assert.Equal(2, pads.Count);
            Assert.Contains(pads, p => Near(p.X, l.Left.X));
            Assert.Contains(pads, p => Near(p.X, l.Right.X));
            Assert.True(l.Left.Y > 0.2f && l.Right.Y > 0.2f);
        }

        [Theory, MemberData(nameof(Maps))]
        public void PerimeterHasFourWallsAndCeiling(int map)
        {
            var l = ArenaLayout.Create(map);
            Assert.Equal(5, l.Boxes.Count(b => b.Kind == BoxKind.Invisible));
        }

        [Theory, MemberData(nameof(Maps))]
        public void NoLineOfSightBetweenSpawnPads(int map)
        {
            // From anywhere on one pad at head height to anywhere on the other pad (and crouch height), something must be in the way.
            var l = ArenaLayout.Create(map);
            var offsets = new[] { -2.5f, -1.25f, 0f, 1.25f, 2.5f };
            var heights = new[] { 0.6f, 1.2f, 1.7f };
            foreach (float za in offsets)
                foreach (float xa in offsets)
                    foreach (float zb in offsets)
                        foreach (float xb in offsets)
                            foreach (float ha in heights)
                                foreach (float hb in heights)
                                {
                                    bool blocked = l.SegmentHitsCover(
                                        l.Left.X + xa, l.Left.Y + ha, l.Left.Z + za,
                                        l.Right.X + xb, l.Right.Y + hb, l.Right.Z + zb);
                                    Assert.True(blocked, $"{l.Name}: open line from L({xa},{ha},{za}) to R({xb},{hb},{zb})");
                                }
        }
    }
}
