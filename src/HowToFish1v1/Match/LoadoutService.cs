using System;
using System.Collections.Generic;
using System.Linq;
using FishNet;
using HarmonyLib;
using HowToFish1v1.Core;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>Attachment options a gun prefab offers, as display names. Index 0 is always the stock option.</summary>
    public sealed class GunOptions
    {
        public byte ItemId;
        public string Name = "";
        public List<string> Sights = new List<string>();
        public List<string> Barrels = new List<string>();
        public List<string> Bullets = new List<string>();
        public bool HasExtendedMag;
        public bool HasLaser;
    }

    public static class LoadoutService
    {
        private static List<Item> _weapons;
        private static readonly Dictionary<byte, GunOptions> _options = new Dictionary<byte, GunOptions>();

        /// <summary>Every item prefab that is a gun (a Weapon subclass of Item), sorted by name.</summary>
        public static IReadOnlyList<Item> Weapons()
        {
            if (_weapons == null || _weapons.Count == 0)
            {
                // GameInfo already loaded every item prefab from Resources/Items into a private registry.
                var registry = Traverse.Create(typeof(GameInfo)).Field<Dictionary<byte, Item>>("_allItems").Value;
                IEnumerable<Item> all = registry != null && registry.Count > 0 ? registry.Values : Resources.LoadAll<Item>("Items");
                _weapons = all
                    .Where(i => i && i is Weapon)
                    .OrderBy(i => i.name)
                    .ToList();
                Plugin.Log.LogInfo($"Weapon catalog ({_weapons.Count}): {string.Join(", ", _weapons.Select(w => $"{w.name}#{w.ID}"))}");
            }
            return _weapons;
        }

        public static string DisplayName(Item item) => item ? item.name.Replace("(Clone)", "").Trim() : "?";

        /// <summary>Attachment options for a gun, read once from its prefab.</summary>
        public static GunOptions Options(byte itemId)
        {
            if (_options.TryGetValue(itemId, out var cached)) return cached;
            var o = new GunOptions { ItemId = itemId };
            var prefab = GameInfo.IDToItem(itemId);
            o.Name = DisplayName(prefab);
            var att = prefab ? prefab.GetComponentInChildren<Attachments>(true) : null;
            if (att)
            {
                var t = Traverse.Create(att);
                var sights = t.Field<List<Sight>>("_sights").Value;
                var barrels = t.Field<List<BarrelAttachment>>("_barrelAttachments").Value;
                var bullets = t.Field<BulletUpgrade[]>("_bulletUpgrades").Value;
                var laser = t.Field<LaserSight>("_laserSight").Value;
                var extInfo = t.Field<AttachmentInfo>("_extendedMagInfo").Value;
                if (sights != null) for (int i = 0; i < sights.Count; i++) o.Sights.Add(i == 0 ? "Iron sights" : AttachmentName(sights[i], "Sight " + i));
                if (barrels != null) for (int i = 0; i < barrels.Count; i++) o.Barrels.Add(i == 0 ? "Stock barrel" : AttachmentName(barrels[i], "Barrel " + i));
                if (bullets != null) for (int i = 0; i < bullets.Length; i++) o.Bullets.Add(i == 0 ? $"Standard ({bullets[i].Damage} dmg)" : $"Tier {i} ({bullets[i].Damage} dmg)");
                o.HasLaser = laser && laser.Info;
                o.HasExtendedMag = extInfo;
            }
            if (o.Sights.Count == 0) o.Sights.Add("Iron sights");
            if (o.Barrels.Count == 0) o.Barrels.Add("Stock barrel");
            if (o.Bullets.Count == 0) o.Bullets.Add("Standard");
            _options[itemId] = o;
            return o;
        }

        private static string AttachmentName(Attachment a, string fallback)
        {
            if (!a) return fallback;
            try
            {
                if (a.Info)
                {
                    string loc = a.Info.NameLocalized;
                    if (!string.IsNullOrWhiteSpace(loc)) return loc;
                    string raw = Traverse.Create(a.Info).Field<string>("_name").Value;
                    if (!string.IsNullOrWhiteSpace(raw)) return raw;
                }
            }
            catch (Exception) { }
            return string.IsNullOrWhiteSpace(a.gameObject.name) ? fallback : a.gameObject.name;
        }

        /// <summary>"Assault Rifle (+2 mods)" style summary for cards.</summary>
        public static string Summary(byte[] loadout)
        {
            var guns = LoadoutCodec.Decode(loadout);
            if (guns.Count == 0) return "fists";
            return string.Join(", ", guns.Select(g => DisplayName(GameInfo.IDToItem(g.ItemId)) + (g.ModCount > 0 ? $" (+{g.ModCount})" : "")));
        }

        /// <summary>Server only. Destroys the held item, everything in the inventory, and the death ragdoll.</summary>
        public static void ServerClearItems(Player p)
        {
            if (!p || !InstanceFinder.IsServerStarted) return;
            var held = p.Holding.HeldItem;
            if (held)
            {
                p.Holding.SetHeldItem(null);
                held.DestroyItem(7);
            }
            var inv = p.Inventory;
            foreach (var key in inv._items.Keys.ToList())
            {
                var item = inv._items[key];
                if (item) item.DestroyItem(7);
                inv._items[key] = null;
            }
            var ragdoll = p.Dying.DeadPlayer;
            if (ragdoll) ragdoll.DestroyItem(7);
        }

        /// <summary>Server only. Spawns the loadout with attachments: first gun into the hands, the rest into inventory slots 0..n.</summary>
        public static void ServerGive(Player p, byte[] loadout, Vector3 pos)
        {
            if (!p || !InstanceFinder.IsServerStarted) return;
            var guns = LoadoutCodec.Decode(loadout);
            byte slot = 0;
            for (int i = 0; i < guns.Count; i++)
            {
                var g = guns[i];
                var prefab = GameInfo.IDToItem(g.ItemId);
                if (!prefab) { Plugin.Log.LogWarning($"Unknown item id {g.ItemId}"); continue; }
                // Spawn above the head: an item instantiated inside the player's capsule shoves them away.
                var item = UnityEngine.Object.Instantiate(prefab, pos + Vector3.up * 2.5f, Quaternion.identity);
                item.SetSyncedHolder(p, true);
                InstanceFinder.ServerManager.Spawn(item.gameObject);
                ApplyAttachments(item as Weapon, g);
                if (i == 0)
                {
                    if (!p.Owner.IsLocalClient) p.Hands.PrepareForItemPickup(g.ItemId);
                    p.Holding.SetHeldItem(item);
                }
                else
                {
                    p.Inventory.AddItem(slot, item);
                    slot++;
                }
            }
        }

        private static void ApplyAttachments(Weapon w, LoadoutGun g)
        {
            if (!w || !w.Attachments) return;
            var att = w.Attachments;
            var o = Options(g.ItemId);
            att._syncedSight.Value = (byte)Mathf.Clamp(g.Sight, 0, o.Sights.Count - 1);
            att._syncedBarrelAttachment.Value = (byte)Mathf.Clamp(g.Barrel, 0, o.Barrels.Count - 1);
            att._syncedBulletIndex.Value = (byte)Mathf.Clamp(g.Bullets, 0, o.Bullets.Count - 1);
            att._syncedExtendedMag.Value = g.ExtendedMag && o.HasExtendedMag;
            att._syncedLaserSight.Value = g.Laser && o.HasLaser;
        }

        /// <summary>Local client. Fills the magazine of every gun the local player carries and clears reload state.</summary>
        public static void RefillLocalAmmo()
        {
            var p = Player.LocalPlayer;
            if (!p) return;
            var guns = new List<Weapon>();
            if (p.Holding.HeldItem is Weapon held) guns.Add(held);
            foreach (var kv in p.Inventory._items)
                if (kv.Value is Weapon w) guns.Add(w);
            foreach (var w in guns)
            {
                var t = Traverse.Create(w);
                t.Property("Ammo").SetValue(w.Attachments.AmmoPerMag);
                t.Field("_isReloading").SetValue(false);
                t.Field("_queueReload").SetValue(false);
            }
        }
    }
}
