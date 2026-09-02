using System.Linq;
using HowToFish1v1.Core;
using Xunit;

namespace HowToFish1v1.Tests
{
    public class MatchMachineTests
    {
        private static MatchMachine ReadyLobby(MatchRules rules = null)
        {
            var m = new MatchMachine(rules ?? new MatchRules());
            m.Open();
            m.PlayerJoined(1, "Gavin");
            m.PlayerJoined(2, "Friend");
            m.PlayerSaidHello(1, true);
            m.PlayerSaidHello(2, true);
            m.SetLoadout(1, new byte[] { 10 }, true);
            m.SetLoadout(2, new byte[] { 11, 12 }, true);
            m.Effects.Clear();
            m.Dirty = false;
            return m;
        }

        private static MatchMachine LiveRound(MatchRules rules = null)
        {
            var m = ReadyLobby(rules);
            m.Start(0);
            m.Tick(3.0);
            m.Effects.Clear();
            return m;
        }

        [Fact]
        public void NewMachineIsInactive()
        {
            var m = new MatchMachine(new MatchRules());
            Assert.Equal(MatchPhase.Inactive, m.State.Phase);
        }

        [Fact]
        public void OpenMovesToLobbyAndMarksDirty()
        {
            var m = new MatchMachine(new MatchRules());
            m.Open();
            Assert.Equal(MatchPhase.Lobby, m.State.Phase);
            Assert.True(m.Dirty);
        }

        [Fact]
        public void PlayersFillSlotAThenB_ThirdIgnored()
        {
            var m = new MatchMachine(new MatchRules());
            m.Open();
            m.PlayerJoined(5, "A");
            m.PlayerJoined(6, "B");
            m.PlayerJoined(7, "C");
            Assert.Equal(5, m.State.A.Id);
            Assert.Equal(6, m.State.B.Id);
            Assert.Equal(2, m.State.PresentCount);
        }

        [Fact]
        public void CannotStartUntilBothReadyWithMod()
        {
            var m = new MatchMachine(new MatchRules());
            m.Open();
            m.PlayerJoined(1, "A");
            Assert.False(m.CanStart(out var r1));
            Assert.Contains("two players", r1);
            m.PlayerJoined(2, "B");
            Assert.False(m.CanStart(out var r2));
            Assert.Contains("mod", r2);
            m.PlayerSaidHello(1, true);
            m.PlayerSaidHello(2, true);
            Assert.False(m.CanStart(out var r3));
            Assert.Contains("ready", r3);
            m.SetLoadout(1, new byte[0], true);
            m.SetLoadout(2, new byte[0], true);
            Assert.True(m.CanStart(out _));
        }

        [Fact]
        public void SoloDebugAllowsOnePlayer()
        {
            var m = new MatchMachine(new MatchRules { SoloDebug = true });
            m.Open();
            m.PlayerJoined(1, "A");
            m.PlayerSaidHello(1, true);
            m.SetLoadout(1, new byte[0], true);
            Assert.True(m.CanStart(out _));
        }

        [Fact]
        public void LoadoutIsTruncatedToMax()
        {
            var m = new MatchMachine(new MatchRules { MaxLoadoutGuns = 2 });
            m.Open();
            m.PlayerJoined(1, "A");
            m.SetLoadout(1, new byte[] { 1, 2, 3 }, false);
            Assert.Equal(new byte[] { 1, 2 }, m.State.A.Loadout);
        }

        [Fact]
        public void StartBuildsArenaResetsPlayersAndCountsDown()
        {
            var m = ReadyLobby();
            m.Start(10);
            Assert.Equal(MatchPhase.Countdown, m.State.Phase);
            Assert.Equal(1, m.State.Round);
            Assert.True(m.State.ArenaBuilt);
            Assert.Equal(new[] { EffectKind.BuildArena, EffectKind.ResetPlayers }, m.Effects);
            Assert.Equal(13, m.State.PhaseEndsAt);
            Assert.True(m.State.AIsLeft);
        }

        [Fact]
        public void StartWhenNotReadyOnlySetsStatus()
        {
            var m = new MatchMachine(new MatchRules());
            m.Open();
            m.Start(0);
            Assert.Equal(MatchPhase.Lobby, m.State.Phase);
            Assert.False(string.IsNullOrEmpty(m.State.StatusText));
            Assert.Empty(m.Effects);
        }

        [Fact]
        public void CountdownGoesLiveWhenTimeElapses()
        {
            var m = ReadyLobby();
            m.Start(0);
            m.Tick(2.9);
            Assert.Equal(MatchPhase.Countdown, m.State.Phase);
            m.Tick(3.0);
            Assert.Equal(MatchPhase.Live, m.State.Phase);
        }

        [Fact]
        public void KillDuringCountdownIsIgnored()
        {
            var m = ReadyLobby();
            m.Start(0);
            m.Kill(1, 1.0);
            Assert.Equal(MatchPhase.Countdown, m.State.Phase);
            Assert.Equal(0, m.State.B.Score);
        }

        [Fact]
        public void KillAwardsRoundToOtherPlayer()
        {
            var m = LiveRound();
            m.Kill(1, 5.0);
            Assert.Equal(MatchPhase.RoundEnd, m.State.Phase);
            Assert.Equal(1, m.State.B.Score);
            Assert.Equal(0, m.State.A.Score);
            Assert.Equal(2, m.State.LastRoundWinnerId);
            Assert.Equal(7.0, m.State.PhaseEndsAt);
        }

        [Fact]
        public void SecondKillInSameRoundIgnored()
        {
            var m = LiveRound();
            m.Kill(1, 5.0);
            m.Kill(2, 5.5);
            Assert.Equal(1, m.State.B.Score);
            Assert.Equal(0, m.State.A.Score);
        }

        [Fact]
        public void RoundEndSwapsSidesAndStartsNextRound()
        {
            var m = LiveRound();
            m.Kill(1, 5.0);
            m.Effects.Clear();
            m.Tick(7.0);
            Assert.Equal(MatchPhase.Countdown, m.State.Phase);
            Assert.Equal(2, m.State.Round);
            Assert.False(m.State.AIsLeft);
            Assert.Equal(Side.Right, m.State.SideFor(1));
            Assert.Equal(Side.Left, m.State.SideFor(2));
            Assert.Equal(new[] { EffectKind.ResetPlayers }, m.Effects);
            Assert.False(m.State.A.DeadThisRound);
        }

        [Fact]
        public void ReachingRoundsToWinEndsMatchThenReturnsToLobby()
        {
            var m = LiveRound(new MatchRules { RoundsToWin = 2 });
            double t = 5;
            m.Kill(1, t); t += 2; m.Tick(t);         // B 1-0, countdown
            t += 3; m.Tick(t);                        // live
            m.Kill(1, t); t += 2; m.Tick(t);          // B 2-0 -> MatchEnd
            Assert.Equal(MatchPhase.MatchEnd, m.State.Phase);
            Assert.Equal(2, m.State.MatchWinnerId);
            Assert.Equal(t + 5, m.State.PhaseEndsAt);
            m.Tick(t + 5);
            Assert.Equal(MatchPhase.Lobby, m.State.Phase);
            Assert.Equal(0, m.State.A.Score);
            Assert.Equal(0, m.State.B.Score);
            Assert.False(m.State.A.Ready);
            Assert.False(m.State.B.Ready);
            Assert.True(m.State.ArenaBuilt);
            Assert.Equal(new byte[] { 10 }, m.State.A.Loadout);
        }

        [Fact]
        public void RematchFromLobbyDoesNotRebuildArena()
        {
            var m = LiveRound(new MatchRules { RoundsToWin = 1 });
            m.Kill(2, 5); m.Tick(7); m.Tick(12);
            Assert.Equal(MatchPhase.Lobby, m.State.Phase);
            m.SetLoadout(1, m.State.A.Loadout, true);
            m.SetLoadout(2, m.State.B.Loadout, true);
            m.Effects.Clear();
            m.Start(20);
            Assert.Equal(MatchPhase.Countdown, m.State.Phase);
            Assert.Equal(new[] { EffectKind.ResetPlayers }, m.Effects);
        }

        [Fact]
        public void PlayerLeavingMidMatchReturnsToLobby()
        {
            var m = LiveRound();
            m.PlayerLeft(2);
            Assert.Equal(MatchPhase.Lobby, m.State.Phase);
            Assert.False(m.State.B.IsPresent);
            Assert.Contains("left", m.State.StatusText);
            Assert.Equal(0, m.State.A.Score);
        }

        [Fact]
        public void PlayerLeavingWhileInactiveDoesNothing()
        {
            var m = new MatchMachine(new MatchRules());
            m.PlayerLeft(1);
            Assert.Equal(MatchPhase.Inactive, m.State.Phase);
            Assert.False(m.Dirty);
        }

        [Fact]
        public void QuitDestroysArenaAndGoesInactive()
        {
            var m = LiveRound();
            m.Effects.Clear();
            m.Quit();
            Assert.Equal(MatchPhase.Inactive, m.State.Phase);
            Assert.Equal(new[] { EffectKind.DestroyArena }, m.Effects);
            Assert.False(m.State.ArenaBuilt);
            Assert.False(m.State.A.IsPresent);
        }

        [Fact]
        public void QuitFromLobbyWithoutArenaEmitsNoDestroy()
        {
            var m = new MatchMachine(new MatchRules());
            m.Open();
            m.Quit();
            Assert.Equal(MatchPhase.Inactive, m.State.Phase);
            Assert.Empty(m.Effects);
        }

        [Fact]
        public void SoloKillEndsRoundWithoutScore()
        {
            var m = new MatchMachine(new MatchRules { SoloDebug = true });
            m.Open();
            m.PlayerJoined(1, "A");
            m.PlayerSaidHello(1, true);
            m.SetLoadout(1, new byte[0], true);
            m.Start(0);
            m.Tick(3);
            m.Kill(1, 4);
            Assert.Equal(MatchPhase.RoundEnd, m.State.Phase);
            Assert.Equal(-1, m.State.LastRoundWinnerId);
            Assert.Equal(0, m.State.A.Score);
        }
    }
}
