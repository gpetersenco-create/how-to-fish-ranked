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

        private class Applied { public Material[] Mats; public Material[] Original; public byte Skin; public Renderer Renderer; }

        private static readonly Dictionary<int, Applied> _applied = new Dictionary<int, Applied>();
        private static float _nextScan;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        /// <summary>Call every frame: applies skins to guns as they appear and animates the ones that move.</summary>
        public static void Update()
        {
            if (!ModState.IsActive) { if (_applied.Count > 0) RestoreAll(); return; }
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
                    skinByOwnerAndItem.TryGetValue((player.OwnerId, item.ID), out byte skin);
                    Renderer hands = null;
                    try { hands = item is Tool tool ? tool.HandsMesh : null; } catch (System.Exception) { }
                    foreach (var r in item.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                        if (r == hands) continue;   // the player's hands on the gun keep the game's skin-colour shader
                        int key = r.GetInstanceID();
                        bool has = _applied.TryGetValue(key, out var a) && a.Renderer;
                        if (skin == 0) { if (has) Restore(key); continue; }
                        if (has && a.Skin == skin) continue;
                        if (has) Restore(key);
                        Apply(r, skin);
                    }
                }
            }
            // Forget renderers that were destroyed.
            foreach (var k in _applied.Where(kv => !kv.Value.Renderer).Select(kv => kv.Key).ToList()) _applied.Remove(k);
        }

        /// <summary>
        /// The game's gun materials use its own shaders, which have no emission, so a glow cannot be added to them. Each
        /// material is swapped for a lit-shader copy that keeps the original texture and adds the tint and the glow; the
        /// originals are put back when the skin changes or the match ends.
        /// </summary>
        private static void Apply(Renderer r, byte skin)
        {
            var original = r.sharedMaterials;
            var glow = Glow[Mathf.Clamp(skin, 0, Glow.Length - 1)];
            Color tint = Color.Lerp(Color.white, glow, 0.35f);
            if (skin == 6) tint = new Color(0.9f, 0.95f, 1f);
            var shader = Arena.ArenaMaterials.LitShader;
            var mats = new Material[original.Length];
            for (int i = 0; i < original.Length; i++)
            {
                var src = original[i];
                var m = shader ? new Material(shader) : (src ? new Material(src) : null);
                if (!m) continue;
                m.name = "HTF1v1_Skin_" + Names[Mathf.Clamp(skin, 0, Names.Length - 1)];
                Texture tex = null;
                try { tex = src ? src.mainTexture : null; } catch (System.Exception) { }
                if (tex)
                {
                    if (m.HasProperty(BaseMapId)) m.SetTexture(BaseMapId, tex);
                    else if (m.HasProperty(MainTexId)) m.SetTexture(MainTexId, tex);
                }
                if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, tint);
                else if (m.HasProperty(ColorId)) m.SetColor(ColorId, tint);
                if (m.HasProperty(MetallicId)) m.SetFloat(MetallicId, 0.55f);
                if (m.HasProperty(SmoothnessId)) m.SetFloat(SmoothnessId, 0.75f);
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty(EmissionId)) m.SetColor(EmissionId, glow * 2.5f);
                mats[i] = m;
            }
            r.sharedMaterials = mats;
            _applied[r.GetInstanceID()] = new Applied { Mats = mats, Original = original, Skin = skin, Renderer = r };
        }

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        private static void Restore(int key)
        {
            if (!_applied.TryGetValue(key, out var a)) return;
            _applied.Remove(key);
            if (a.Renderer && a.Original != null) a.Renderer.sharedMaterials = a.Original;
            if (a.Mats != null) foreach (var m in a.Mats) if (m) Object.Destroy(m);
        }

        private static void RestoreAll()
        {
            foreach (var key in _applied.Keys.ToList()) Restore(key);
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
