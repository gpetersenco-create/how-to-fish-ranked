using System;
using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace HowToFish1v1.Net
{
    /// <summary>Registers broadcast handlers once a NetworkManager exists and exposes typed events + send helpers.</summary>
    public static class ModNet
    {
        public static event Action<NetworkConnection, HelloBroadcast> HelloReceived;
        public static event Action<NetworkConnection, LoadoutBroadcast> LoadoutReceived;
        public static event Action<MatchStateBroadcast> StateReceived;
        public static event Action<ArenaBroadcast> ArenaReceived;
        public static event Action ClientStopped;
        /// <summary>Host side: a remote connection dropped (its client id may be reused later).</summary>
        public static event Action<int> RemoteDisconnected;

        private static NetworkManager _nm;
        private static float _nextHello;

        public static bool IsHost => InstanceFinder.IsServerStarted;
        public static bool IsClient => InstanceFinder.IsClientStarted;

        public static void Init() => ModSerializers.Register();

        /// <summary>Call every frame; binds to the NetworkManager the first time it appears.</summary>
        public static void Update()
        {
            if (_nm != null) return;
            var nm = InstanceFinder.NetworkManager;
            if (nm == null) return;
            _nm = nm;
            nm.ServerManager.RegisterBroadcast<HelloBroadcast>((conn, msg, ch) => HelloReceived?.Invoke(conn, msg));
            nm.ServerManager.RegisterBroadcast<LoadoutBroadcast>((conn, msg, ch) => LoadoutReceived?.Invoke(conn, msg));
            nm.ServerManager.OnRemoteConnectionState += (conn, args) =>
            {
                if (args.ConnectionState == RemoteConnectionState.Stopped) RemoteDisconnected?.Invoke(conn.ClientId);
            };
            nm.ClientManager.RegisterBroadcast<MatchStateBroadcast>((msg, ch) => StateReceived?.Invoke(msg));
            nm.ClientManager.RegisterBroadcast<ArenaBroadcast>((msg, ch) => ArenaReceived?.Invoke(msg));
            nm.ClientManager.OnAuthenticated += SendHello;
            nm.ClientManager.OnClientConnectionState += args =>
            {
                if (args.ConnectionState == LocalConnectionState.Started) _nextHello = Time.unscaledTime + 1f;
                if (args.ConnectionState == LocalConnectionState.Stopped) ClientStopped?.Invoke();
            };
            Plugin.Log.LogInfo("Broadcast handlers registered");
        }

        /// <summary>Repeats the hello every few seconds until the host reports that we have the mod. Call every frame.</summary>
        public static void KeepHelloAlive(bool hostKnowsUs)
        {
            if (!IsClient || hostKnowsUs) return;
            if (Time.unscaledTime < _nextHello) return;
            _nextHello = Time.unscaledTime + 3f;
            SendHello();
        }

        public static void SendHello()
        {
            if (!IsClient) return;
            InstanceFinder.ClientManager.Broadcast(new HelloBroadcast { ModVersion = Plugin.Version });
        }

        public static void SendLoadout(byte[] ids, bool ready, int rankPoints)
        {
            if (!IsClient) return;
            InstanceFinder.ClientManager.Broadcast(new LoadoutBroadcast { ItemIds = ids ?? Array.Empty<byte>(), Ready = ready, RankPoints = rankPoints, ModVersion = Plugin.Version });
        }

        public static void BroadcastState(MatchStateBroadcast s)
        {
            if (!IsHost) return;
            InstanceFinder.ServerManager.Broadcast(s);
        }

        public static void BroadcastArena(ArenaBroadcast a)
        {
            if (!IsHost) return;
            InstanceFinder.ServerManager.Broadcast(a);
        }
    }
}
