using System.Collections.Generic;
using System.Linq;
using FishNet;
using HarmonyLib;
using UnityEngine;

namespace HowToFish1v1.Match
{
    public static class LoadoutService
    {
        private static List<Item> _weapons;

        /// <summary>Every item prefab that is a gun (has a Weapon component), sorted by name.</summary>
        public static IReadOnlyList<Item> Weapons()
        {
            if (_weapons == null || _weapons.Count == 0)
            {
                _weapons = Resources.LoadAll<Item>("Items")
                    .Where(i => i && i.Weapon)
                    .OrderBy(i => i.name)
                    .ToList();
                Plugin.Log.LogInfo($"Weapon catalog: {string.Join(", ", _weapons.Select(w => $"{w.name}#{w.ID}"))}");
            }
            return _weapons;
        }

        public static string DisplayName(Item item) => item ? item.name.Replace("(Clone)", "").Trim() : "?";

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

        /// <summary>Server only. Spawns the loadout: first gun into the hands, the rest into inventory slots 0..n.</summary>
        public static void ServerGive(Player p, byte[] itemIds, Vector3 pos)
        {
            if (!p || !InstanceFinder.IsServerStarted || itemIds == null) return;
            byte slot = 0;
            for (int i = 0; i < itemIds.Length; i++)
            {
                var prefab = GameInfo.IDToItem(itemIds[i]);
                if (!prefab) { Plugin.Log.LogWarning($"Unknown item id {itemIds[i]}"); continue; }
                var item = Object.Instantiate(prefab, pos + Vector3.up * 0.5f, Quaternion.identity);
                item.SetSyncedHolder(p, true);
                InstanceFinder.ServerManager.Spawn(item.gameObject);
                if (i == 0)
                {
                    if (!p.Owner.IsLocalClient) p.Hands.PrepareForItemPickup(itemIds[i]);
                    p.Holding.SetHeldItem(item);
                }
                else
                {
                    p.Inventory.AddItem(slot, item);
                    slot++;
                }
            }
        }

        /// <summary>Local client. Fills the magazine of every gun the local player carries and clears reload state.</summary>
        public static void RefillLocalAmmo()
        {
            var p = Player.LocalPlayer;
            if (!p) return;
            var guns = new List<Weapon>();
            if (p.Holding.HeldItem && p.Holding.HeldItem.Weapon) guns.Add(p.Holding.HeldItem.Weapon);
            foreach (var kv in p.Inventory._items)
                if (kv.Value && kv.Value.Weapon) guns.Add(kv.Value.Weapon);
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
