using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FishNet;
using FishNet.Connection;
using HowToFish1v1.Arena;
using HowToFish1v1.Core;
using HowToFish1v1.Net;
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
            ModState.KillDetected += OnKill;
        }

        private MatchRules RulesFromConfig() => new MatchRules
        {
            RoundsToWin = Mathf.Max(1, Plugin.Cfg.RoundsToWin.Value),
            CountdownSeconds = Mathf.Max(0f, Plugin.Cfg.CountdownSeconds.Value),
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
            foreach (var kv in _helloVersions) Machine.PlayerSaidHello(kv.Key, kv.Value == Plugin.Version);
            Flush();
        }

        public void Start()
        {
            if (Machine == null) return;
            Machine.Start(Now);
            Flush();
        }

        public void Quit()
        {
            if (Machine == null) return;
            Machine.Quit();
            Flush();
        }

        public void SetMap(int mapIndex)
        {
            if (Machine == null) return;
            Machine.SetMap(mapIndex);
            Flush();
        }

        public void SetLocalLoadout(byte[] ids, bool ready)
        {
            if (Machine == null || Player.LocalPlayer == null) return;
            Machine.SetLoadout(Player.LocalPlayer.OwnerId, ids, ready);
            Flush();
        }

        public void Update()
        {
            if (!IsOpen) return;
            if (!ModNet.IsHost) { Machine.Quit(); Flush(); return; }

            // Joins / leaves
            var present = new HashSet<int>(PlayerManager.Players.Select(p => p.OwnerId));
            foreach (var p in PlayerManager.Players) Machine.PlayerJoined(p.OwnerId, p.SteamName);
            foreach (var slot in new[] { Machine.State.A, Machine.State.B })
                if (slot.IsPresent && !present.Contains(slot.Id)) Machine.PlayerLeft(slot.Id);

            Machine.Tick(Now);
            Flush();
        }

        private void OnHello(NetworkConnection conn, HelloBroadcast msg)
        {
            _helloVersions[conn.ClientId] = msg.ModVersion ?? "";
            if (Machine != null) { Machine.PlayerSaidHello(conn.ClientId, msg.ModVersion == Plugin.Version); Flush(); }
        }

        private void OnLoadout(NetworkConnection conn, LoadoutBroadcast msg)
        {
            if (Machine == null) return;
            Machine.SetLoadout(conn.ClientId, msg.ItemIds, msg.Ready);
            Flush();
        }

        private void OnKill(Player victim)
        {
            if (Machine == null || !ModNet.IsHost) return;
            Machine.Kill(victim.OwnerId, Now);
            Flush();
        }

        private void Flush()
        {
            foreach (var e in Machine.Effects)
            {
                switch (e)
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
                        _runner.StartCoroutine(ResetPlayersRoutine());
                        break;
                }
            }
            Machine.Effects.Clear();
            if (Machine.Dirty)
            {
                Machine.Dirty = false;
                ModNet.BroadcastState(ToBroadcast(Machine.State));
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
            var players = new List<(Player player, Vector3 pos, float yaw, byte[] loadout)>();
            foreach (var slot in new[] { state.A, state.B })
            {
                if (!slot.IsPresent) continue;
                var player = PlayerManager.Players.FirstOrDefault(p => p.OwnerId == slot.Id);
                if (!player) continue;
                var (pos, yaw) = ArenaBuilder.Spawn(state.SideFor(slot.Id));
                players.Add((player, pos, yaw, slot.Loadout));
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

        private static MatchStateBroadcast ToBroadcast(MatchState s)
        {
            var tm = InstanceFinder.TimeManager;
            uint endTick = tm.TickDelta > 0 ? (uint)System.Math.Max(0, System.Math.Round(s.PhaseEndsAt / tm.TickDelta)) : 0u;
            return new MatchStateBroadcast
            {
                Phase = (byte)s.Phase, Round = s.Round,
                AId = s.A.Id, AName = s.A.Name, AScore = s.A.Score, AReady = s.A.Ready, AHasMod = s.A.HasMod, ALoadout = s.A.Loadout,
                BId = s.B.Id, BName = s.B.Name, BScore = s.B.Score, BReady = s.B.Ready, BHasMod = s.B.HasMod, BLoadout = s.B.Loadout,
                AIsLeft = s.AIsLeft, PhaseEndsAtTick = endTick,
                LastRoundWinnerId = s.LastRoundWinnerId, MatchWinnerId = s.MatchWinnerId, StatusText = s.StatusText ?? "",
                MapIndex = (byte)s.MapIndex
            };
        }
    }
}
