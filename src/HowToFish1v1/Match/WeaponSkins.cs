using System.Collections.Generic;
using System.Linq;
using HowToFish1v1.Core;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// Glowing weapon skins. A skin is a tint plus an emissive colour applied to every mesh of a gun; some skins animate.
    /// Every client applies skins from the loadouts in the match state, so everyone sees everyone's glow.
    /// </summary>
    public static class WeaponSkins
    {
        public static readonly string[] Names = { "Stock", "Neon Blue", "Toxic", "Magma", "Ultraviolet", "Gold", "Ghost", "Rainbow" };
        public static int Count => Names.Length;

        private static readonly Color[] Glow =
        {
            Color.black,
            new Color(0.10f, 0.55f, 1.00f),
            new Color(0.25f, 1.00f, 0.20f),
            new Color(1.00f, 0.30f, 0.05f),
            new Color(0.70f, 0.20f, 1.00f),
            new Color(1.00f, 0.80f, 0.25f),
            new Color(0.85f, 0.95f, 1.00f),
            Color.red,
        };

        private class Applied { public Material[] Mats; public byte Skin; public Renderer Renderer; }

        private static readonly Dictionary<int, Applied> _applied = new Dictionary<int, Applied>();
        private static float _nextScan;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        /// <summary>Call every frame: applies skins to guns as they appear and animates the ones that move.</summary>
        public static void Update()
        {
            if (!ModState.IsActive) { if (_applied.Count > 0) _applied.Clear(); return; }
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.5f;
                Scan();
            }
            Animate();
        }

        private static void Scan()
        {
            var skinByOwnerAndItem = new Dictionary<(int owner, byte item), byte>();
            foreach (var p in ClientMatchView.Players)
                foreach (var g in LoadoutCodec.Decode(p.Loadout))
                    skinByOwnerAndItem[(p.Id, g.ItemId)] = g.Skin;

            foreach (var player in PlayerManager.Players)
            {
                if (!player) continue;
                var items = new List<Item>();
                if (player.Holding && player.Holding.HeldItem) items.Add(player.Holding.HeldItem);
                if (player.Inventory != null) foreach (var kv in player.Inventory._items) if (kv.Value) items.Add(kv.Value);
                foreach (var item in items)
                {
                    if (!(item is Weapon)) continue;
                    if (!skinByOwnerAndItem.TryGetValue((player.OwnerId, item.ID), out byte skin) || skin == 0) continue;
                    foreach (var r in item.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                        int key = r.GetInstanceID();
                        if (_applied.TryGetValue(key, out var a) && a.Skin == skin && a.Renderer) continue;
                        Apply(r, skin);
                    }
                }
            }
            // Forget renderers that were destroyed.
            foreach (var k in _applied.Where(kv => !kv.Value.Renderer).Select(kv => kv.Key).ToList()) _applied.Remove(k);
        }

        private static void Apply(Renderer r, byte skin)
        {
            var mats = r.materials; // instances, safe to modify
            var glow = Glow[Mathf.Clamp(skin, 0, Glow.Length - 1)];
            foreach (var m in mats)
            {
                if (!m) continue;
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty(EmissionId)) m.SetColor(EmissionId, glow * 2.5f);
                Color tint = Color.Lerp(Color.white, glow, 0.35f);
                if (skin == 6) tint = new Color(0.9f, 0.95f, 1f);
                if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, tint);
                else if (m.HasProperty(ColorId)) m.SetColor(ColorId, tint);
            }
            _applied[r.GetInstanceID()] = new Applied { Mats = mats, Skin = skin, Renderer = r };
        }

        private static void Animate()
        {
            float t = Time.unscaledTime;
            foreach (var a in _applied.Values)
            {
                if (!a.Renderer) continue;
                Color c;
                switch (a.Skin)
                {
                    case 3: c = Glow[3] * (2.0f + 1.5f * Mathf.Abs(Mathf.Sin(t * 3f))); break;              // magma pulse
                    case 7: c = Color.HSVToRGB((t * 0.25f) % 1f, 1f, 1f) * 2.5f; break;                      // rainbow cycle
                    case 6: c = Glow[6] * (1.2f + 0.8f * Mathf.Abs(Mathf.Sin(t * 1.5f))); break;              // ghost breathe
                    default: continue;
                }
                foreach (var m in a.Mats) if (m && m.HasProperty(EmissionId)) m.SetColor(EmissionId, c);
            }
        }
    }
}
