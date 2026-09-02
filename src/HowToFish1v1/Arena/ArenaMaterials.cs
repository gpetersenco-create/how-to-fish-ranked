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
