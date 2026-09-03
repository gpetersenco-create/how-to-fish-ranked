using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using HowToFish1v1.Match;
using HowToFish1v1.Net;
using HowToFish1v1.UI;
using UnityEngine;

namespace HowToFish1v1
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.gavin.howtofish1v1";
        public const string Name = "HowToFish1v1";
        public const string Version = "0.2.17";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }
        public static ModConfig Cfg { get; private set; }
        public static HostMatchController Host { get; private set; }

        private Harmony _harmony;
        private bool _autoHosted;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Cfg = new ModConfig(Config);
            ModNet.Init();
            RankService.Init();
            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            ClientMatchView.Init(this);
            Hud.Init();
            KillCam.Init();
            Host = new HostMatchController(this);
            Log.LogInfo($"{Name} {Version} loaded. Panel key: {Cfg.PanelKey.Value}. Rank: {RankService.RankName} ({RankService.Points})");
        }

        private void Start()
        {
            StartCoroutine(Updater.Run());
            StartCoroutine(CloudRanks.Report());
            StartCoroutine(CloudRanks.Refresh(force: true));
            ModNet.HelloReceived += (conn, msg) => Log.LogInfo($"Hello from client {conn.ClientId}: mod {msg.ModVersion}{(msg.ModVersion == Version ? "" : " (MISMATCH, ours is " + Version + ")")}");
            ModNet.LoadoutReceived += (conn, msg) => Log.LogInfo($"Loadout from client {conn.ClientId}: ready={msg.Ready} mod {msg.ModVersion}");
        }

        private void Update()
        {
            if (Input.GetKeyDown(Cfg.PanelKey.Value) && !MainMenuManager.IsInMenu) LobbyPanel.Toggle();
            ModNet.Update();
            ClientMatchView.Update();
            RankedMenu.Update();
            RankedMenu.ApplyPendingSetup();
            Host.Update();
            Recorder.Update();
            KillCam.Update();
            if (Input.GetKeyDown(Cfg.KillcamPreviewKey.Value) && ModState.IsActive && !ModState.PanelOpen) KillCam.StartPreview();
            WeaponSkins.Update();
            ModAttachments.Update();
            UI.ClassPreview.Update();
            Leaderboard.Update();
            Hud.Update();
            AutoHostForTesting();
            DebugAutoTest.Update();
            DebugMenuDump.Update();
        }

        private void OnGUI()
        {
            LobbyPanel.Draw();
            Scoreboard.Draw();
            Match.KillCam.DrawOverlay();
            Hud.DrawKillcamCard();
            Updater.Draw();
            RankedMenu.Draw();
        }

        private void AutoHostForTesting()
        {
            if (_autoHosted || !Cfg.AutoHostOffline.Value || Time.time < 10f) return;
            if (!MainMenuManager.IsInMenu || !ConnectionManager.Instance) return;
            _autoHosted = true;
            Log.LogInfo("AutoHostOffline: creating offline lobby");
            ConnectionManager.Instance.CreateOfflineLobby();
        }
    }
}
