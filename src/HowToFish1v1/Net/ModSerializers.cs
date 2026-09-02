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
            });
            GenericReader<LoadoutBroadcast>.SetRead(r => new LoadoutBroadcast
            {
                ItemIds = r.ReadUInt8ArrayAndSizeAllocated() ?? Array.Empty<byte>(),
                Ready = r.ReadBoolean()
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
                w.WriteInt32(v.Round);
                w.WriteInt32(v.AId); w.WriteString(v.AName ?? ""); w.WriteInt32(v.AScore); w.WriteBoolean(v.AReady); w.WriteBoolean(v.AHasMod); w.WriteUInt8ArrayAndSize(v.ALoadout ?? Array.Empty<byte>());
                w.WriteInt32(v.BId); w.WriteString(v.BName ?? ""); w.WriteInt32(v.BScore); w.WriteBoolean(v.BReady); w.WriteBoolean(v.BHasMod); w.WriteUInt8ArrayAndSize(v.BLoadout ?? Array.Empty<byte>());
                w.WriteBoolean(v.AIsLeft);
                w.WriteUInt32(v.PhaseEndsAtTick);
                w.WriteInt32(v.LastRoundWinnerId);
                w.WriteInt32(v.MatchWinnerId);
                w.WriteString(v.StatusText ?? "");
                w.WriteUInt8Unpacked(v.MapIndex);
            });
            GenericReader<MatchStateBroadcast>.SetRead(r => new MatchStateBroadcast
            {
                Phase = r.ReadUInt8Unpacked(),
                Round = r.ReadInt32(),
                AId = r.ReadInt32(), AName = r.ReadStringAllocated() ?? "", AScore = r.ReadInt32(), AReady = r.ReadBoolean(), AHasMod = r.ReadBoolean(), ALoadout = r.ReadUInt8ArrayAndSizeAllocated() ?? Array.Empty<byte>(),
                BId = r.ReadInt32(), BName = r.ReadStringAllocated() ?? "", BScore = r.ReadInt32(), BReady = r.ReadBoolean(), BHasMod = r.ReadBoolean(), BLoadout = r.ReadUInt8ArrayAndSizeAllocated() ?? Array.Empty<byte>(),
                AIsLeft = r.ReadBoolean(),
                PhaseEndsAtTick = r.ReadUInt32(),
                LastRoundWinnerId = r.ReadInt32(),
                MatchWinnerId = r.ReadInt32(),
                StatusText = r.ReadStringAllocated() ?? "",
                MapIndex = r.ReadUInt8Unpacked()
            });
        }
    }
}
