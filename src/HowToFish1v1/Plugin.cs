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
        public const string Version = "0.3.3";

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
            // Configs saved by 0.2.37/0.2.38 carry the old 35% ricochet default; bring them to the new 10%.
            if (Mathf.Abs(Cfg.RicochetChance.Value - 0.35f) < 0.001f) Cfg.RicochetChance.Value = 0.10f;
            ModNet.Init();
            RankService.Init();
            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            ClientMatchView.Init(this);
            Hud.Init();
            KillCam.Init();
            MatchEvents.Init();
            Host = new HostMatchController(this);
            Log.LogInfo($"{Name} {Version} loaded. Panel key: {Cfg.PanelKey.Value}. Rank: {RankService.RankName} ({RankService.Points})");
        }

        private void Start()
        {
            StartCoroutine(Updater.Run());
            StartCoroutine(HitSounds.LoadFiles());
            StartCoroutine(Announcer.LoadFiles());
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
            if (Input.GetKeyDown(Cfg.CaughtPreviewKey.Value) && !MainMenuManager.IsInMenu) Hud.Announce(AntiCheat.Message, 8f, true);
            WeaponSkins.Update();
            ModAttachments.Update();
            Knife.Update();
            Trickshot.Update();
            Ricochet.Update();
            BombSite.Update();
            Grenades.Update();
            Fx.Update();
            Spectate.Update();
            AntiCheat.Update();
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
            HitReactions.Draw();
            Hud.DrawRadar();
            Hud.DrawBombHud();
            Hud.DrawGrenadeHud();
            Match.KillCam.DrawOverlay();
            Hud.DrawKillcamCard();
            Hud.DrawAnnouncement();
            Updater.Draw();
            RankedMenu.Draw();
            Results.Draw();
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
