using System.Collections.Generic;
using System.Linq;
using HowToFish1v1.Core;
using HowToFish1v1.Match;
using HowToFish1v1.Net;
using UnityEngine;

namespace HowToFish1v1.UI
{
    /// <summary>In-session IMGUI panel: mode, map, players and teams, loadout picker, ready, and host controls.</summary>
    public static class LobbyPanel
    {
        public static bool IsOpen => ModState.PanelOpen;

        private static readonly List<byte> _selected = new List<byte>();
        private static bool _ready;
        private static Vector2 _scroll;
        private static Rect _rect = new Rect(40, 40, 620, 700);

        public static void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        public static void Open()
        {
            if (!Player.LocalPlayer) { Plugin.Log.LogInfo("Join or host a game before opening the match panel"); return; }
            ModState.PanelOpen = true;
            PlayerCamera.ToggleMouse(true);
            if (ModNet.IsHost && !Plugin.Host.IsOpen) Plugin.Host.Open();
        }

        public static void Close()
        {
            ModState.PanelOpen = false;
            PlayerCamera.ToggleMouse(false);
        }

        public static void Draw()
        {
            if (!IsOpen) return;
            _rect = GUILayout.Window(19191, _rect, DrawWindow, "How to Fish Ranked");
        }

        private static void DrawWindow(int id)
        {
            bool host = ModNet.IsHost;
            var s = ClientMatchView.Latest;
            bool has = ClientMatchView.HasState && ModState.IsActive;

            GUILayout.Label($"You: {RankService.RankName}  ({RankService.Points} pts, {RankService.Wins}W {RankService.Losses}L)");
            GUILayout.Label(host ? "You are the host." : "Host controls the match.");
            if (!has)
            {
                GUILayout.Label(host ? "Opening..." : "Waiting for the host to open the match panel.");
                if (GUILayout.Button("Close")) Close();
                GUI.DragWindow();
                return;
            }

            var mode = (MatchMode)s.Mode;
            bool inLobby = (MatchPhase)s.Phase == MatchPhase.Lobby;
            bool ffa = MatchModes.IsFfa(mode);
            GUILayout.Label($"Phase: {(MatchPhase)s.Phase}   {(ffa ? $"First to {s.KillsToWin} kills" : $"Round {s.Round}, first to {s.RoundsToWin}")}");
            GUILayout.Label(s.StatusText ?? "");
            GUILayout.Space(4);

            // Mode
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mode:", GUILayout.Width(50));
            foreach (var m in MatchModes.All)
            {
                GUI.enabled = host && inLobby;
                bool on = m == mode;
                if (GUILayout.Toggle(on, " " + MatchModes.Name(m), "Button", GUILayout.Width(120)) && !on) Plugin.Host.SetMode(m);
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();

            // Map
            string mapName = ArenaLayout.MapNames[((s.MapIndex % ArenaLayout.MapCount) + ArenaLayout.MapCount) % ArenaLayout.MapCount];
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Map: {mapName}", GUILayout.Width(200));
            if (host)
            {
                GUI.enabled = inLobby;
                if (GUILayout.Button("<", GUILayout.Width(40))) Plugin.Host.SetMap(s.MapIndex - 1);
                if (GUILayout.Button(">", GUILayout.Width(40))) Plugin.Host.SetMap(s.MapIndex + 1);
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            // Players
            var players = ClientMatchView.Players;
            if (!ffa)
            {
                GUILayout.Label($"Team A  {s.TeamScoreA}  -  {s.TeamScoreB}  Team B");
                foreach (int team in new[] { 0, 1 })
                {
                    GUILayout.Label(team == 0 ? "Team A:" : "Team B:");
                    foreach (var p in players.Where(p => p.Team == team)) DrawPlayer(p, host && inLobby, ffa);
                }
            }
            else
            {
                GUILayout.Label("Players (kills):");
                foreach (var p in players.OrderByDescending(p => p.Kills)) DrawPlayer(p, false, ffa);
            }
            if (players.Length == 0) GUILayout.Label("  (nobody yet)");
            GUILayout.Space(8);

            // Loadout
            if (inLobby && _ready && ClientMatchView.Me is PlayerEntry me && !me.Ready) _ready = false;
            int max = Mathf.Max(0, Plugin.Cfg.MaxLoadoutGuns.Value);
            GUILayout.Label($"Your loadout (pick up to {max}):");
            GUI.enabled = inLobby;
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(150));
            foreach (var item in LoadoutService.Weapons())
            {
                bool on = _selected.Contains(item.ID);
                bool now = GUILayout.Toggle(on, $"  {LoadoutService.DisplayName(item)}");
                if (now && !on && _selected.Count < max) { _selected.Add(item.ID); SendLoadout(false); }
                else if (!now && on) { _selected.Remove(item.ID); SendLoadout(false); }
            }
            GUILayout.EndScrollView();
            bool readyNow = GUILayout.Toggle(_ready, _ready ? "  READY" : "  Ready up");
            if (readyNow != _ready) { _ready = readyNow; SendLoadout(_ready); }
            GUI.enabled = true;

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (host)
            {
                GUI.enabled = inLobby;
                if (GUILayout.Button("Start match")) Plugin.Host.Start();
                GUI.enabled = true;
                if (GUILayout.Button("Quit ranked")) { Plugin.Host.Quit(); _ready = false; }
            }
            if (GUILayout.Button("Close panel")) Close();
            GUILayout.EndHorizontal();
            GUI.DragWindow();
        }

        private static void DrawPlayer(PlayerEntry p, bool canMove, bool ffa)
        {
            string guns = p.Loadout == null || p.Loadout.Length == 0 ? "fists" :
                string.Join(", ", p.Loadout.Select(b => LoadoutService.DisplayName(GameInfo.IDToItem(b))));
            string you = p.Id == ModState.LocalOwnerId ? " (you)" : "";
            string rank = RankService.Ladder.TierName(p.RankPoints);
            string score = ffa ? $"{p.Kills} kills" : "";
            GUILayout.BeginHorizontal();
            GUILayout.Label($"  {p.Name}{you}  [{rank}]  {score}  {(p.HasMod ? "mod OK" : "NO MOD")}  {(p.Ready ? "READY" : "not ready")}  ({guns})");
            if (canMove && GUILayout.Button("Move", GUILayout.Width(50))) Plugin.Host.MoveTeam(p.Id);
            GUILayout.EndHorizontal();
        }

        private static void SendLoadout(bool ready)
        {
            _ready = ready;
            var ids = _selected.ToArray();
            if (ModNet.IsHost) Plugin.Host.SetLocalLoadout(ids, ready, RankService.Points);
            else ModNet.SendLoadout(ids, ready, RankService.Points);
        }

        /// <summary>Re-sends the current loadout so the host learns updated rank points.</summary>
        public static void ResendLoadout() => SendLoadout(_ready);
    }
}
