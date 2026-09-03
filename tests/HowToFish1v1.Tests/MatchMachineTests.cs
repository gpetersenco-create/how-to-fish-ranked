using System.Linq;
using HowToFish1v1.Core;
using Xunit;

namespace HowToFish1v1.Tests
{
    public class MatchMachineTests
    {
        private static MatchMachine Lobby(MatchMode mode, int players, MatchRules rules = null)
        {
            var m = new MatchMachine(rules ?? new MatchRules());
            m.Open();
            m.SetMode(mode);
            for (int i = 1; i <= players; i++)
            {
                m.PlayerJoined(i, "P" + i);
                m.PlayerSaidHello(i, true);
                m.SetLoadout(i, LoadoutCodec.Encode(new[] { new LoadoutGun((byte)(10 + i)) }), true);
            }
            m.Effects.Clear();
            m.Dirty = false;
            return m;
        }

        private static MatchMachine Live(MatchMode mode, int players, MatchRules rules = null)
        {
            var m = Lobby(mode, players, rules);
            m.Start(0);
            m.Tick(3.0);
            m.Effects.Clear();
            return m;
        }

        private static EffectKind[] Kinds(MatchMachine m) => m.Effects.Select(e => e.Kind).ToArray();

        // ------------------------------------------------------------ lobby

        [Fact]
        public void NewMachineIsInactive_OpenGoesToLobby()
        {
            var m = new MatchMachine(new MatchRules());
            Assert.Equal(MatchPhase.Inactive, m.State.Phase);
            m.Open();
            Assert.Equal(MatchPhase.Lobby, m.State.Phase);
            Assert.True(m.Dirty);
        }

        [Fact]
        public void PlayersAlternateTeamsAndCapAtEight()
        {
            var m = new MatchMachine(new MatchRules());
            m.Open();
            for (int i = 1; i <= 10; i++) m.PlayerJoined(i, "P" + i);
            Assert.Equal(8, m.State.PresentCount);
            Assert.Equal(4, m.State.TeamCount(0));
            Assert.Equal(4, m.State.TeamCount(1));
            Assert.Equal(0, m.State.TeamOf(1));
            Assert.Equal(1, m.State.TeamOf(2));
        }

        [Fact]
        public void CanStartChecksCountsTeamsModAndReady()
        {
            var m = new MatchMachine(new MatchRules());
            m.Open();
            m.SetMode(MatchMode.TwoVTwo);
            m.PlayerJoined(1, "A");
            Assert.False(m.CanStart(out var r1));
            Assert.Contains("needs 2", r1);
            m.PlayerJoined(2, "B"); m.PlayerJoined(3, "C"); m.PlayerJoined(4, "D");
            m.MoveTeam(3);   // now 3 vs 1: over the 2-per-team cap
            Assert.False(m.CanStart(out var r2));
            Assert.Contains("at most 2", r2);
            m.MoveTeam(3);
            Assert.False(m.CanStart(out var r3));
            Assert.Contains("mod", r3);
            foreach (int i in new[] { 1, 2, 3, 4 }) m.PlayerSaidHello(i, true);
            Assert.False(m.CanStart(out var r4));
            Assert.Contains("ready", r4);
            foreach (int i in new[] { 1, 2, 3, 4 }) m.SetLoadout(i, new byte[0], true);
            Assert.True(m.CanStart(out _));
        }

        [Fact]
        public void OneVOneRejectsThirdPlayer_FfaAllowsUpToEight()
        {
            var m = Lobby(MatchMode.OneVOne, 3);
            Assert.False(m.CanStart(out var r));
            Assert.Contains("at most 2", r);
            var f = Lobby(MatchMode.FreeForAll, 8);
            Assert.True(f.CanStart(out _));
        }

        [Fact]
        public void TeamModesAllowUnevenTeamsButNotEmptyOnes()
        {
            var m = Lobby(MatchMode.TwoVTwo, 3);   // teams {1,3} vs {2}
            Assert.True(m.CanStart(out _));
            m.MoveTeam(2);                          // everyone on team 0
            Assert.False(m.CanStart(out var r));
            Assert.Contains("Both teams", r);
            var f = Lobby(MatchMode.ThreeVThree, 5);
            Assert.True(f.CanStart(out _));
            f.Start(0); f.Tick(3);
            f.Kill(2, 1, 4); f.Kill(4, 1, 5);       // team 1 = {2,4} wiped
            Assert.Equal(MatchPhase.RoundEnd, f.State.Phase);
            Assert.Equal(1, f.State.TeamScore[0]);
        }

        [Fact]
        public void KillReportsWhetherItWasAccepted()
        {
            var m = Live(MatchMode.FreeForAll, 2);
            Assert.True(m.Kill(1, 2, 5));
            Assert.False(m.Kill(1, 2, 5.5));      // already dead
            Assert.False(m.Kill(9, 2, 5.5));      // unknown victim
        }

        [Fact]
        public void SetModeOnlyInLobbyAndRebalances()
        {
            var m = Lobby(MatchMode.OneVOne, 2);
            m.MoveTeam(2);                       // both on team 0
            m.SetMode(MatchMode.TwoVTwo);        // rebalance
            Assert.Equal(1, m.State.TeamCount(0));
            Assert.Equal(1, m.State.TeamCount(1));
            m.SetMode(MatchMode.OneVOne);
            m.Start(0);
            Assert.Equal(MatchPhase.Countdown, m.State.Phase);
            m.SetMode(MatchMode.FreeForAll);     // locked once started
            Assert.Equal(MatchMode.OneVOne, m.State.Mode);
        }

        [Fact]
        public void SoloDebugAllowsOnePlayer()
        {
            var m = Lobby(MatchMode.OneVOne, 1, new MatchRules { SoloDebug = true });
            Assert.True(m.CanStart(out _));
        }

        [Fact]
        public void LoadoutTruncatedToWholeGunsAndRankPointsStored()
        {
            var m = new MatchMachine(new MatchRules { MaxLoadoutGuns = 2 });
            m.Open();
            m.PlayerJoined(1, "A");
            var three = LoadoutCodec.Encode(new[] { new LoadoutGun(1), new LoadoutGun(2) { Sight = 1, Laser = true }, new LoadoutGun(3) });
            m.SetLoadout(1, three, false, 250);
            var kept = LoadoutCodec.Decode(m.State.Slot(1).Loadout);
            Assert.Equal(2, kept.Count);
            Assert.Equal(2, kept[1].ItemId);
            Assert.Equal(1, kept[1].Sight);
            Assert.True(kept[1].Laser);
            Assert.Equal(250, m.State.Slot(1).RankPoints);
        }

        [Fact]
        public void LoadoutCodecRoundTripsAndIgnoresPartialBytes()
        {
            var guns = new[] { new LoadoutGun(54) { Sight = 2, Barrel = 1, Bullets = 3, ExtendedMag = true, Laser = false, Skin = 7 } };
            var bytes = LoadoutCodec.Encode(guns);
            Assert.Equal(6, bytes.Length);
            var back = LoadoutCodec.Decode(bytes);
            Assert.Single(back);
            Assert.Equal((54, 2, 1, 3, true, false, 7), (back[0].ItemId, (int)back[0].Sight, (int)back[0].Barrel, (int)back[0].Bullets, back[0].ExtendedMag, back[0].Laser, (int)back[0].Skin));
            Assert.Equal(4, back[0].ModCount);
            Assert.Empty(LoadoutCodec.Decode(new byte[] { 1, 2, 3 }));
            Assert.Empty(LoadoutCodec.Encode(null));
        }

        // ------------------------------------------------------------ 1v1 / team flow

        [Fact]
        public void StartBuildsArenaResetsAndCountsDown()
        {
            var m = Lobby(MatchMode.OneVOne, 2);
            m.Start(10);
            Assert.Equal(MatchPhase.Countdown, m.State.Phase);
            Assert.Equal(1, m.State.Round);
            Assert.Equal(1, m.State.MatchNumber);
            Assert.Equal(new[] { EffectKind.BuildArena, EffectKind.ResetPlayers }, Kinds(m));
            Assert.Equal(13, m.State.PhaseEndsAt);
            Assert.Equal(Side.Left, m.State.SideFor(1));
            Assert.Equal(Side.Right, m.State.SideFor(2));
        }

        [Fact]
        public void CountdownGoesLive_KillEndsRoundForOtherTeam()
        {
            var m = Live(MatchMode.OneVOne, 2);
            m.Kill(1, 2, 5.0);
            Assert.Equal(MatchPhase.RoundEnd, m.State.Phase);
            Assert.Equal(1, m.State.TeamScore[1]);
            Assert.Equal(0, m.State.TeamScore[0]);
            Assert.Equal(1, m.State.LastRoundWinnerTeam);
            Assert.Equal(7.0, m.State.PhaseEndsAt);
            Assert.Equal("P2 wins the round", m.State.StatusText);
        }

        [Fact]
        public void KillDuringCountdownEndsRound_KillDuringRoundEndIgnored()
        {
            var m = Lobby(MatchMode.OneVOne, 2);
            m.Start(0);
            m.Kill(1, 2, 1.0);
            Assert.Equal(MatchPhase.RoundEnd, m.State.Phase);
            m.Kill(2, 1, 1.5);
            Assert.Equal(1, m.State.TeamScore[1]);
            Assert.Equal(0, m.State.TeamScore[0]);
        }

        [Fact]
        public void TeamRoundEndsOnlyWhenWholeTeamIsDead()
        {
            var m = Live(MatchMode.TwoVTwo, 4);   // teams: {1,3} vs {2,4}
            m.Kill(1, 2, 5.0);
            Assert.Equal(MatchPhase.Live, m.State.Phase);
            m.Kill(3, 4, 5.5);
            Assert.Equal(MatchPhase.RoundEnd, m.State.Phase);
            Assert.Equal(1, m.State.TeamScore[1]);
            Assert.Equal("Team B wins the round", m.State.StatusText);
        }

        [Fact]
        public void TeamModesCountKillsAndDeathsButNotTeamKills()
        {
            var m = Live(MatchMode.TwoVTwo, 4);   // teams: {1,3} vs {2,4}
            m.Kill(1, 2, 5.0);
            Assert.Equal(1, m.State.Slot(2).Kills);
            Assert.Equal(1, m.State.Slot(1).Deaths);
            m.Kill(3, 1, 5.5);                   // team kill: death counted, no credit
            Assert.Equal(0, m.State.Slot(1).Kills);
            Assert.Equal(1, m.State.Slot(3).Deaths);
            Assert.Equal(MatchPhase.RoundEnd, m.State.Phase);
        }

        [Fact]
        public void TeammatesGetSpacedPadSlots()
        {
            var m = Live(MatchMode.ThreeVThree, 6);   // team 0: 1,3,5
            var (i1, c1) = m.State.TeamSlot(1);
            var (i5, c5) = m.State.TeamSlot(5);
            Assert.Equal((0, 3), (i1, c1));
            Assert.Equal((2, 3), (i5, c5));
            var l = ArenaLayout.Create(0);
            var s0 = l.TeamSpawn(Side.Left, 0, 3);
            var s2 = l.TeamSpawn(Side.Left, 2, 3);
            Assert.Equal(l.Left.X, s0.X);
            Assert.Equal(4f, s2.Z - s0.Z);
        }

        [Fact]
        public void RoundEndSwapsSidesAndResetsDead()
        {
            var m = Live(MatchMode.OneVOne, 2);
            m.Kill(1, 2, 5.0);
            m.Effects.Clear();
            m.Tick(7.0);
            Assert.Equal(MatchPhase.Countdown, m.State.Phase);
            Assert.Equal(2, m.State.Round);
            Assert.False(m.State.TeamAIsLeft);
            Assert.Equal(Side.Right, m.State.SideFor(1));
            Assert.Equal(new[] { EffectKind.ResetPlayers }, Kinds(m));
            Assert.False(m.State.Slot(1).DeadThisRound);
        }

        [Fact]
        public void ReachingRoundsToWinEndsMatchThenLobby()
        {
            var m = Live(MatchMode.OneVOne, 2, new MatchRules { RoundsToWin = 2 });
            double t = 5;
            m.Kill(1, 2, t); t += 2; m.Tick(t);
            t += 3; m.Tick(t);
            m.Kill(1, 2, t);                      // deciding kill: straight to MatchEnd, no RoundEnd pause
            Assert.Equal(MatchPhase.MatchEnd, m.State.Phase);
            Assert.Equal(1, m.State.MatchWinnerTeam);
            Assert.Equal(t + 5, m.State.PhaseEndsAt);
            t += 2; m.Tick(t);
            Assert.Equal(MatchPhase.MatchEnd, m.State.Phase);
            t -= 2;
            m.Tick(t + 5);
            Assert.Equal(MatchPhase.Lobby, m.State.Phase);
            Assert.Equal(0, m.State.TeamScore[1]);
            Assert.All(m.State.Players, p => Assert.False(p.Ready));
            Assert.True(m.State.ArenaBuilt);
            Assert.Equal(11, LoadoutCodec.Decode(m.State.Slot(1).Loadout)[0].ItemId);
        }

        [Fact]
        public void RematchKeepsArena_ChangingMapRebuilds()
        {
            var m = Live(MatchMode.OneVOne, 2, new MatchRules { RoundsToWin = 1 });
            m.Kill(2, 1, 5); m.Tick(7); m.Tick(12);
            Assert.Equal(MatchPhase.Lobby, m.State.Phase);
            foreach (var p in m.State.Players) m.SetLoadout(p.Id, p.Loadout, true);
            m.Effects.Clear();
            m.Start(20);
            Assert.Equal(new[] { EffectKind.ResetPlayers }, Kinds(m));
            Assert.Equal(2, m.State.MatchNumber);
            m.Kill(2, 1, 25); m.Tick(27); m.Tick(32);
            m.SetMap(2);
            foreach (var p in m.State.Players) m.SetLoadout(p.Id, p.Loadout, true);
            m.Effects.Clear();
            m.Start(40);
            Assert.Equal(new[] { EffectKind.BuildArena, EffectKind.ResetPlayers }, Kinds(m));
            Assert.Equal(2, m.State.BuiltMapIndex);
        }

        [Fact]
        public void PlayerLeavingMidTeamMatchReturnsToLobby()
        {
            var m = Live(MatchMode.OneVOne, 2);
            m.PlayerLeft(2);
            Assert.Equal(MatchPhase.Lobby, m.State.Phase);
            Assert.Equal(1, m.State.PresentCount);
            Assert.Contains("left", m.State.StatusText);
        }

        [Fact]
        public void QuitDestroysArenaAndClearsPlayers()
        {
            var m = Live(MatchMode.OneVOne, 2);
            m.Effects.Clear();
            m.Quit();
            Assert.Equal(MatchPhase.Inactive, m.State.Phase);
            Assert.Equal(new[] { EffectKind.DestroyArena }, Kinds(m));
            Assert.Empty(m.State.Players);
            var l = new MatchMachine(new MatchRules());
            l.Open(); l.Quit();
            Assert.Empty(l.Effects);
        }

        // ------------------------------------------------------------ free-for-all

        [Fact]
        public void FfaKillAddsKillAndSchedulesRespawn()
        {
            var m = Live(MatchMode.FreeForAll, 3);
            m.Kill(1, 2, 5.0);
            Assert.Equal(MatchPhase.Live, m.State.Phase);
            Assert.Equal(1, m.State.Slot(2).Kills);
            Assert.True(m.State.Slot(1).DeadThisRound);
            Assert.Single(m.Effects);
            Assert.Equal(EffectKind.RespawnPlayer, m.Effects[0].Kind);
            Assert.Equal(1, m.Effects[0].PlayerId);
            m.Kill(1, 3, 5.5);                    // already dead, ignored
            Assert.Equal(0, m.State.Slot(3).Kills);
            m.PlayerRespawned(1);
            Assert.False(m.State.Slot(1).DeadThisRound);
            m.Kill(1, 3, 6.0);
            Assert.Equal(1, m.State.Slot(3).Kills);
        }

        [Fact]
        public void FfaSuicideCountsNothingButStillRespawns()
        {
            var m = Live(MatchMode.FreeForAll, 2);
            m.Kill(1, 1, 5.0);
            Assert.Equal(0, m.State.Slot(1).Kills);
            Assert.Equal(EffectKind.RespawnPlayer, m.Effects[0].Kind);
            m.Effects.Clear();
            m.PlayerRespawned(1);
            m.Kill(1, -1, 6.0);
            Assert.Equal(EffectKind.RespawnPlayer, m.Effects[0].Kind);
        }

        [Fact]
        public void FfaReachingKillsToWinEndsMatch()
        {
            var m = Live(MatchMode.FreeForAll, 2, new MatchRules { KillsToWin = 2 });
            m.Kill(1, 2, 5.0); m.PlayerRespawned(1);
            m.Kill(1, 2, 9.0);
            Assert.Equal(MatchPhase.MatchEnd, m.State.Phase);
            Assert.Equal(2, m.State.MatchWinnerId);
            Assert.Equal(-1, m.State.MatchWinnerTeam);
            Assert.Equal(14.0, m.State.PhaseEndsAt);
            m.Tick(14.0);
            Assert.Equal(MatchPhase.Lobby, m.State.Phase);
            Assert.Equal(0, m.State.Slot(2).Kills);
        }

        [Fact]
        public void FfaContinuesWhenSomeoneLeavesWithTwoRemaining()
        {
            var m = Live(MatchMode.FreeForAll, 3);
            m.PlayerLeft(3);
            Assert.Equal(MatchPhase.Live, m.State.Phase);
            m.PlayerLeft(2);
            Assert.Equal(MatchPhase.Lobby, m.State.Phase);
        }

        [Fact]
        public void TeamLabelUsesNameInOneVOne()
        {
            var m = Lobby(MatchMode.OneVOne, 2);
            Assert.Equal("P1", m.TeamLabel(0));
            var t = Lobby(MatchMode.TwoVTwo, 4);
            Assert.Equal("Team A", t.TeamLabel(0));
            Assert.Equal("Team B", t.TeamLabel(1));
        }
    
        [Fact]
        public void DrumAndSwitchRoundTrip()
        {
            var g = new LoadoutGun(66) { Drum = true, Switch = true, Laser = true, Skin = 9 };
            var back = LoadoutCodec.Decode(LoadoutCodec.Encode(new[] { g }))[0];
            Assert.True(back.Drum); Assert.True(back.Switch); Assert.True(back.Laser); Assert.False(back.ExtendedMag);
            Assert.Equal(9, back.Skin);
            Assert.Equal(3, back.ModCount);
        }

        [Fact]
        public void GunBalanceIsAuthoritative()
        {
            Assert.Equal(100, GunBalance.DamageFor("Sniper Rifle"));          // one shot
            Assert.True(GunBalance.DamageFor("Smg") * 6 < GunBalance.Health);  // seven to kill
            Assert.Equal(24, GunBalance.Authoritative("Assault Rifle", 999, 10f));   // damage hack clamped
            Assert.Equal(24, GunBalance.Authoritative("Assault Rifle", 3, 10f));     // low value raised too
            Assert.Equal(18, GunBalance.Authoritative("Assault Rifle", 18, 10f));    // ricochet (75%) kept
            Assert.Equal(150, GunBalance.Authoritative("Assault Rifle", 150, 2f));   // knife in reach kept
            Assert.Equal(24, GunBalance.Authoritative("Assault Rifle", 150, 12f));   // "knife" from 12 m is not a knife
            Assert.Equal(75, GunBalance.RicochetDamageFor("Sniper Rifle"));
        }
}
}
