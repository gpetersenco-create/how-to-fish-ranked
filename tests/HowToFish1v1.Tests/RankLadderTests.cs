using HowToFish1v1.Core;
using Xunit;

namespace HowToFish1v1.Tests
{
    public class RankLadderTests
    {
        [Fact]
        public void DefaultLadderStartsAtMasterBaiterAndTopsAtPoseidon()
        {
            var l = new RankLadder();
            Assert.Equal(10, l.Names.Length);
            Assert.Equal("Master Baiter", l.TierName(0));
            Assert.Equal("Master Baiter", l.TierName(99));
            Assert.Equal("Bottom Feeder", l.TierName(100));
            Assert.Equal("Poseidon", l.TierName(900));
            Assert.Equal("Poseidon", l.TierName(5000));
        }

        [Fact]
        public void PointsToNextCountsDownAndIsZeroAtTop()
        {
            var l = new RankLadder();
            Assert.Equal(100, l.PointsToNext(0));
            Assert.Equal(1, l.PointsToNext(99));
            Assert.Equal(0, l.PointsToNext(900));
        }

        [Fact]
        public void ApplyAddsWinSubtractsLossAndFloorsAtZero()
        {
            var l = new RankLadder();
            Assert.Equal(20, l.Apply(0, true, false));
            Assert.Equal(0, l.Apply(5, false, false));
            Assert.Equal(90, l.Apply(100, false, false));
            Assert.Equal(95, l.Apply(100, false, true));
            Assert.Equal(120, l.Apply(100, true, true));
        }

        [Fact]
        public void CustomNamesFromCsvAndBlankFallsBack()
        {
            var l = new RankLadder(" Guppy , Shark ", 50);
            Assert.Equal(new[] { "Guppy", "Shark" }, l.Names);
            Assert.Equal("Shark", l.TierName(50));
            var d = new RankLadder("   ");
            Assert.Equal(10, d.Names.Length);
        }
    }
}
