using System.Collections.Generic;
using HowToFish1v1.Core;
using UnityEngine;

namespace HowToFish1v1.Arena
{
    internal static class ArenaMaterials
    {
        private static readonly Dictionary<BoxKind, Material> _cache = new Dictionary<BoxKind, Material>();

        public static Material For(BoxKind kind)
        {
            if (_cache.TryGetValue(kind, out var m) && m) return m;
            Color c;
            switch (kind)
            {
                case BoxKind.Rust: c = new Color(0.72f, 0.33f, 0.12f); break;
                case BoxKind.Steel: c = new Color(0.25f, 0.27f, 0.30f); break;
                case BoxKind.Wood: c = new Color(0.55f, 0.38f, 0.20f); break;
                case BoxKind.Brick: c = new Color(0.45f, 0.20f, 0.15f); break;
                case BoxKind.Yellow: c = new Color(0.95f, 0.78f, 0.10f); break;
                case BoxKind.Red: c = new Color(0.75f, 0.12f, 0.10f); break;
                case BoxKind.Blue: c = new Color(0.15f, 0.30f, 0.70f); break;
                case BoxKind.White: c = new Color(0.90f, 0.90f, 0.85f); break;
                default: c = new Color(0.62f, 0.62f, 0.60f); break;
            }
            var shader = FindShader();
            m = shader ? new Material(shader) : new Material(Shader.Find("Sprites/Default"));
            m.name = "HTF1v1_" + kind;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            m.color = c;
            _cache[kind] = m;
            return m;
        }

        private static Shader FindShader()
        {
            foreach (var name in new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Standard" })
            {
                var s = Shader.Find(name);
                if (s) return s;
            }
            foreach (var s in Resources.FindObjectsOfTypeAll<Shader>())
                if (s.name == "Universal Render Pipeline/Lit") return s;
            Plugin.Log.LogWarning("No URP shader found; arena will use a fallback material");
            return null;
        }
    }
}
