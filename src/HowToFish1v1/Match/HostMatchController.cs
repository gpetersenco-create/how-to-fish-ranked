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
        // Search and Destroy: who is holding the plant/defuse key, and how far along they are (seconds).
        private readonly Dictionary<int, bool> _bombHolding = new Dictionary<int, bool>();
        private readonly Dictionary<int, double> _bombProgress = new Dictionary<int, double>();
        private double _lastBombTick = -1;
        private float _nextProgressBroadcast;
        private byte _returnIsland;
        private bool _resetRunning;

        public HostMatchController(MonoBehaviour runner)
        {
            _runner = runner;
            ModNet.HelloReceived += OnHello;
            ModNet.LoadoutReceived += OnLoadout;
            ModNet.RemoteDisconnected += id => _helloVersions.Remove(id);
            ModNet.AimReceived += (conn, msg) => { if (IsOpen) ModNet.BroadcastAimState(conn.ClientId, msg.Ads); };
            ModNet.KnifeReceived += (conn, msg) => { if (IsOpen) ModNet.BroadcastKnifeState(conn.ClientId, msg.Skin); };
            ModNet.BounceReceived += (conn, msg) => { if (IsOpen) ModNet.BroadcastBounce(conn.ClientId, msg.From, msg.To); };
            ModNet.GrenadeReceived += (conn, msg) => { if (IsOpen) { ModNet.BroadcastGrenade(conn.ClientId, msg); Grenades.OnRemote(conn.ClientId, msg.Kind, msg.Pos, msg.Vel, msg.Fuse); } };
            ModNet.BombReceived += (conn, msg) => { _bombHolding[conn.ClientId] = msg.Holding; if (!msg.Holding) _bombProgress.Remove(conn.ClientId); };
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
            RoundEndSeconds = Mathf.Max(0f, Plugin.Cfg.RoundEndSeconds.Value),
            MatchEndSeconds = Mathf.Max(3f, Plugin.Cfg.MatchEndSeconds.Value),
            RoundSeconds = Mathf.Max(20f, Plugin.Cfg.RoundSeconds.Value),
            PlantSeconds = Mathf.Max(1f, Plugin.Cfg.PlantSeconds.Value),
            DefuseSeconds = Mathf.Max(1f, Plugin.Cfg.DefuseSeconds.Value),
            BombSeconds = Mathf.Max(10f, Plugin.Cfg.BombSeconds.Value),
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

        /// <summary>Trickshot landed: end the match and announce it so the final killcam has a kill to replay.</summary>
        public void EndTrickshot(int attempts)
        {
            if (Machine == null || Player.LocalPlayer == null) return;
            int me = Player.LocalPlayer.OwnerId;
            if (Machine.State.Phase != MatchPhase.Live) return;
            Machine.EndTrickshot(me, Now, attempts);
            ModNet.BroadcastKill(new KillFeedBroadcast { Killer = Player.LocalPlayer.SteamName, Victim = "Bot", Suicide = false, KillerId = me, VictimId = -2 });
            Flush();
        }
        public void SetMap(int mapIndex) { if (Machine != null) { Machine.SetMap(mapIndex); Flush(); } }
        public void SetMode(MatchMode mode) { if (Machine != null) { Machine.SetMode(mode); Flush(); } }
        public void MoveTeam(int ownerId) { if (Machine != null) { Machine.MoveTeam(ownerId); Flush(); } }

        public void SetLocalLoadout(byte[] ids, bool ready, int rankPoints, byte charm = 0, int vote = -1)
        {
            if (Machine == null || Player.LocalPlayer == null) return;
            Machine.SetLoadout(Player.LocalPlayer.OwnerId, ids, ready, rankPoints, charm, vote);
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

            TickBomb();
            Machine.Tick(Now);
            Flush();
        }

        /// <summary>Search and Destroy timing: players holding the key at the site plant or defuse over a few seconds.</summary>
        private void TickBomb()
        {
            if (!MatchModes.IsBomb(Machine.State.Mode) || Machine.State.Phase != MatchPhase.Live) { if (_bombProgress.Count > 0) _bombProgress.Clear(); _lastBombTick = -1; return; }
            double now = Now;
            double dt = _lastBombTick < 0 ? 0 : now - _lastBombTick;
            _lastBombTick = now;
            // The host's own key counts too.
            if (Player.LocalPlayer) _bombHolding[Player.LocalPlayer.OwnerId] = BombSite.Holding;
            var site = BombSite.SitePos;
            bool changed = false;
            foreach (var kv in _bombHolding.ToList())
            {
                int id = kv.Key;
                bool ok = kv.Value && Machine.CanWorkBomb(id);
                var p = ok ? PlayerManager.Players.FirstOrDefault(x => x && x.OwnerId == id) : null;
                ok = p && !p.Dying.IsDead && p.Transform && Vector3.Distance(p.Transform.position, site) <= BombSite.Reach + 0.5f;
                if (!ok) { if (_bombProgress.Remove(id)) changed = true; continue; }
                double need = Machine.State.BombPlanted ? Machine.Rules.DefuseSeconds : Machine.Rules.PlantSeconds;
                double prog = (_bombProgress.TryGetValue(id, out var cur) ? cur : 0) + dt;
                _bombProgress[id] = prog;
                changed = true;
                if (prog >= need)
                {
                    bool done = Machine.State.BombPlanted ? Machine.Defuse(id, now) : Machine.Plant(id, now);
                    if (done) { Plugin.Log.LogInfo($"Bomb {(Machine.State.BombPlanted ? "planted" : "defused")} by {p.SteamName}"); _bombProgress.Clear(); _bombHolding.Clear(); }
                    break;
                }
            }
            if (changed && Time.unscaledTime >= _nextProgressBroadcast) { _nextProgressBroadcast = Time.unscaledTime + 0.15f; Machine.Dirty = true; }
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
            Machine.SetLoadout(conn.ClientId, msg.ItemIds, msg.Ready, msg.RankPoints, msg.Charm, msg.Vote == 255 ? -1 : msg.Vote);
            Flush();
        }

        private void OnKill(Player victim)
        {
            if (Machine == null || !ModNet.IsHost) return;
            var hit = KillAttribution.TakeDetail(victim.OwnerId);
            int killer = hit.killer;
            var victimSlot = Machine.State.Slot(victim.OwnerId);
            var killerSlot = Machine.State.Slot(killer);
            var detail = Machine.Kill(victim.OwnerId, killer, Now, hit.kind, hit.airborne);
            if (detail.Accepted)
            {
                bool suicide = killerSlot == null || killer == victim.OwnerId;
                Plugin.Log.LogInfo($"Kill: {(suicide ? "(self)" : killerSlot.Name)} -> {victimSlot?.Name} [{hit.kind}{(hit.airborne ? ", airborne" : "")}] streak {detail.Streak} medals [{detail.MedalText}]");
                ModNet.BroadcastKill(new KillFeedBroadcast
                {
                    Killer = suicide ? "" : killerSlot.Name, Victim = victimSlot?.Name ?? "", Suicide = suicide,
                    KillerId = suicide ? -1 : killer, VictimId = victim.OwnerId,
                    Medals = suicide ? "" : detail.MedalText, Streak = suicide ? 0 : detail.Streak, Kind = (byte)hit.kind
                });
            }
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
                players.Add((player, spawn.pos, spawn.yaw, LoadoutService.ForcedLoadout(state.Mode, slot.Loadout)));
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
            if (!IsOpen || !MatchModes.RespawnsInPlace(Machine.State.Mode) || Machine.State.Phase != MatchPhase.Live) yield break;
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
            LoadoutService.ServerGive(player, LoadoutService.ForcedLoadout(Machine.State.Mode, slot.Loadout), best.pos);
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

        private float BombProgressFraction(out int who)
        {
            who = -1; double best = 0;
            foreach (var kv in _bombProgress) if (kv.Value > best) { best = kv.Value; who = kv.Key; }
            if (who < 0) return 0f;
            double need = Machine.State.BombPlanted ? Machine.Rules.DefuseSeconds : Machine.Rules.PlantSeconds;
            return (float)System.Math.Min(1.0, best / need);
        }

        private MatchStateBroadcast ToBroadcast(MatchState s, MatchRules rules)
        {
            var tm = InstanceFinder.TimeManager;
            uint endTick = tm.TickDelta > 0 ? (uint)System.Math.Max(0, System.Math.Round(s.PhaseEndsAt / tm.TickDelta)) : 0u;
            var entries = s.Players.Select(p => new PlayerEntry
            {
                Id = p.Id, Name = p.Name ?? "", Team = (byte)p.Team, Kills = p.Kills, Deaths = p.Deaths, Ready = p.Ready, HasMod = p.HasMod,
                RankPoints = p.RankPoints, Loadout = p.Loadout, Charm = p.Charm, Vote = p.Vote, ModVersion = _helloVersions.TryGetValue(p.Id, out var v) ? v : ""
            }).ToArray();
            return new MatchStateBroadcast
            {
                Phase = (byte)s.Phase, Mode = (byte)s.Mode, Round = s.Round, MatchNumber = s.MatchNumber,
                TeamScoreA = s.TeamScore[0], TeamScoreB = s.TeamScore[1], TeamAIsLeft = s.TeamAIsLeft,
                PhaseEndsAtTick = endTick, LastRoundWinnerTeam = s.LastRoundWinnerTeam,
                MatchWinnerTeam = s.MatchWinnerTeam, MatchWinnerId = s.MatchWinnerId, StatusText = s.StatusText ?? "",
                MapIndex = (byte)s.MapIndex, KillsToWin = rules.KillsToWin, RoundsToWin = rules.RoundsToWin, Players = entries,
                RespawnSeconds = (float)rules.FfaRespawnSeconds, RoundEndSeconds = (float)rules.RoundEndSeconds, MatchEndSeconds = (float)rules.MatchEndSeconds,
                BombPlanted = s.BombPlanted, AttackersTeam = (byte)s.AttackersTeam,
                BombEndsAtTick = tm.TickDelta > 0 && s.BombPlanted ? (uint)System.Math.Max(0, System.Math.Round(s.BombExplodesAt / tm.TickDelta)) : 0u,
                RoundEndsAtTick = tm.TickDelta > 0 && s.RoundEndsAt > 0 ? (uint)System.Math.Max(0, System.Math.Round(s.RoundEndsAt / tm.TickDelta)) : 0u,
                PlantProgress = BombProgressFraction(out int progId), PlantProgressId = progId
            };
        }
    }
}
