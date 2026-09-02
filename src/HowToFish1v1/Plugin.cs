using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using HowToFish1v1.Net;
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
            Log.LogInfo($"{Name} {Version} loaded. Panel key: {Cfg.PanelKey.Value}");
        }

        private void Start()
        {
            ModNet.HelloReceived += (conn, msg) => Log.LogInfo($"Hello from client {conn.ClientId}: mod {msg.ModVersion}");
        }

        private void Update()
        {
            ModNet.Update();
            AutoHostForTesting();
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
