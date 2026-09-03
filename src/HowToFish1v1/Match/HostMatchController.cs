using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FishNet;
using FishNet.Connection;
using HowToFish1v1.Arena;
using HowToFish1v1.Core;
using HowToFish1v1.Net;
using HowToFish1v1.Net.Proto2;
using HowToFish1v1.Patches;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>Runs on the host only. Feeds the pure MatchMachine with real events and applies its effects to the game.</summary>
    public sealed class HostMatchController
    {
        public MatchMachine Machine { get; private set; }
        public bool IsOpen => Machine != null && Machine.State.Phase != MatchPhase.Inactive;

        private readonly MonoBehaviour _runner;
        private readonly Dictionary<int, string> _helloVersions = new Dictionary<int, string>();
        private byte _returnIsland;
        private bool _resetRunning;

        public HostMatchController(MonoBehaviour runner)
        {
            _runner = runner;
            ModNet.HelloReceived += OnHello;
            ModNet.LoadoutReceived += OnLoadout;
            ModNet.RemoteDisconnected += id => _helloVersions.Remove(id);
            ModState.KillDetected += OnKill;
        }

        /// <summary>Applies remembered hellos to slots whose mod flag is out of date (players can join after their hello arrived).</summary>
        private void ApplyKnownVersions()
        {
            foreach (var kv in _helloVersions)
            {
                var slot = Machine.State.Slot(kv.Key);
                bool ok = kv.Value == Plugin.Version;
                if (slot != null && slot.HasMod != ok) Machine.PlayerSaidHello(kv.Key, ok);
            }
        }

        private MatchRules RulesFromConfig() => new MatchRules
        {
            RoundsToWin = Mathf.Max(1, Plugin.Cfg.RoundsToWin.Value),
            KillsToWin = Mathf.Max(1, Plugin.Cfg.KillsToWin.Value),
            CountdownSeconds = Mathf.Max(0f, Plugin.Cfg.CountdownSeconds.Value),
            FfaRespawnSeconds = Mathf.Max(0f, Plugin.Cfg.FfaRespawnSeconds.Value),
            MaxLoadoutGuns = Mathf.Max(0, Plugin.Cfg.MaxLoadoutGuns.Value),
            SoloDebug = Plugin.Cfg.SoloDebug.Value
        };

        private static double Now => InstanceFinder.TimeManager.TicksToTime(InstanceFinder.TimeManager.Tick);

        public void Open()
        {
            if (!ModNet.IsHost) return;
            if (Machine == null || Machine.State.Phase == MatchPhase.Inactive) Machine = new MatchMachine(RulesFromConfig());
            Machine.Open();
            foreach (var p in PlayerManager.Players) Machine.PlayerJoined(p.OwnerId, p.SteamName);
            // The host's own client always has the mod.
            if (Player.LocalPlayer) _helloVersions[Player.LocalPlayer.OwnerId] = Plugin.Version;
            ApplyKnownVersions();
            Flush();
        }

        public void Start()
        {
            if (Machine == null) return;
            if (!Machine.CanStart(out string why))
                Plugin.Log.LogInfo($"Start refused: {why} (players={Machine.State.PresentCount}, mode={MatchModes.Name(Machine.State.Mode)}, solo={Machine.Rules.SoloDebug})");
            Machine.Start(Now);
            Flush();
        }
        public void Quit() { if (Machine != null) { Machine.Quit(); Flush(); } }
        public void SetMap(int mapIndex) { if (Machine != null) { Machine.SetMap(mapIndex); Flush(); } }
        public void SetMode(MatchMode mode) { if (Machine != null) { Machine.SetMode(mode); Flush(); } }
        public void MoveTeam(int ownerId) { if (Machine != null) { Machine.MoveTeam(ownerId); Flush(); } }

        public void SetLocalLoadout(byte[] ids, bool ready, int rankPoints)
        {
            if (Machine == null || Player.LocalPlayer == null) return;
            Machine.SetLoadout(Player.LocalPlayer.OwnerId, ids, ready, rankPoints);
            Flush();
        }

        public void Update()
        {
            if (!IsOpen) return;
            if (!ModNet.IsHost) { Machine.Quit(); Flush(); return; }

            // Joins / leaves
            var present = new HashSet<int>(PlayerManager.Players.Select(p => p.OwnerId));
            foreach (var p in PlayerManager.Players) Machine.PlayerJoined(p.OwnerId, p.SteamName);
            foreach (var slot in Machine.State.Players.ToList())
                if (!present.Contains(slot.Id)) Machine.PlayerLeft(slot.Id);
            ApplyKnownVersions();

            Machine.Tick(Now);
            Flush();
        }

        private void OnHello(NetworkConnection conn, HelloBroadcast msg)
        {
            _helloVersions[conn.ClientId] = msg.ModVersion ?? "";
            if (Machine != null) { ApplyKnownVersions(); Flush(); }
        }

        private void OnLoadout(NetworkConnection conn, LoadoutBroadcast msg)
        {
            // A loadout message is proof of the mod too.
            if (!string.IsNullOrEmpty(msg.ModVersion)) _helloVersions[conn.ClientId] = msg.ModVersion;
            if (Machine == null) return;
            ApplyKnownVersions();
            Machine.SetLoadout(conn.ClientId, msg.ItemIds, msg.Ready, msg.RankPoints);
            Flush();
        }

        private void OnKill(Player victim)
        {
            if (Machine == null || !ModNet.IsHost) return;
            int killer = KillAttribution.Take(victim.OwnerId);
            Machine.Kill(victim.OwnerId, killer, Now);
            Flush();
        }

        private void Flush()
        {
            foreach (var e in Machine.Effects)
            {
                switch (e.Kind)
                {
                    case EffectKind.BuildArena:
                        if (!ArenaBuilder.IsBuilt) _returnIsland = OnlineIslandManager.CurIsland;
                        ModNet.BroadcastArena(new ArenaBroadcast { Build = true, ReturnIsland = _returnIsland, MapIndex = (byte)Machine.State.BuiltMapIndex });
                        break;
                    case EffectKind.DestroyArena:
                        foreach (var p in PlayerManager.Players) ReviveInPlace(p);
                        OnlineIslandManager.ToggleTeleportPlayers(true);
                        ModNet.BroadcastArena(new ArenaBroadcast { Build = false, ReturnIsland = _returnIsland });
                        break;
                    case EffectKind.ResetPlayers:
                        KillAttribution.Clear();
                        _runner.StartCoroutine(ResetPlayersRoutine());
                        break;
                    case EffectKind.RespawnPlayer:
                        _runner.StartCoroutine(FfaRespawnRoutine(e.PlayerId));
                        break;
                }
            }
            Machine.Effects.Clear();
            if (Machine.Dirty)
            {
                Machine.Dirty = false;
                ModNet.BroadcastState(ToBroadcast(Machine.State, Machine.Rules));
            }
        }

        private IEnumerator ResetPlayersRoutine()
        {
            if (_resetRunning) yield break;
            _resetRunning = true;
            float deadline = Time.unscaledTime + 15f;
            yield return new WaitUntil(() => ArenaBuilder.IsBuilt || Time.unscaledTime > deadline);
            if (!ArenaBuilder.IsBuilt) { Plugin.Log.LogError("Arena never built; aborting reset"); _resetRunning = false; yield break; }

            var state = Machine.State;
            var ffaSpawns = ArenaBuilder.FfaSpawns();
            var players = new List<(Player player, Vector3 pos, float yaw, byte[] loadout)>();
            for (int i = 0; i < state.Players.Count; i++)
            {
                var slot = state.Players[i];
                var player = PlayerManager.Players.FirstOrDefault(p => p.OwnerId == slot.Id);
                if (!player) continue;
                (Vector3 pos, float yaw) spawn;
                if (state.IsFfa)
                {
                    spawn = ffaSpawns[i % ffaSpawns.Count];
                }
                else
                {
                    var (index, count) = state.TeamSlot(slot.Id);
                    spawn = ArenaBuilder.Spawn(state.SideFor(slot.Id), index, count);
                }
                players.Add((player, spawn.pos, spawn.yaw, slot.Loadout));
            }

            // Move everyone to their pads immediately so nobody is standing on an island that is about to unload.
            foreach (var p in players)
            {
                LoadoutService.ServerClearItems(p.player);
                ReviveInPlace(p.player);
                Server.Instance.TeleportPlayer(p.player, p.pos, p.yaw);
            }
            yield return new WaitForSeconds(0.3f);
            foreach (var p in players)
            {
                Server.Instance.TeleportPlayer(p.player, p.pos, p.yaw);
                LoadoutService.ServerGive(p.player, p.loadout, p.pos);
            }
            // One more nudge after the island unload has settled, in case a client fell during the swap.
            yield return new WaitForSeconds(1.2f);
            foreach (var p in players)
                if (p.player) Server.Instance.TeleportPlayer(p.player, p.pos, p.yaw);
            _resetRunning = false;
        }

        /// <summary>Free-for-all: after the delay, revive at the spawn farthest from everyone else and hand out the loadout again.</summary>
        private IEnumerator FfaRespawnRoutine(int ownerId)
        {
            yield return new WaitForSeconds((float)Machine.Rules.FfaRespawnSeconds);
            if (!IsOpen || !Machine.State.IsFfa || Machine.State.Phase != MatchPhase.Live) yield break;
            var slot = Machine.State.Slot(ownerId);
            var player = PlayerManager.Players.FirstOrDefault(p => p.OwnerId == ownerId);
            if (slot == null || !player) yield break;

            var others = PlayerManager.Players.Where(p => p && p.OwnerId != ownerId && !p.Dying.IsDead).Select(p => p.Transform.position).ToList();
            var best = ArenaBuilder.FfaSpawns()
                .OrderByDescending(s => others.Count == 0 ? 0f : others.Min(o => Vector3.Distance(o, s.pos)))
                .First();

            LoadoutService.ServerClearItems(player);
            ReviveInPlace(player);
            Server.Instance.TeleportPlayer(player, best.pos, best.yaw);
            yield return new WaitForSeconds(0.3f);
            if (!player) yield break;
            Server.Instance.TeleportPlayer(player, best.pos, best.yaw);
            LoadoutService.ServerGive(player, slot.Loadout, best.pos);
            Machine.PlayerRespawned(ownerId);
            Flush();
        }

        /// <summary>Server only. Full health and fullness, no poison or fire, ragdoll removed. The game's own reset leaves fire burning.</summary>
        private static void ReviveInPlace(Player player)
        {
            if (!player) return;
            var ragdoll = player.Dying.DeadPlayer;
            if (ragdoll) ragdoll.DestroyItem(7);
            // ServerResetVitals restores the prefab's serialized starting values (50 hp / 25 fullness), not full bars.
            player.Vitals.ServerResetVitals();
            player.Vitals._syncedHealth.Value = 100;
            player.Vitals._syncedFullness.Value = 100;
            player.Vitals._syncedFire.Value = 0;
            player.Vitals._syncedPoison.Value = 0;
        }

        private static MatchStateBroadcast ToBroadcast(MatchState s, MatchRules rules)
        {
            var tm = InstanceFinder.TimeManager;
            uint endTick = tm.TickDelta > 0 ? (uint)System.Math.Max(0, System.Math.Round(s.PhaseEndsAt / tm.TickDelta)) : 0u;
            var entries = s.Players.Select(p => new PlayerEntry
            {
                Id = p.Id, Name = p.Name ?? "", Team = (byte)p.Team, Kills = p.Kills, Deaths = p.Deaths, Ready = p.Ready, HasMod = p.HasMod,
                RankPoints = p.RankPoints, Loadout = p.Loadout
            }).ToArray();
            return new MatchStateBroadcast
            {
                Phase = (byte)s.Phase, Mode = (byte)s.Mode, Round = s.Round, MatchNumber = s.MatchNumber,
                TeamScoreA = s.TeamScore[0], TeamScoreB = s.TeamScore[1], TeamAIsLeft = s.TeamAIsLeft,
                PhaseEndsAtTick = endTick, LastRoundWinnerTeam = s.LastRoundWinnerTeam,
                MatchWinnerTeam = s.MatchWinnerTeam, MatchWinnerId = s.MatchWinnerId, StatusText = s.StatusText ?? "",
                MapIndex = (byte)s.MapIndex, KillsToWin = rules.KillsToWin, RoundsToWin = rules.RoundsToWin, Players = entries
            };
        }
    }
}
