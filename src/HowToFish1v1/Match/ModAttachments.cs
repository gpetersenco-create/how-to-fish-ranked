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
        private static readonly Dictionary<Weapon, float> _originalRate = new Dictionary<Weapon, float>();
        private static readonly Dictionary<Weapon, (float spread, int kick)> _originalGrip = new Dictionary<Weapon, (float, int)>();
        private static readonly Dictionary<Weapon, bool> _fastMag = new Dictionary<Weapon, bool>();
        private static readonly Dictionary<Weapon, GameObject> _lamps = new Dictionary<Weapon, GameObject>();
        public const float GripSpreadScale = 0.6f;
        public const float FastMagSpeed = 1.6f;
        public const float SwitchRateMultiplier = 0.5f;   // time between shots is halved: twice the fire rate
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
                if (_originalRate.Count > 0) { foreach (var kv in _originalRate.ToList()) SetRate(kv.Key, kv.Value); _originalRate.Clear(); }
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
                    bool grip = g != null && g.Value.Grip;
                    bool fast = g != null && g.Value.FastMag;
                    bool lamp = g != null && g.Value.Flashlight;
                    if (w.Attachments) _drum[w.Attachments] = drum;
                    seen.Add(w);
                    ApplyGrip(w, grip);
                    ApplyFastMag(w, fast);
                    ApplyLamp(w, lamp);
                    if (sw)
                    {
                        if (!_originalAuto.ContainsKey(w)) _originalAuto[w] = GetAuto(w);
                        SetAuto(w, true);
                        if (!_originalRate.ContainsKey(w)) { _originalRate[w] = GetRate(w); SetRate(w, _originalRate[w] * SwitchRateMultiplier); }
                    }
                    else
                    {
                        if (_originalAuto.TryGetValue(w, out var orig)) { SetAuto(w, orig); _originalAuto.Remove(w); }
                        if (_originalRate.TryGetValue(w, out var rate)) { SetRate(w, rate); _originalRate.Remove(w); }
                    }
                }
            }
            foreach (var w in _originalAuto.Keys.ToList())
                if (!w || !seen.Contains(w)) { if (w) SetAuto(w, _originalAuto[w]); _originalAuto.Remove(w); }
            foreach (var w in _originalRate.Keys.ToList())
                if (!w || !seen.Contains(w)) { if (w) SetRate(w, _originalRate[w]); _originalRate.Remove(w); }
            foreach (var w in _originalGrip.Keys.ToList())
                if (!w || !seen.Contains(w)) { if (w) ApplyGrip(w, false); _originalGrip.Remove(w); }
            foreach (var w in _lamps.Keys.ToList())
                if (!w || !seen.Contains(w)) { if (_lamps[w]) Object.Destroy(_lamps[w]); _lamps.Remove(w); }
            foreach (var a in _drum.Keys.Where(a => !a).ToList()) _drum.Remove(a);
        }

        private static void ApplyGrip(Weapon w, bool on)
        {
            try
            {
                var t = Traverse.Create(w);
                if (on && !_originalGrip.ContainsKey(w))
                {
                    _originalGrip[w] = (t.Field<float>("_spread").Value, t.Field<int>("_recoilKnockback").Value);
                    t.Field<float>("_spread").Value = _originalGrip[w].spread * GripSpreadScale;
                    t.Field<int>("_recoilKnockback").Value = Mathf.RoundToInt(_originalGrip[w].kick * GripSpreadScale);
                }
                else if (!on && _originalGrip.TryGetValue(w, out var orig))
                {
                    t.Field<float>("_spread").Value = orig.spread;
                    t.Field<int>("_recoilKnockback").Value = orig.kick;
                    _originalGrip.Remove(w);
                }
            }
            catch (System.Exception) { }
        }

        private static void ApplyFastMag(Weapon w, bool on)
        {
            try
            {
                var anim = Traverse.Create(w).Field<Animation>("_anim").Value;
                if (!anim) return;
                float speed = on ? FastMagSpeed : 1f;
                foreach (var name in new[] { "Reload", "ReloadLast" })
                {
                    var st = anim[name];
                    if (st != null && Mathf.Abs(st.speed - speed) > 0.01f) st.speed = speed;
                }
                _fastMag[w] = on;
            }
            catch (System.Exception) { }
        }

        private static void ApplyLamp(Weapon w, bool on)
        {
            _lamps.TryGetValue(w, out var lamp);
            if (!on) { if (lamp) { Object.Destroy(lamp); _lamps.Remove(w); } return; }
            if (lamp) return;
            Transform fp = null;
            try { fp = w.Attachments ? w.Attachments.FirePoint : null; } catch (System.Exception) { }
            if (!fp) return;
            lamp = new GameObject("HTF1v1_Flashlight");
            lamp.transform.SetParent(fp, false);
            lamp.transform.localPosition = new Vector3(0f, -0.02f, 0f);
            lamp.transform.localRotation = Quaternion.identity;
            var light = lamp.AddComponent<Light>();
            light.type = LightType.Spot;
            light.spotAngle = 42f; light.innerSpotAngle = 20f;
            light.range = 28f; light.intensity = 9f;
            light.color = new Color(1f, 0.96f, 0.85f);
            light.shadows = LightShadows.None;
            _lamps[w] = lamp;
        }

        private static bool GetAuto(Weapon w)
        {
            try { return Traverse.Create(w).Field<bool>("_fullAuto").Value; } catch (System.Exception) { return false; }
        }

        private static void SetAuto(Weapon w, bool on)
        {
            try { Traverse.Create(w).Field<bool>("_fullAuto").Value = on; } catch (System.Exception) { }
        }

        private static float GetRate(Weapon w)
        {
            try { return Traverse.Create(w).Field<float>("_timeBetweenShots").Value; } catch (System.Exception) { return 0.2f; }
        }

        private static void SetRate(Weapon w, float seconds)
        {
            try { Traverse.Create(w).Field<float>("_timeBetweenShots").Value = seconds; } catch (System.Exception) { }
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
