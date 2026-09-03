using System;
using System.Collections;
using System.Linq;
using FishNet;
using HowToFish1v1.Arena;
using HowToFish1v1.Core;
using HowToFish1v1.Net;
using HowToFish1v1.Net.Proto2;
using HowToFish1v1.UI;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>Runs on every peer (host included). Applies host broadcasts to ModState, the arena, ranks, and local ammo.</summary>
    public static class ClientMatchView
    {
        public static MatchStateBroadcast Latest;
        public static bool HasState;

        private static MonoBehaviour _runner;
        private static MatchPhase _prevPhase = MatchPhase.Inactive;
        private static int _lastRankedMatch = -1;

        public static void Init(MonoBehaviour runner)
        {
            _runner = runner;
            ModNet.StateReceived += OnState;
            ModNet.ArenaReceived += OnArena;
            ModNet.ClientStopped += OnStopped;
            // Our own aim state is recorded the moment it changes (below); the relayed copy would arrive a round trip late.
            ModNet.AimStateReceived += a => { if (a.OwnerId != ModState.LocalOwnerId) Recorder.RecordAim(a.OwnerId, a.Ads); };
            ModNet.KnifeStateReceived += k => { if (k.OwnerId != ModState.LocalOwnerId) Recorder.RecordKnife(k.OwnerId, k.Skin); };
            ModNet.BounceStateReceived += b => { if (b.OwnerId != ModState.LocalOwnerId) Ricochet.OnRemoteBounce(b.OwnerId, b.From, b.To); };
            ModNet.CheatReceived += c =>
            {
                bool me = c.OwnerId == ModState.LocalOwnerId;
                Hud.Announce(me ? AntiCheat.Message : $"{c.Name}\n<size=60%>{AntiCheat.Message}</size>", me ? 8f : 6f, me);
                Plugin.Log.LogWarning($"Anti-cheat: {c.Name} was caught ({c.Reason})");
            };
        }

        private static bool _lastAds;

        /// <summary>Call every frame: keeps re-sending our hello until the host lists us with the mod, and reports aim changes.</summary>
        public static void Update()
        {
            var lp = Player.LocalPlayer;
            if (!lp) return;
            bool known = HasState && Me is PlayerEntry me && me.HasMod;
            ModNet.KeepHelloAlive(known);
            if (ModState.IsActive)
            {
                bool ads = lp.Holding && lp.Holding.HeldItem is Weapon w && w.IsAds;
                if (ads != _lastAds) { _lastAds = ads; Recorder.RecordAim(lp.OwnerId, ads); ModNet.SendAim(ads); }
            }
        }

        public static PlayerEntry[] Players => HasState && Latest.Players != null ? Latest.Players : Array.Empty<PlayerEntry>();
        public static MatchMode Mode => HasState ? (MatchMode)Latest.Mode : MatchMode.OneVOne;
        public static bool IsFfa => MatchModes.IsFfa(Mode);

        public static PlayerEntry? Me
        {
            get
            {
                int me = ModState.LocalOwnerId;
                foreach (var p in Players) if (p.Id == me) return p;
                return null;
            }
        }

        public static int MyTeam => Me?.Team ?? -1;

        public static double SecondsLeftInPhase
        {
            get
            {
                if (!HasState) return 0;
                var tm = InstanceFinder.TimeManager;
                if (tm == null) return 0;
                long dt = (long)Latest.PhaseEndsAtTick - tm.Tick;
                return dt <= 0 ? 0 : dt * tm.TickDelta;
            }
        }

        /// <summary>"Gavin" in 1v1, "Team A"/"Team B" otherwise.</summary>
        public static string TeamLabel(int team)
        {
            var members = Players.Where(p => p.Team == team).ToList();
            if (members.Count == 1) return members[0].Name;
            return team == 0 ? "Team A" : "Team B";
        }

        private static (Side side, int index, int count)? SpawnSlotOf(int ownerId)
        {
            if (!HasState || IsFfa) return null;
            var players = Players;
            int idx = Array.FindIndex(players, p => p.Id == ownerId);
            if (idx < 0) return null;
            int team = players[idx].Team;
            var members = players.Where(p => p.Team == team).ToList();
            int index = members.FindIndex(p => p.Id == ownerId);
            Side side = ((team == 0) == Latest.TeamAIsLeft) ? Side.Left : Side.Right;
            return (side, index, members.Count);
        }

        private static void OnState(MatchStateBroadcast s)
        {
            Latest = s;
            HasState = true;
            var phase = (MatchPhase)s.Phase;
            if (phase != _prevPhase)
                Plugin.Log.LogInfo($"Phase {_prevPhase} -> {phase} mode {MatchModes.Name((MatchMode)s.Mode)} round {s.Round} score {s.TeamScoreA}-{s.TeamScoreB} status='{s.StatusText}'");
            ModState.Phase = phase;
            ModState.SpawnSlotLookup = SpawnSlotOf;
            // Joiners arrive through a plain Steam invite, not our menu: the host running the mode makes this a ranked session for them too.
            if (phase != MatchPhase.Inactive) ModState.RankedSession = true;

            if (phase == MatchPhase.Live && _prevPhase != MatchPhase.Live) LoadoutService.RefillLocalAmmo();
            if (phase == MatchPhase.MatchEnd && _prevPhase != MatchPhase.MatchEnd) KillCam.StartFinal();
            if (phase != MatchPhase.MatchEnd && _prevPhase == MatchPhase.MatchEnd) KillCam.OnMatchLeftEndPhase();
            if ((phase == MatchPhase.Inactive || phase == MatchPhase.Lobby) && phase != _prevPhase) KillCam.Stop();
            if (phase == MatchPhase.MatchEnd && s.MatchNumber != _lastRankedMatch) ApplyRank(s);
            // Ranked sessions: the lobby screen shows itself whenever the match is in the lobby and hides when a match starts.
            if (ModState.RankedSession)
            {
                if (phase == MatchPhase.Lobby && _prevPhase != MatchPhase.Lobby && !ModState.PanelOpen) LobbyPanel.Open();
                if (phase == MatchPhase.Countdown && _prevPhase == MatchPhase.Lobby && ModState.PanelOpen) LobbyPanel.Close();
            }
            if (phase == MatchPhase.Inactive) { ModState.PanelOpen = false; PlayerCamera.ToggleMouse(false); }

            // The host may not have our hello if we connected before it registered handlers; resend when it says we lack the mod.
            var me = Me;
            if (me != null && !me.Value.HasMod) ModNet.SendHello();
            _prevPhase = phase;
        }

        private static void ApplyRank(MatchStateBroadcast s)
        {
            _lastRankedMatch = s.MatchNumber;
            var me = Me;
            if (me == null) return;
            if (MatchModes.IsSolo((MatchMode)s.Mode)) return;   // practice: no rank change
            bool ffa = MatchModes.IsFfa((MatchMode)s.Mode);
            bool won = ffa ? s.MatchWinnerId == me.Value.Id : s.MatchWinnerTeam == me.Value.Team;
            string map = ArenaLayout.MapNames[((s.MapIndex % ArenaLayout.MapCount) + ArenaLayout.MapCount) % ArenaLayout.MapCount];
            RankService.ApplyResult(won, ffa, me.Value.Kills, me.Value.Deaths, MatchModes.Name((MatchMode)s.Mode), map);
            _runner.StartCoroutine(CloudRanks.Report());
            // Tell the host our new points so the panel shows the fresh rank next round.
            LobbyPanel.ResendLoadout();
        }

        private static void OnArena(ArenaBroadcast a)
        {
            if (a.Build) _runner.StartCoroutine(BuildRoutine(a.MapIndex));
            else _runner.StartCoroutine(ReturnRoutine(a.ReturnIsland));
        }

        /// <summary>Build first, unload the island afterwards: the arena is far from any island, and players must never stand on nothing.</summary>
        private static IEnumerator BuildRoutine(int mapIndex)
        {
            float deadline = Time.unscaledTime + 15f;
            yield return new WaitUntil(() => !IslandManager.IsLoading || Time.unscaledTime > deadline);
            ArenaBuilder.Destroy();
            ArenaBuilder.Build(mapIndex);
            yield return new WaitForSeconds(0.75f);
            IslandManager.UnloadIslands();
            yield return new WaitForSeconds(0.5f);
            PlayerUI.ToggleIslandWarning(false);
        }

        /// <summary>Load the island first and only then remove the arena, so players stand on something until the game teleports them.</summary>
        private static IEnumerator ReturnRoutine(byte returnIsland)
        {
            ModState.ForceInstantTeleportUntil = Time.unscaledTime + 30f;
            IslandManager.LoadIsland(returnIsland);
            yield return new WaitForSeconds(0.5f);
            float deadline = Time.unscaledTime + 20f;
            yield return new WaitUntil(() => !IslandManager.IsLoading || Time.unscaledTime > deadline);
            yield return new WaitForSeconds(1.5f);
            ArenaBuilder.Destroy();
            ModState.ForceInstantTeleportUntil = Time.unscaledTime + 5f;
        }

        private static void OnStopped()
        {
            HasState = false;
            _prevPhase = MatchPhase.Inactive;
            KillCam.Stop();
            ModState.Reset();
            ArenaBuilder.Destroy();
        }
    }
}
