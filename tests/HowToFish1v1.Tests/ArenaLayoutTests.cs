using System;
using System.Linq;
using HowToFish1v1.Core;
using Xunit;

namespace HowToFish1v1.Tests
{
    public class ArenaLayoutTests
    {
        private static bool Near(float a, float b) => Math.Abs(a - b) < 0.001f;

        [Fact]
        public void HasFloorAndTwoSpawnsFarApart()
        {
            var l = ArenaLayout.Create();
            Assert.Contains(l.Boxes, b => b.Name == "Floor");
            float dx = l.Right.X - l.Left.X;
            Assert.True(dx >= 30f, "spawns should be at least 30 m apart");
            Assert.True(Near(l.Left.Yaw, 90f) && Near(l.Right.Yaw, 270f), "spawns face each other");
        }

        [Fact]
        public void VisibleCoverIsMirroredAcrossX()
        {
            var l = ArenaLayout.Create();
            var visible = l.Boxes.Where(b => b.Kind != BoxKind.Invisible && !Near(b.X, 0f)).ToList();
            Assert.NotEmpty(visible);
            foreach (var b in visible)
            {
                bool hasMirror = visible.Any(o =>
                    Near(o.X, -b.X) && Near(o.Y, b.Y) && Near(o.Z, b.Z) &&
                    Near(o.SX, b.SX) && Near(o.SY, b.SY) && Near(o.SZ, b.SZ) &&
                    Near(o.RotX, b.RotX) && Near(o.RotZ, -b.RotZ) && o.Kind == b.Kind);
                Assert.True(hasMirror, "no X mirror for " + b.Name);
            }
        }

        [Fact]
        public void EverythingVisibleFitsInsidePerimeter()
        {
            var l = ArenaLayout.Create();
            foreach (var b in l.Boxes.Where(b => b.Kind != BoxKind.Invisible))
            {
                float halfX = b.SX / 2f + Math.Abs(b.SZ / 2f * (float)Math.Sin(b.RotZ * Math.PI / 180));
                float halfZ = b.SZ / 2f + Math.Abs(b.SZ / 2f * (float)Math.Sin(b.RotX * Math.PI / 180));
                Assert.True(Math.Abs(b.X) + halfX <= ArenaLayout.HalfWidth + 0.001f, b.Name + " exceeds X bound");
                Assert.True(Math.Abs(b.Z) + halfZ <= ArenaLayout.HalfDepth + 0.001f, b.Name + " exceeds Z bound");
                Assert.True(b.Y + b.SY / 2f <= ArenaLayout.CeilingY, b.Name + " exceeds ceiling");
            }
        }

        [Fact]
        public void OnlyFloorGoesBelowGround()
        {
            var l = ArenaLayout.Create();
            foreach (var b in l.Boxes.Where(b => b.Name != "Floor" && b.Kind != BoxKind.Invisible))
            {
                float bottom = b.Y - b.SY / 2f - Math.Abs(b.SZ / 2f * (float)Math.Sin(b.RotX * Math.PI / 180)) - Math.Abs(b.SX / 2f * (float)Math.Sin(b.RotZ * Math.PI / 180));
                Assert.True(bottom >= -0.5f, b.Name + " is below ground");
            }
        }

        [Fact]
        public void SpawnsAreAboveTheirPads()
        {
            var l = ArenaLayout.Create();
            var pads = l.Boxes.Where(b => b.Name.StartsWith("SpawnPad")).ToList();
            Assert.Equal(2, pads.Count);
            Assert.Contains(pads, p => Near(p.X, l.Left.X));
            Assert.Contains(pads, p => Near(p.X, l.Right.X));
            Assert.True(l.Left.Y > 0.2f && l.Right.Y > 0.2f);
        }

        [Fact]
        public void PerimeterHasFourWallsAndCeiling()
        {
            var l = ArenaLayout.Create();
            Assert.Equal(5, l.Boxes.Count(b => b.Kind == BoxKind.Invisible));
        }
    }
}
