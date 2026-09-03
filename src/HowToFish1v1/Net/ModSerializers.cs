using System;
using FishNet.Serializing;

namespace HowToFish1v1.Net
{
    /// <summary>
    /// FishNet normally generates these at build time; the mod registers them by hand.
    /// Field order must match between write and read.
    /// </summary>
    internal static class ModSerializers
    {
        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;

            GenericWriter<HelloBroadcast>.SetWrite((w, v) => w.WriteString(v.ModVersion ?? ""));
            GenericReader<HelloBroadcast>.SetRead(r => new HelloBroadcast { ModVersion = r.ReadStringAllocated() ?? "" });

            GenericWriter<LoadoutBroadcast>.SetWrite((w, v) =>
            {
                w.WriteUInt8ArrayAndSize(v.ItemIds ?? Array.Empty<byte>());
                w.WriteBoolean(v.Ready);
                w.WriteInt32(v.RankPoints);
                w.WriteString(v.ModVersion ?? "");
            });
            GenericReader<LoadoutBroadcast>.SetRead(r => new LoadoutBroadcast
            {
                ItemIds = r.ReadUInt8ArrayAndSizeAllocated() ?? Array.Empty<byte>(),
                Ready = r.ReadBoolean(),
                RankPoints = r.ReadInt32(),
                ModVersion = r.ReadStringAllocated() ?? ""
            });

            GenericWriter<ArenaBroadcast>.SetWrite((w, v) =>
            {
                w.WriteBoolean(v.Build);
                w.WriteUInt8Unpacked(v.ReturnIsland);
                w.WriteUInt8Unpacked(v.MapIndex);
            });
            GenericReader<ArenaBroadcast>.SetRead(r => new ArenaBroadcast
            {
                Build = r.ReadBoolean(),
                ReturnIsland = r.ReadUInt8Unpacked(),
                MapIndex = r.ReadUInt8Unpacked()
            });

            GenericWriter<MatchStateBroadcast>.SetWrite((w, v) =>
            {
                w.WriteUInt8Unpacked(v.Phase);
                w.WriteUInt8Unpacked(v.Mode);
                w.WriteInt32(v.Round);
                w.WriteInt32(v.MatchNumber);
                w.WriteInt32(v.TeamScoreA);
                w.WriteInt32(v.TeamScoreB);
                w.WriteBoolean(v.TeamAIsLeft);
                w.WriteUInt32(v.PhaseEndsAtTick);
                w.WriteInt32(v.LastRoundWinnerTeam);
                w.WriteInt32(v.MatchWinnerTeam);
                w.WriteInt32(v.MatchWinnerId);
                w.WriteString(v.StatusText ?? "");
                w.WriteUInt8Unpacked(v.MapIndex);
                w.WriteInt32(v.KillsToWin);
                w.WriteInt32(v.RoundsToWin);
                var players = v.Players ?? Array.Empty<PlayerEntry>();
                w.WriteUInt8Unpacked((byte)players.Length);
                foreach (var p in players)
                {
                    w.WriteInt32(p.Id);
                    w.WriteString(p.Name ?? "");
                    w.WriteUInt8Unpacked(p.Team);
                    w.WriteInt32(p.Kills);
                    w.WriteInt32(p.Deaths);
                    w.WriteBoolean(p.Ready);
                    w.WriteBoolean(p.HasMod);
                    w.WriteInt32(p.RankPoints);
                    w.WriteUInt8ArrayAndSize(p.Loadout ?? Array.Empty<byte>());
                }
            });
            GenericReader<MatchStateBroadcast>.SetRead(r =>
            {
                var s = new MatchStateBroadcast
                {
                    Phase = r.ReadUInt8Unpacked(),
                    Mode = r.ReadUInt8Unpacked(),
                    Round = r.ReadInt32(),
                    MatchNumber = r.ReadInt32(),
                    TeamScoreA = r.ReadInt32(),
                    TeamScoreB = r.ReadInt32(),
                    TeamAIsLeft = r.ReadBoolean(),
                    PhaseEndsAtTick = r.ReadUInt32(),
                    LastRoundWinnerTeam = r.ReadInt32(),
                    MatchWinnerTeam = r.ReadInt32(),
                    MatchWinnerId = r.ReadInt32(),
                    StatusText = r.ReadStringAllocated() ?? "",
                    MapIndex = r.ReadUInt8Unpacked(),
                    KillsToWin = r.ReadInt32(),
                    RoundsToWin = r.ReadInt32()
                };
                int n = r.ReadUInt8Unpacked();
                s.Players = new PlayerEntry[n];
                for (int i = 0; i < n; i++)
                {
                    s.Players[i] = new PlayerEntry
                    {
                        Id = r.ReadInt32(),
                        Name = r.ReadStringAllocated() ?? "",
                        Team = r.ReadUInt8Unpacked(),
                        Kills = r.ReadInt32(),
                        Deaths = r.ReadInt32(),
                        Ready = r.ReadBoolean(),
                        HasMod = r.ReadBoolean(),
                        RankPoints = r.ReadInt32(),
                        Loadout = r.ReadUInt8ArrayAndSizeAllocated() ?? Array.Empty<byte>()
                    };
                }
                return s;
            });
        }
    }
}
