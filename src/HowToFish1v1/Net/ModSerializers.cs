using System;
using FishNet;
using FishNet.Serializing;
using HowToFish1v1.Net.Proto2;

namespace HowToFish1v1.Net
{
    /// <summary>
    /// FishNet normally generates serializers at build time; the mod registers them by hand.
    /// Every message is wrapped in a length-prefixed envelope: the body is written to an inner buffer and sent as one byte
    /// array. The receiver always consumes exactly that array from the network stream, so a body that is shorter or longer
    /// than expected (a different mod build) can never desync FishNet's packet parsing, which would disconnect the sender.
    /// </summary>
    internal static class ModSerializers
    {
        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;

            GenericWriter<HelloBroadcast>.SetWrite((w, v) => Envelope(w, b => b.WriteString(v.ModVersion ?? "")));
            GenericReader<HelloBroadcast>.SetRead(r => Open(r, b => new HelloBroadcast { ModVersion = b.ReadStringAllocated() ?? "" }));

            GenericWriter<LoadoutBroadcast>.SetWrite((w, v) => Envelope(w, b =>
            {
                b.WriteUInt8ArrayAndSize(v.ItemIds ?? Array.Empty<byte>());
                b.WriteBoolean(v.Ready);
                b.WriteInt32(v.RankPoints);
                b.WriteString(v.ModVersion ?? "");
                b.WriteUInt8Unpacked(v.Charm);
            }));
            GenericReader<LoadoutBroadcast>.SetRead(r => Open(r, b => new LoadoutBroadcast
            {
                ItemIds = b.ReadUInt8ArrayAndSizeAllocated() ?? Array.Empty<byte>(),
                Ready = b.ReadBoolean(),
                RankPoints = b.ReadInt32(),
                ModVersion = b.ReadStringAllocated() ?? "",
                Charm = b.Remaining >= 1 ? b.ReadUInt8Unpacked() : (byte)1
            }));

            GenericWriter<AimBroadcast>.SetWrite((w, v) => Envelope(w, b => b.WriteBoolean(v.Ads)));
            GenericReader<AimBroadcast>.SetRead(r => Open(r, b => new AimBroadcast { Ads = b.ReadBoolean() }));
            GenericWriter<KnifeBroadcast>.SetWrite((w, v) => Envelope(w, b => b.WriteUInt8Unpacked(v.Skin)));
            GenericReader<KnifeBroadcast>.SetRead(r => Open(r, b => new KnifeBroadcast { Skin = b.ReadUInt8Unpacked() }));
            GenericWriter<KnifeStateBroadcast>.SetWrite((w, v) => Envelope(w, b => { b.WriteInt32(v.OwnerId); b.WriteUInt8Unpacked(v.Skin); }));
            GenericReader<KnifeStateBroadcast>.SetRead(r => Open(r, b => new KnifeStateBroadcast { OwnerId = b.ReadInt32(), Skin = b.ReadUInt8Unpacked() }));
            GenericWriter<CheatBroadcast>.SetWrite((w, v) => Envelope(w, b => { b.WriteInt32(v.OwnerId); b.WriteString(v.Name ?? ""); b.WriteString(v.Reason ?? ""); }));
            GenericReader<CheatBroadcast>.SetRead(r => Open(r, b => new CheatBroadcast { OwnerId = b.ReadInt32(), Name = b.ReadStringAllocated() ?? "", Reason = b.ReadStringAllocated() ?? "" }));
            GenericWriter<BounceBroadcast>.SetWrite((w, v) => Envelope(w, b => { b.WriteVector3(v.From); b.WriteVector3(v.To); }));
            GenericReader<BounceBroadcast>.SetRead(r => Open(r, b => new BounceBroadcast { From = b.ReadVector3(), To = b.ReadVector3() }));
            GenericWriter<BounceStateBroadcast>.SetWrite((w, v) => Envelope(w, b => { b.WriteInt32(v.OwnerId); b.WriteVector3(v.From); b.WriteVector3(v.To); }));
            GenericReader<BounceStateBroadcast>.SetRead(r => Open(r, b => new BounceStateBroadcast { OwnerId = b.ReadInt32(), From = b.ReadVector3(), To = b.ReadVector3() }));
            GenericWriter<AimStateBroadcast>.SetWrite((w, v) => Envelope(w, b => { b.WriteInt32(v.OwnerId); b.WriteBoolean(v.Ads); }));
            GenericReader<AimStateBroadcast>.SetRead(r => Open(r, b => new AimStateBroadcast { OwnerId = b.ReadInt32(), Ads = b.ReadBoolean() }));

            GenericWriter<KillFeedBroadcast>.SetWrite((w, v) => Envelope(w, b =>
            {
                b.WriteString(v.Killer ?? "");
                b.WriteString(v.Victim ?? "");
                b.WriteBoolean(v.Suicide);
                b.WriteInt32(v.KillerId);
                b.WriteInt32(v.VictimId);
            }));
            GenericReader<KillFeedBroadcast>.SetRead(r => Open(r, b => new KillFeedBroadcast
            {
                Killer = b.ReadStringAllocated() ?? "",
                Victim = b.ReadStringAllocated() ?? "",
                Suicide = b.ReadBoolean(),
                KillerId = b.ReadInt32(),
                VictimId = b.ReadInt32()
            }));

            GenericWriter<ArenaBroadcast>.SetWrite((w, v) => Envelope(w, b =>
            {
                b.WriteBoolean(v.Build);
                b.WriteUInt8Unpacked(v.ReturnIsland);
                b.WriteUInt8Unpacked(v.MapIndex);
            }));
            GenericReader<ArenaBroadcast>.SetRead(r => Open(r, b => new ArenaBroadcast
            {
                Build = b.ReadBoolean(),
                ReturnIsland = b.ReadUInt8Unpacked(),
                MapIndex = b.ReadUInt8Unpacked()
            }));

            GenericWriter<MatchStateBroadcast>.SetWrite((w, v) => Envelope(w, b =>
            {
                b.WriteUInt8Unpacked(v.Phase);
                b.WriteUInt8Unpacked(v.Mode);
                b.WriteInt32(v.Round);
                b.WriteInt32(v.MatchNumber);
                b.WriteInt32(v.TeamScoreA);
                b.WriteInt32(v.TeamScoreB);
                b.WriteBoolean(v.TeamAIsLeft);
                b.WriteUInt32(v.PhaseEndsAtTick);
                b.WriteInt32(v.LastRoundWinnerTeam);
                b.WriteInt32(v.MatchWinnerTeam);
                b.WriteInt32(v.MatchWinnerId);
                b.WriteString(v.StatusText ?? "");
                b.WriteUInt8Unpacked(v.MapIndex);
                b.WriteInt32(v.KillsToWin);
                b.WriteInt32(v.RoundsToWin);
                var players = v.Players ?? Array.Empty<PlayerEntry>();
                b.WriteUInt8Unpacked((byte)players.Length);
                foreach (var p in players)
                {
                    b.WriteInt32(p.Id);
                    b.WriteString(p.Name ?? "");
                    b.WriteUInt8Unpacked(p.Team);
                    b.WriteInt32(p.Kills);
                    b.WriteInt32(p.Deaths);
                    b.WriteBoolean(p.Ready);
                    b.WriteBoolean(p.HasMod);
                    b.WriteInt32(p.RankPoints);
                    b.WriteUInt8ArrayAndSize(p.Loadout ?? Array.Empty<byte>());
                    b.WriteString(p.ModVersion ?? "");
                }
                b.WriteSingle(v.RespawnSeconds);
                b.WriteSingle(v.RoundEndSeconds);
                b.WriteSingle(v.MatchEndSeconds);
                foreach (var p in players) b.WriteUInt8Unpacked(p.Charm);
            }));
            GenericReader<MatchStateBroadcast>.SetRead(r => Open(r, b =>
            {
                var s = new MatchStateBroadcast
                {
                    Phase = b.ReadUInt8Unpacked(),
                    Mode = b.ReadUInt8Unpacked(),
                    Round = b.ReadInt32(),
                    MatchNumber = b.ReadInt32(),
                    TeamScoreA = b.ReadInt32(),
                    TeamScoreB = b.ReadInt32(),
                    TeamAIsLeft = b.ReadBoolean(),
                    PhaseEndsAtTick = b.ReadUInt32(),
                    LastRoundWinnerTeam = b.ReadInt32(),
                    MatchWinnerTeam = b.ReadInt32(),
                    MatchWinnerId = b.ReadInt32(),
                    StatusText = b.ReadStringAllocated() ?? "",
                    MapIndex = b.ReadUInt8Unpacked(),
                    KillsToWin = b.ReadInt32(),
                    RoundsToWin = b.ReadInt32()
                };
                int n = b.ReadUInt8Unpacked();
                s.Players = new PlayerEntry[n];
                for (int i = 0; i < n; i++)
                {
                    s.Players[i] = new PlayerEntry
                    {
                        Id = b.ReadInt32(),
                        Name = b.ReadStringAllocated() ?? "",
                        Team = b.ReadUInt8Unpacked(),
                        Kills = b.ReadInt32(),
                        Deaths = b.ReadInt32(),
                        Ready = b.ReadBoolean(),
                        HasMod = b.ReadBoolean(),
                        RankPoints = b.ReadInt32(),
                        Loadout = b.ReadUInt8ArrayAndSizeAllocated() ?? Array.Empty<byte>(),
                        ModVersion = b.ReadStringAllocated() ?? ""
                    };
                }
                // Timing rules were added in 0.2.12; older hosts stop here.
                if (b.Remaining >= 12)
                {
                    s.RespawnSeconds = b.ReadSingle();
                    s.RoundEndSeconds = b.ReadSingle();
                    s.MatchEndSeconds = b.ReadSingle();
                }
                for (int i = 0; i < n; i++) s.Players[i].Charm = b.Remaining >= 1 ? b.ReadUInt8Unpacked() : (byte)1;
                return s;
            }));
        }

        /// <summary>Writes the body into an inner buffer and emits it as one length-prefixed byte array.</summary>
        private static void Envelope(Writer w, Action<Writer> body)
        {
            var inner = WriterPool.Retrieve();
            try
            {
                body(inner);
                var seg = inner.GetArraySegment();
                var bytes = new byte[seg.Count];
                Array.Copy(seg.Array, seg.Offset, bytes, 0, seg.Count);
                w.WriteUInt8ArrayAndSize(bytes);
            }
            finally
            {
                inner.Store();
            }
        }

        /// <summary>Consumes exactly one envelope from the stream, then parses the body from a private reader. Bad bodies yield default.</summary>
        private static T Open<T>(Reader r, Func<Reader, T> body) where T : struct
        {
            var bytes = r.ReadUInt8ArrayAndSizeAllocated();
            if (bytes == null || bytes.Length == 0) return default;
            var inner = ReaderPool.Retrieve(bytes, InstanceFinder.NetworkManager);
            try
            {
                return body(inner);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Ignoring {typeof(T).Name} from another mod build ({e.GetType().Name})");
                return default;
            }
            finally
            {
                inner.Store();
            }
        }
    }
}
