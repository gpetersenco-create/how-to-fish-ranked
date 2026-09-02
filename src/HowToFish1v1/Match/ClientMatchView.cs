using System;
using System.Collections;
using System.Linq;
using FishNet;
using HowToFish1v1.Arena;
using HowToFish1v1.Core;
using HowToFish1v1.Net;
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

            if (phase == MatchPhase.Live && _prevPhase != MatchPhase.Live) LoadoutService.RefillLocalAmmo();
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
            bool ffa = MatchModes.IsFfa((MatchMode)s.Mode);
            bool won = ffa ? s.MatchWinnerId == me.Value.Id : s.MatchWinnerTeam == me.Value.Team;
            string map = ArenaLayout.MapNames[((s.MapIndex % ArenaLayout.MapCount) + ArenaLayout.MapCount) % ArenaLayout.MapCount];
            RankService.ApplyResult(won, ffa, me.Value.Kills, me.Value.Deaths, MatchModes.Name((MatchMode)s.Mode), map);
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
            ModState.Reset();
            ArenaBuilder.Destroy();
        }
    }
}
