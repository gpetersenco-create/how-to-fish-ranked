using System.Collections.Generic;
using System.Linq;
using HowToFish1v1.Core;
using HowToFish1v1.Match;
using HowToFish1v1.Net;
using UnityEngine;

namespace HowToFish1v1.UI
{
    /// <summary>IMGUI panel: players, loadout picker, ready, and host controls.</summary>
    public static class LobbyPanel
    {
        public static bool IsOpen => ModState.PanelOpen;

        private static readonly List<byte> _selected = new List<byte>();
        private static bool _ready;
        private static Vector2 _scroll;
        private static Rect _rect = new Rect(40, 40, 520, 620);

        public static void Toggle()
        {
            if (!Player.LocalPlayer) { Plugin.Log.LogInfo("Join or host a game before opening the 1v1 panel"); return; }
            ModState.PanelOpen = !ModState.PanelOpen;
            PlayerCamera.ToggleMouse(ModState.PanelOpen);
            if (ModState.PanelOpen && ModNet.IsHost && !Plugin.Host.IsOpen) Plugin.Host.Open();
        }

        public static void Draw()
        {
            if (!IsOpen) return;
            _rect = GUILayout.Window(19191, _rect, DrawWindow, "How to Fish 1v1");
        }

        private static void DrawWindow(int id)
        {
            bool host = ModNet.IsHost;
            var s = ClientMatchView.Latest;
            bool has = ClientMatchView.HasState && ModState.IsActive;

            GUILayout.Label(host ? "You are the host." : "Host controls the match.");
            if (!has)
            {
                GUILayout.Label(host ? "Opening..." : "Waiting for the host to open the 1v1 panel.");
                if (GUILayout.Button("Close")) Toggle();
                GUI.DragWindow();
                return;
            }

            GUILayout.Label($"Phase: {(MatchPhase)s.Phase}   Round: {s.Round}");
            GUILayout.Label(s.StatusText ?? "");
            GUILayout.Space(6);
            DrawSlot("A", s.AId, s.AName, s.AScore, s.AReady, s.AHasMod, s.ALoadout);
            DrawSlot("B", s.BId, s.BName, s.BScore, s.BReady, s.BHasMod, s.BLoadout);
            GUILayout.Space(8);

            bool inLobby = (MatchPhase)s.Phase == MatchPhase.Lobby;
            // The host clears Ready flags after a match or when someone leaves; mirror that locally.
            if (inLobby && _ready && !ClientMatchView.Me.Ready) _ready = false;

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
            GUILayout.Space(4);
            int max = Mathf.Max(0, Plugin.Cfg.MaxLoadoutGuns.Value);
            GUILayout.Label($"Your loadout (pick up to {max}):");
            GUI.enabled = inLobby;
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(260));
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
                if (GUILayout.Button("Quit 1v1")) { Plugin.Host.Quit(); _ready = false; }
            }
            if (GUILayout.Button("Close panel")) Toggle();
            GUILayout.EndHorizontal();
            GUI.DragWindow();
        }

        private static void DrawSlot(string label, int id, string name, int score, bool ready, bool hasMod, byte[] loadout)
        {
            if (id == -1) { GUILayout.Label($"{label}: (empty)"); return; }
            string guns = loadout == null || loadout.Length == 0 ? "fists" :
                string.Join(", ", loadout.Select(b => LoadoutService.DisplayName(GameInfo.IDToItem(b))));
            string you = id == ModState.LocalOwnerId ? " (you)" : "";
            GUILayout.Label($"{label}: {name}{you}  score {score}  {(hasMod ? "mod OK" : "NO MOD")}  {(ready ? "READY" : "not ready")}  [{guns}]");
        }

        private static void SendLoadout(bool ready)
        {
            _ready = ready;
            var ids = _selected.ToArray();
            if (ModNet.IsHost) Plugin.Host.SetLocalLoadout(ids, ready);
            else ModNet.SendLoadout(ids, ready);
        }
    }
}
