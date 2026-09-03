using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using HowToFish1v1.Core;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// Attachments the game does not have, driven by the loadout flags every client already knows:
    /// the drum magazine (SMG and pistol: 2.5x the magazine) and the switch (pistol: full auto).
    /// Applied on every client to every weapon whose owner picked them, so the shooter's client fires
    /// full auto and everyone's ammo display agrees.
    /// </summary>
    public static class ModAttachments
    {
        public const float DrumMultiplier = 2.5f;

        private static readonly Dictionary<Attachments, bool> _drum = new Dictionary<Attachments, bool>();
        private static readonly Dictionary<Weapon, bool> _originalAuto = new Dictionary<Weapon, bool>();
        private static float _next;

        /// <summary>Re-check on the next frame (a loadout was just given out).</summary>
        public static void Refresh() { _next = 0f; }

        public static bool HasDrum(Attachments a) => a && _drum.TryGetValue(a, out var on) && on;

        /// <summary>The loadout entry an owner picked for an item id, if any.</summary>
        public static LoadoutGun? GunOf(int ownerId, byte itemId)
        {
            foreach (var p in ClientMatchView.Players)
            {
                if (p.Id != ownerId) continue;
                foreach (var g in LoadoutCodec.Decode(p.Loadout)) if (g.ItemId == itemId) return g;
            }
            return null;
        }

        public static void Update()
        {
            if (!ModState.IsActive)
            {
                if (_originalAuto.Count > 0) { foreach (var kv in _originalAuto.ToList()) SetAuto(kv.Key, kv.Value); _originalAuto.Clear(); }
                if (_drum.Count > 0) _drum.Clear();
                return;
            }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;

            var seen = new HashSet<Weapon>();
            foreach (var player in PlayerManager.Players)
            {
                if (!player) continue;
                var items = new List<Item>();
                if (player.Holding && player.Holding.HeldItem) items.Add(player.Holding.HeldItem);
                try { if (player.Inventory != null) foreach (var kv in player.Inventory._items) if (kv.Value) items.Add(kv.Value); } catch (System.Exception) { }
                foreach (var item in items)
                {
                    if (!(item is Weapon w)) continue;
                    var g = GunOf(player.OwnerId, w.ID);
                    var o = LoadoutService.Options(w.ID);
                    bool drum = g != null && g.Value.Drum && o.HasDrum;
                    bool sw = g != null && g.Value.Switch && o.HasSwitch;
                    if (w.Attachments) _drum[w.Attachments] = drum;
                    seen.Add(w);
                    if (sw)
                    {
                        if (!_originalAuto.ContainsKey(w)) _originalAuto[w] = GetAuto(w);
                        SetAuto(w, true);
                    }
                    else if (_originalAuto.TryGetValue(w, out var orig))
                    {
                        SetAuto(w, orig);
                        _originalAuto.Remove(w);
                    }
                }
            }
            foreach (var w in _originalAuto.Keys.ToList())
                if (!w || !seen.Contains(w)) { if (w) SetAuto(w, _originalAuto[w]); _originalAuto.Remove(w); }
            foreach (var a in _drum.Keys.Where(a => !a).ToList()) _drum.Remove(a);
        }

        private static bool GetAuto(Weapon w)
        {
            try { return Traverse.Create(w).Field<bool>("_fullAuto").Value; } catch (System.Exception) { return false; }
        }

        private static void SetAuto(Weapon w, bool on)
        {
            try { Traverse.Create(w).Field<bool>("_fullAuto").Value = on; } catch (System.Exception) { }
        }
    }

    [HarmonyPatch]
    internal static class ModAttachmentPatches
    {
        // The drum magazine: everything that reads the magazine size (reloads, ammo counter, refills) sees the bigger number.
        [HarmonyPatch(typeof(Attachments), nameof(Attachments.AmmoPerMag), MethodType.Getter)]
        [HarmonyPostfix]
        private static void Drum(Attachments __instance, ref int __result)
        {
            if (ModState.IsActive && ModAttachments.HasDrum(__instance)) __result = Mathf.RoundToInt(__result * ModAttachments.DrumMultiplier);
        }
    }
}
