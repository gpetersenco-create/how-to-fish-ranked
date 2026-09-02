using HowToFish1v1.Core;
using HowToFish1v1.Match;
using HowToFish1v1.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HowToFish1v1.UI
{
    /// <summary>
    /// "Ranked" button on the main menu (cloned from the game's Host button so it looks native) and the panel it opens:
    /// rank card, mode, map, and hosting. After hosting, the chosen mode/map are applied once the player exists in the world.
    /// </summary>
    public static class RankedMenu
    {
        private const string HostButtonPath = "CanvasHolder/MainMenuCanvas/MainMenuButtons (To toggle)/MainMenuLayout/HostButton";
        private const string CharacterButtonPath = "CanvasHolder/MainMenuCanvas/MainMenuButtons (To toggle)/MainMenuLayout/CharacterButton";

        private static GameObject _button;
        private static bool _panelOpen;
        private static Rect _rect = new Rect(40, 40, 520, 420);
        private static MatchMode _mode = MatchMode.OneVOne;
        private static int _map;
        private static string _status = "";

        // Set when hosting from this menu; consumed by Plugin.Update once the local player exists.
        public static bool PendingHostSetup;
        public static MatchMode PendingMode;
        public static int PendingMap;

        public static void Update()
        {
            if (!MainMenuManager.IsInMenu) { _panelOpen = false; return; }
            if (_button) return;
            var host = GameObject.Find(HostButtonPath);
            var character = GameObject.Find(CharacterButtonPath);
            if (!host) return;
            _button = Object.Instantiate(host, host.transform.parent);
            _button.name = "RankedButton";
            _button.transform.SetSiblingIndex((character ? character.transform.GetSiblingIndex() : host.transform.GetSiblingIndex()) + 1);
            var tmp = _button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp) tmp.text = "Ranked";
            var btn = _button.GetComponent<Button>();
            if (btn)
            {
                btn.onClick = new Button.ButtonClickedEvent(); // drop the inspector-wired Host handler
                btn.onClick.AddListener(() => _panelOpen = !_panelOpen);
            }
            Plugin.Log.LogInfo("Ranked button added to the main menu");
        }

        public static void Draw()
        {
            if (!_panelOpen || !MainMenuManager.IsInMenu) return;
            _rect = GUILayout.Window(19192, _rect, DrawWindow, "Ranked");
        }

        private static void DrawWindow(int id)
        {
            var ladder = RankService.Ladder;
            int pts = RankService.Points;
            GUILayout.Label($"Rank: {RankService.RankName}");
            GUILayout.Label($"{pts} points   {RankService.Wins} wins   {RankService.Losses} losses");
            int toNext = ladder.PointsToNext(pts);
            GUILayout.Label(toNext > 0 ? $"{toNext} points to {ladder.Names[ladder.TierIndex(pts) + 1]}" : "Top rank reached");
            GUILayout.Space(8);

            GUILayout.Label("Mode:");
            GUILayout.BeginHorizontal();
            foreach (var m in MatchModes.All)
            {
                bool on = m == _mode;
                if (GUILayout.Toggle(on, " " + MatchModes.Name(m), "Button", GUILayout.Width(115)) && !on) _mode = m;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Map: {ArenaLayout.MapNames[_map]}", GUILayout.Width(200));
            if (GUILayout.Button("<", GUILayout.Width(40))) _map = (_map + ArenaLayout.MapCount - 1) % ArenaLayout.MapCount;
            if (GUILayout.Button(">", GUILayout.Width(40))) _map = (_map + 1) % ArenaLayout.MapCount;
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            GUILayout.Label("Host Ranked creates an invite-only Steam lobby. Invite friends from the Steam overlay; they need the mod too.");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host Ranked (Steam)")) Host(steam: true);
            if (GUILayout.Button("Solo practice (offline)")) Host(steam: false);
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_status)) GUILayout.Label(_status);
            GUILayout.Space(6);
            if (GUILayout.Button("Close")) _panelOpen = false;
            GUI.DragWindow();
        }

        private static void Host(bool steam)
        {
            if (!ConnectionManager.Instance) { _status = "Connection manager not ready"; return; }
            ModState.RankedSession = true;
            PendingHostSetup = true;
            PendingMode = _mode;
            PendingMap = _map;
            _panelOpen = false;
            _status = "";
            Plugin.Log.LogInfo($"Ranked host: mode {MatchModes.Name(_mode)} map {ArenaLayout.MapNames[_map]} steam={steam}");
            if (steam) SteamManager.CreateLobby();
            else ConnectionManager.Instance.CreateOfflineLobby();
        }

        /// <summary>Called every frame; applies the pending host setup once the host is in the world.</summary>
        public static void ApplyPendingSetup()
        {
            if (!PendingHostSetup || !ModNet.IsHost || !Player.LocalPlayer || MainMenuManager.IsInMenu) return;
            PendingHostSetup = false;
            Plugin.Host.Open();
            Plugin.Host.SetMode(PendingMode);
            Plugin.Host.SetMap(PendingMap);
            LobbyPanel.Open();
        }
    }
}
