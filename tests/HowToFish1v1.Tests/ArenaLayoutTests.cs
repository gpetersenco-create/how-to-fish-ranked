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

        /// <summary>The two-sided maps; the solo trickshot map has no facing pads and no symmetry.</summary>
        public static IEnumerable<object[]> Maps() =>
            Enumerable.Range(0, ArenaLayout.MapCount).Where(i => !ArenaLayout.IsSoloMap(i)).Select(i => new object[] { i });

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
        public void FfaSpawnsAreSixSpreadPointsInsideTheArenaFacingCenter(int map)
        {
            var l = ArenaLayout.Create(map);
            Assert.Equal(6, l.FfaSpawns.Count);
            for (int i = 0; i < l.FfaSpawns.Count; i++)
            {
                var s = l.FfaSpawns[i];
                Assert.True(Math.Abs(s.X) < l.HalfWidth - 1 && Math.Abs(s.Z) < l.HalfDepth - 1, $"{l.Name}: FFA spawn {i} outside");
                for (int j = 0; j < i; j++)
                {
                    var o = l.FfaSpawns[j];
                    float d = (float)Math.Sqrt((s.X - o.X) * (s.X - o.X) + (s.Z - o.Z) * (s.Z - o.Z));
                    Assert.True(d >= 8f, $"{l.Name}: FFA spawns {i} and {j} too close ({d:0.0} m)");
                }
                // Not standing inside a visible box
                foreach (var b in l.Boxes.Where(b => b.Kind != BoxKind.Invisible && b.Name != "Floor" && !b.Name.StartsWith("SpawnPad")))
                {
                    bool inside = Math.Abs(s.X - b.X) < b.SX / 2 && Math.Abs(s.Z - b.Z) < b.SZ / 2 && b.Y - b.SY / 2 < 1.5f && b.Y + b.SY / 2 > 0.2f;
                    Assert.False(inside, $"{l.Name}: FFA spawn {i} is inside {b.Name}");
                }
            }
            Assert.True(Near(ArenaLayout.YawToCenter(-5f, 0f), 90f), "left of center should face +X (yaw 90)");
            Assert.True(Near(Math.Abs(ArenaLayout.YawToCenter(0f, 5f)), 180f), "in front of center should face -Z (yaw 180)");
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
    
        [Fact]
        public void TrickshotMapHasPerchAndBots()
        {
            var l = ArenaLayout.Create(ArenaLayout.TrickshotIndex);
            Assert.Contains(l.Boxes, b => b.Name == "Floor");
            Assert.True(l.Left.Y > 15f, "spawn is high up");
            Assert.True(l.Bots.Count >= 6);
            Assert.Contains(l.Bots, b => b.Moving);
            Assert.Contains(l.Bots, b => !b.Moving);
            Assert.Contains(l.Bots, b => b.Y > 3f);
            Assert.True(l.Birds.Count >= 3);
            Assert.True(l.Ceiling > l.Left.Y + 5f, "ceiling above the perch");
        }

        [Fact]
        public void TrickshotModeIsSoloAndPicksItsMap()
        {
            var m = new MatchMachine(new MatchRules());
            m.Open();
            m.PlayerJoined(1, "a");
            m.PlayerSaidHello(1, true);
            m.SetLoadout(1, new byte[0], true);
            m.SetMode(MatchMode.Trickshot);
            Assert.Equal(ArenaLayout.TrickshotIndex, m.State.MapIndex);
            Assert.True(m.CanStart(out var why), why);
            m.Start(0); m.Tick(3);
            Assert.Equal(MatchPhase.Live, m.State.Phase);
            m.Kill(1, -1, 4);                                   // fell: respawn, no round end
            Assert.Equal(MatchPhase.Live, m.State.Phase);
            m.EndTrickshot(1, 5, 3);
            Assert.Equal(MatchPhase.MatchEnd, m.State.Phase);
            Assert.Equal(1, m.State.MatchWinnerId);
            m.Tick(5 + m.Rules.MatchEndSeconds);
            Assert.Equal(MatchPhase.Lobby, m.State.Phase);
            m.SetMode(MatchMode.OneVOne);
            Assert.Equal(0, m.State.MapIndex);
        }
}
}
