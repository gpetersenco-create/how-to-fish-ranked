using System;
using System.Collections.Generic;

namespace HowToFish1v1.Core
{
    /// <summary>One gun in a loadout with its attachment and skin choices (indices into the gun's own option lists).</summary>
    public struct LoadoutGun
    {
        public byte ItemId;
        public byte Sight;
        public byte Barrel;
        public byte Bullets;
        public bool ExtendedMag;
        public bool Laser;
        /// <summary>Mod-made drum magazine (SMG, pistol): much bigger magazine.</summary>
        public bool Drum;
        /// <summary>Mod-made "switch" (pistol): full auto.</summary>
        public bool Switch;
        /// <summary>Grip: less spread and kick. Fast mag: quicker reloads. Flashlight: a lamp under the barrel.</summary>
        public bool Grip, FastMag, Flashlight;
        public byte Skin;

        public LoadoutGun(byte itemId) { ItemId = itemId; Sight = 0; Barrel = 0; Bullets = 0; ExtendedMag = false; Laser = false; Drum = false; Switch = false; Grip = false; FastMag = false; Flashlight = false; Skin = 0; }

        public int ModCount => (Sight > 0 ? 1 : 0) + (Barrel > 0 ? 1 : 0) + (Bullets > 0 ? 1 : 0) + (ExtendedMag ? 1 : 0) + (Laser ? 1 : 0) + (Drum ? 1 : 0) + (Switch ? 1 : 0) + (Grip ? 1 : 0) + (FastMag ? 1 : 0) + (Flashlight ? 1 : 0);
    }

    /// <summary>Packs guns into the byte array carried by the loadout messages: 6 bytes per gun.</summary>
    public static class LoadoutCodec
    {
        public const int Stride = 6;

        public static byte[] Encode(IReadOnlyList<LoadoutGun> guns)
        {
            if (guns == null || guns.Count == 0) return Array.Empty<byte>();
            var bytes = new byte[guns.Count * Stride];
            for (int i = 0; i < guns.Count; i++)
            {
                var g = guns[i];
                int o = i * Stride;
                bytes[o] = g.ItemId;
                bytes[o + 1] = g.Sight;
                bytes[o + 2] = g.Barrel;
                bytes[o + 3] = g.Bullets;
                bytes[o + 4] = (byte)((g.ExtendedMag ? 1 : 0) | (g.Laser ? 2 : 0) | (g.Drum ? 4 : 0) | (g.Switch ? 8 : 0) | (g.Grip ? 16 : 0) | (g.FastMag ? 32 : 0) | (g.Flashlight ? 64 : 0));
                bytes[o + 5] = g.Skin;
            }
            return bytes;
        }

        public static List<LoadoutGun> Decode(byte[] bytes)
        {
            var list = new List<LoadoutGun>();
            if (bytes == null) return list;
            for (int o = 0; o + Stride <= bytes.Length; o += Stride)
            {
                list.Add(new LoadoutGun
                {
                    ItemId = bytes[o], Sight = bytes[o + 1], Barrel = bytes[o + 2], Bullets = bytes[o + 3],
                    ExtendedMag = (bytes[o + 4] & 1) != 0, Laser = (bytes[o + 4] & 2) != 0,
                    Drum = (bytes[o + 4] & 4) != 0, Switch = (bytes[o + 4] & 8) != 0,
                    Grip = (bytes[o + 4] & 16) != 0, FastMag = (bytes[o + 4] & 32) != 0, Flashlight = (bytes[o + 4] & 64) != 0, Skin = bytes[o + 5]
                });
            }
            return list;
        }

        /// <summary>Keeps at most maxGuns whole guns.</summary>
        public static byte[] Truncate(byte[] bytes, int maxGuns)
        {
            var guns = Decode(bytes);
            if (guns.Count > maxGuns) guns.RemoveRange(Math.Max(0, maxGuns), guns.Count - Math.Max(0, maxGuns));
            return Encode(guns);
        }
    }
}
