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
        public const string Version = "0.1.0";

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
            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            ClientMatchView.Init(this);
            Host = new HostMatchController(this);
            Log.LogInfo($"{Name} {Version} loaded. Panel key: {Cfg.PanelKey.Value}");
        }

        private void Start()
        {
            ModNet.HelloReceived += (conn, msg) => Log.LogInfo($"Hello from client {conn.ClientId}: mod {msg.ModVersion}");
        }

        private void Update()
        {
            if (Input.GetKeyDown(Cfg.PanelKey.Value)) LobbyPanel.Toggle();
            ModNet.Update();
            Host.Update();
            Hud.Update();
            AutoHostForTesting();
            DebugAutoTest.Update();
        }

        private void OnGUI()
        {
            LobbyPanel.Draw();
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
