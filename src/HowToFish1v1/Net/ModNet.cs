using System;
using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using HowToFish1v1.Net.Proto2;
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
        public static event Action<KillFeedBroadcast> KillFeedReceived;
        public static event Action<NetworkConnection, AimBroadcast> AimReceived;
        public static event Action<AimStateBroadcast> AimStateReceived;
        public static event Action<NetworkConnection, BounceBroadcast> BounceReceived;
        public static event Action<BounceStateBroadcast> BounceStateReceived;
        public static event Action<CheatBroadcast> CheatReceived;
        public static event Action<NetworkConnection, KnifeBroadcast> KnifeReceived;
        public static event Action<KnifeStateBroadcast> KnifeStateReceived;
        public static event Action ClientStopped;
        /// <summary>Host side: a remote connection dropped (its client id may be reused later).</summary>
        public static event Action<int> RemoteDisconnected;

        private static NetworkManager _nm;
        private static float _nextHello;

        public static bool IsHost => InstanceFinder.IsServerStarted;
        public static bool IsClient => InstanceFinder.IsClientStarted;

        /// <summary>True once FishNet has authenticated our client connection; sending mod traffic earlier would get us kicked.</summary>
        public static bool ClientAuthenticated
        {
            get
            {
                if (!IsClient) return false;
                var conn = InstanceFinder.ClientManager?.Connection;
                return conn != null && conn.IsAuthenticated;
            }
        }

        public static void Init() => ModSerializers.Register();

        /// <summary>Call every frame; binds to the NetworkManager the first time it appears.</summary>
        public static void Update()
        {
            if (_nm != null) return;
            var nm = InstanceFinder.NetworkManager;
            if (nm == null) return;
            _nm = nm;
            // Hello may arrive before authentication finishes; it carries nothing sensitive.
            nm.ServerManager.RegisterBroadcast<HelloBroadcast>((conn, msg, ch) => HelloReceived?.Invoke(conn, msg), requireAuthentication: false);
            nm.ServerManager.RegisterBroadcast<LoadoutBroadcast>((conn, msg, ch) => LoadoutReceived?.Invoke(conn, msg));
            nm.ServerManager.OnRemoteConnectionState += (conn, args) =>
            {
                if (args.ConnectionState == RemoteConnectionState.Stopped) RemoteDisconnected?.Invoke(conn.ClientId);
            };
            nm.ClientManager.RegisterBroadcast<MatchStateBroadcast>((msg, ch) => StateReceived?.Invoke(msg));
            nm.ClientManager.RegisterBroadcast<ArenaBroadcast>((msg, ch) => ArenaReceived?.Invoke(msg));
            nm.ClientManager.RegisterBroadcast<KillFeedBroadcast>((msg, ch) => KillFeedReceived?.Invoke(msg));
            nm.ServerManager.RegisterBroadcast<AimBroadcast>((conn, msg, ch) => AimReceived?.Invoke(conn, msg));
            nm.ClientManager.RegisterBroadcast<AimStateBroadcast>((msg, ch) => AimStateReceived?.Invoke(msg));
            nm.ServerManager.RegisterBroadcast<BounceBroadcast>((conn, msg, ch) => BounceReceived?.Invoke(conn, msg));
            nm.ClientManager.RegisterBroadcast<BounceStateBroadcast>((msg, ch) => BounceStateReceived?.Invoke(msg));
            nm.ClientManager.RegisterBroadcast<CheatBroadcast>((msg, ch) => CheatReceived?.Invoke(msg));
            nm.ServerManager.RegisterBroadcast<KnifeBroadcast>((conn, msg, ch) => KnifeReceived?.Invoke(conn, msg));
            nm.ClientManager.RegisterBroadcast<KnifeStateBroadcast>((msg, ch) => KnifeStateReceived?.Invoke(msg));
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
            if (!ClientAuthenticated || hostKnowsUs) return;
            if (Time.unscaledTime < _nextHello) return;
            _nextHello = Time.unscaledTime + 3f;
            SendHello();
        }

        public static void SendHello()
        {
            if (!ClientAuthenticated) return;
            InstanceFinder.ClientManager.Broadcast(new HelloBroadcast { ModVersion = Plugin.Version });
        }

        public static void SendLoadout(byte[] ids, bool ready, int rankPoints, byte charm = 0, int vote = -1)
        {
            if (!ClientAuthenticated) return;
            InstanceFinder.ClientManager.Broadcast(new LoadoutBroadcast { ItemIds = ids ?? Array.Empty<byte>(), Ready = ready, RankPoints = rankPoints, ModVersion = Plugin.Version, Charm = charm, Vote = vote < 0 ? (byte)255 : (byte)vote });
        }

        public static void BroadcastState(MatchStateBroadcast s)
        {
            if (!IsHost) return;
            InstanceFinder.ServerManager.Broadcast(s);
        }

        public static void SendAim(bool ads)
        {
            if (!ClientAuthenticated) return;
            InstanceFinder.ClientManager.Broadcast(new AimBroadcast { Ads = ads }, Channel.Reliable);
        }

        public static void SendKnife(byte skin)
        {
            if (!ClientAuthenticated) return;
            InstanceFinder.ClientManager.Broadcast(new KnifeBroadcast { Skin = skin }, Channel.Reliable);
        }

        public static void BroadcastKnifeState(int ownerId, byte skin)
        {
            if (!IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new KnifeStateBroadcast { OwnerId = ownerId, Skin = skin });
        }

        public static void BroadcastCheat(CheatBroadcast msg)
        {
            if (!IsHost) return;
            InstanceFinder.ServerManager.Broadcast(msg);
        }

        public static void SendBounce(UnityEngine.Vector3 from, UnityEngine.Vector3 to)
        {
            if (!ClientAuthenticated) return;
            InstanceFinder.ClientManager.Broadcast(new BounceBroadcast { From = from, To = to }, Channel.Reliable);
        }

        public static void BroadcastBounce(int ownerId, UnityEngine.Vector3 from, UnityEngine.Vector3 to)
        {
            if (!IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new BounceStateBroadcast { OwnerId = ownerId, From = from, To = to });
        }

        public static void BroadcastAimState(int ownerId, bool ads)
        {
            if (!IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new AimStateBroadcast { OwnerId = ownerId, Ads = ads });
        }

        public static void BroadcastKill(KillFeedBroadcast k)
        {
            if (!IsHost) return;
            InstanceFinder.ServerManager.Broadcast(k);
        }

        public static void BroadcastArena(ArenaBroadcast a)
        {
            if (!IsHost) return;
            InstanceFinder.ServerManager.Broadcast(a);
        }
    }
}
