using System.Collections.Generic;
using System.Linq;
using HowToFish1v1.Core;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// Weapon skins. The game's gun materials use its own shaders (no emission, no free colour), so a skin swaps every
    /// gun material for a lit-shader copy: original texture or a generated pattern, a tint, metal/gloss, and a glow that
    /// may animate. Every client applies skins from the loadouts in the match state, so everyone sees everyone's skin.
    /// The Dragon skin is reserved for one Steam account and only fits the sniper: a dragon head on the barrel that
    /// breathes fire, with a burst on every shot.
    /// </summary>
    public static class WeaponSkins
    {
        public static readonly string[] Names =
        {
            "Stock", "Neon Blue", "Toxic", "Magma", "Ultraviolet", "Gold", "Ghost", "Rainbow",
            "Diamond", "Carbon Fiber", "Galaxy", "Frost", "Bloodshot", "Dragon"
        };
        public static int Count => Names.Length;
        public const byte Diamond = 8, Carbon = 9, Galaxy = 10, Frost = 11, Blood = 12, Dragon = 13;

        /// <summary>The one account allowed to use the Dragon skin.</summary>
        public const ulong DragonOwner = 76561199637934759UL;

        private struct Def { public Color Glow; public Color Tint; public float Metal; public float Gloss; public System.Func<Texture2D> Tex; public float GlowScale; }

        private static readonly Def[] Defs =
        {
            new Def(),
            new Def { Glow = new Color(0.10f, 0.55f, 1.00f), Tint = new Color(0.68f, 0.84f, 1f), Metal = 0.55f, Gloss = 0.75f, GlowScale = 2.5f },
            new Def { Glow = new Color(0.25f, 1.00f, 0.20f), Tint = new Color(0.74f, 1f, 0.72f), Metal = 0.55f, Gloss = 0.75f, GlowScale = 2.5f },
            new Def { Glow = new Color(1.00f, 0.30f, 0.05f), Tint = new Color(1f, 0.75f, 0.66f), Metal = 0.55f, Gloss = 0.75f, GlowScale = 2.5f },
            new Def { Glow = new Color(0.70f, 0.20f, 1.00f), Tint = new Color(0.9f, 0.72f, 1f), Metal = 0.55f, Gloss = 0.75f, GlowScale = 2.5f },
            new Def { Glow = new Color(1.00f, 0.80f, 0.25f), Tint = new Color(1f, 0.85f, 0.35f), Metal = 0.95f, Gloss = 0.9f, GlowScale = 1.2f },
            new Def { Glow = new Color(0.85f, 0.95f, 1.00f), Tint = new Color(0.9f, 0.95f, 1f), Metal = 0.3f, Gloss = 0.6f, GlowScale = 2.5f },
            new Def { Glow = Color.red, Tint = Color.white, Metal = 0.55f, Gloss = 0.75f, GlowScale = 2.5f },
            new Def { Glow = new Color(0.55f, 0.85f, 1f), Tint = Color.white, Metal = 0.85f, Gloss = 0.98f, Tex = DiamondTex, GlowScale = 0.9f },
            new Def { Glow = Color.black, Tint = Color.white, Metal = 0.4f, Gloss = 0.85f, Tex = CarbonTex, GlowScale = 0f },
            new Def { Glow = new Color(0.45f, 0.25f, 1f), Tint = Color.white, Metal = 0.5f, Gloss = 0.8f, Tex = GalaxyTex, GlowScale = 1.4f },
            new Def { Glow = new Color(0.6f, 0.85f, 1f), Tint = Color.white, Metal = 0.6f, Gloss = 0.95f, Tex = FrostTex, GlowScale = 0.8f },
            new Def { Glow = new Color(0.6f, 0.02f, 0.02f), Tint = Color.white, Metal = 0.5f, Gloss = 0.7f, Tex = BloodTex, GlowScale = 0.9f },
            new Def { Glow = new Color(1f, 0.35f, 0.05f), Tint = Color.white, Metal = 0.5f, Gloss = 0.7f, Tex = DragonTex, GlowScale = 1.2f },
        };

        private class Applied { public Material[] Mats; public Material[] Original; public byte Skin; public Renderer Renderer; public GameObject Extra; }

        private static readonly Dictionary<int, Applied> _applied = new Dictionary<int, Applied>();
        private static readonly Dictionary<Item, GameObject> _dragons = new Dictionary<Item, GameObject>();
        private static readonly Dictionary<byte, Texture2D> _textures = new Dictionary<byte, Texture2D>();
        private static float _nextScan;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        // ------------------------------------------------------------------ rules

        /// <summary>Can the local player pick this skin in the lobby?</summary>
        public static bool CanPick(byte skin)
        {
            if (skin >= Count) return false;
            if (skin != Dragon) return true;
            return RankService.LocalId == DragonOwner.ToString();
        }

        /// <summary>Whether a skin applies for an owner and an item: the Dragon needs its owner and a sniper.</summary>
        public static bool AppliesTo(byte skin, Player owner, string itemName)
        {
            if (skin == 0 || skin >= Count) return false;
            if (skin != Dragon) return true;
            if (itemName == null || !itemName.ToLowerInvariant().Contains("snip")) return false;
            try { return owner && owner._steamID.Value == DragonOwner; } catch (System.Exception) { return false; }
        }

        public static bool IsSniper(string itemName) => itemName != null && itemName.ToLowerInvariant().Contains("snip");

        // ------------------------------------------------------------------ per frame

        /// <summary>Call every frame: applies skins to guns as they appear and animates the ones that move.</summary>
        public static void Update()
        {
            if (!ModState.IsActive) { if (_applied.Count > 0) RestoreAll(); if (_dragons.Count > 0) RemoveAllDragons(); return; }
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.5f;
                Scan();
            }
            Animate();
            foreach (var kv in _dragons)
            {
                if (!kv.Value) continue;
                bool ads = kv.Key is Weapon w && w.Holder && w.Holder.Owner != null && w.Holder.Owner.IsLocalClient && w.IsAds;
                if (kv.Value.activeSelf == ads) kv.Value.SetActive(!ads);
            }
        }

        private static void Scan()
        {
            var skinByOwnerAndItem = new Dictionary<(int owner, byte item), byte>();
            foreach (var p in ClientMatchView.Players)
                foreach (var g in LoadoutCodec.Decode(p.Loadout))
                    skinByOwnerAndItem[(p.Id, g.ItemId)] = g.Skin;

            var liveItems = new HashSet<Item>();
            foreach (var player in PlayerManager.Players)
            {
                if (!player) continue;
                var items = new List<Item>();
                if (player.Holding && player.Holding.HeldItem) items.Add(player.Holding.HeldItem);
                if (player.Inventory != null) foreach (var kv in player.Inventory._items) if (kv.Value) items.Add(kv.Value);
                foreach (var item in items)
                {
                    if (!(item is Weapon)) continue;
                    liveItems.Add(item);
                    skinByOwnerAndItem.TryGetValue((player.OwnerId, item.ID), out byte skin);
                    string name = LoadoutService.DisplayName(item);
                    if (!AppliesTo(skin, player, name)) skin = 0;
                    Renderer hands = null;
                    try { hands = item is Tool tool ? tool.HandsMesh : null; } catch (System.Exception) { }
                    foreach (var r in item.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                        if (r == hands) continue;                       // the player's hands keep the game's skin-colour shader
                        if (r.name.StartsWith("HTF1v1_")) continue;     // our own accessories
                        int key = r.GetInstanceID();
                        bool has = _applied.TryGetValue(key, out var a) && a.Renderer;
                        if (skin == 0) { if (has) Restore(key); continue; }
                        if (has && a.Skin == skin) continue;
                        if (has) Restore(key);
                        Apply(r, skin);
                    }
                    if (skin == Dragon) EnsureDragon(item);
                    else RemoveDragon(item);
                }
            }
            foreach (var k in _applied.Where(kv => !kv.Value.Renderer).Select(kv => kv.Key).ToList()) _applied.Remove(k);
            foreach (var item in _dragons.Keys.ToList()) if (!item || !liveItems.Contains(item)) RemoveDragon(item);
        }

        // ------------------------------------------------------------------ materials

        /// <summary>Lit-shader replacements for a renderer's materials in the given skin (also used by the class preview).</summary>
        public static Material[] MaterialsFor(byte skin, Material[] original, List<Object> track = null)
        {
            var d = Defs[Mathf.Clamp(skin, 0, Defs.Length - 1)];
            var shader = Arena.ArenaMaterials.LitShader;
            var tex = d.Tex != null ? TextureFor(skin) : null;
            var mats = new Material[original.Length];
            for (int i = 0; i < original.Length; i++)
            {
                var src = original[i];
                var m = shader ? new Material(shader) : (src ? new Material(src) : null);
                if (!m) continue;
                m.name = "HTF1v1_Skin_" + Names[Mathf.Clamp(skin, 0, Names.Length - 1)];
                Texture use = tex;
                if (!use) { try { use = src ? src.mainTexture : null; } catch (System.Exception) { } }
                if (use)
                {
                    if (m.HasProperty(BaseMapId)) m.SetTexture(BaseMapId, use);
                    else if (m.HasProperty(MainTexId)) m.SetTexture(MainTexId, use);
                    if (tex) { m.SetTextureScale(BaseMapId, new Vector2(3f, 3f)); }
                }
                if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, d.Tint);
                else if (m.HasProperty(ColorId)) m.SetColor(ColorId, d.Tint);
                if (m.HasProperty(MetallicId)) m.SetFloat(MetallicId, d.Metal);
                if (m.HasProperty(SmoothnessId)) m.SetFloat(SmoothnessId, d.Gloss);
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty(EmissionId)) m.SetColor(EmissionId, d.Glow * d.GlowScale);
                mats[i] = m;
                track?.Add(m);
            }
            return mats;
        }

        private static void Apply(Renderer r, byte skin)
        {
            var original = r.sharedMaterials;
            var mats = MaterialsFor(skin, original);
            r.sharedMaterials = mats;
            _applied[r.GetInstanceID()] = new Applied { Mats = mats, Original = original, Skin = skin, Renderer = r };
        }

        private static void Restore(int key)
        {
            if (!_applied.TryGetValue(key, out var a)) return;
            _applied.Remove(key);
            if (a.Renderer && a.Original != null) a.Renderer.sharedMaterials = a.Original;
            if (a.Mats != null) foreach (var m in a.Mats) if (m) Object.Destroy(m);
            if (a.Extra) Object.Destroy(a.Extra);
        }

        // ------------------------------------------------------------------ diamond studs

        private static Material _gemMaterial;

        private static Material GemMaterial()
        {
            if (_gemMaterial) return _gemMaterial;
            var shader = Arena.ArenaMaterials.LitShader;
            var m = new Material(shader ? shader : Shader.Find("Sprites/Default")) { name = "HTF1v1_Gem" };
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, new Color(0.9f, 0.98f, 1f));
            if (m.HasProperty(MetallicId)) m.SetFloat(MetallicId, 0.85f);
            if (m.HasProperty(SmoothnessId)) m.SetFloat(SmoothnessId, 1f);
            m.EnableKeyword("_EMISSION");
            if (m.HasProperty(EmissionId)) m.SetColor(EmissionId, new Color(0.45f, 0.75f, 1f) * 0.6f);
            _gemMaterial = m;
            return m;
        }

        /// <summary>
        /// Covers a gun part with small faceted gems, Black Ops 2 diamond style: one gem per sampled surface point, sitting
        /// on the surface along its normal, all merged into one mesh in a child object (so it costs one draw call and the
        /// killcam copies it like any other part).
        /// </summary>
        public static GameObject AddStuds(Renderer r, List<Object> track)
        {
            if (!r) return null;
            Mesh src = null; bool owned = false;
            if (r is SkinnedMeshRenderer smr) { src = new Mesh(); try { smr.BakeMesh(src); owned = true; } catch (System.Exception) { Object.Destroy(src); src = null; } }
            else { var mf = r.GetComponent<MeshFilter>(); src = mf ? mf.sharedMesh : null; }
            if (!src) return null;
            bool ok = TryReadPositions(src, out var verts, out var norms);
            Bounds srcBounds = src.bounds;
            if (owned) Object.Destroy(src);
            if (!ok || verts == null || verts.Length == 0) return null;
            if (norms == null || norms.Length != verts.Length)
            {
                // No usable normals: point the gems away from the part's centre.
                norms = new Vector3[verts.Length];
                for (int i = 0; i < norms.Length; i++) { var d = verts[i] - srcBounds.center; norms[i] = d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.up; }
            }

            // The renderer's world scale: gems must be a fixed size in the world, and the child inherits the scale.
            Vector3 sc = r is SkinnedMeshRenderer ? Vector3.one : r.transform.lossyScale;
            sc = new Vector3(Mathf.Max(1e-4f, Mathf.Abs(sc.x)), Mathf.Max(1e-4f, Mathf.Abs(sc.y)), Mathf.Max(1e-4f, Mathf.Abs(sc.z)));
            const float Spacing = 0.011f;   // world metres between gems
            const int MaxGems = 420;
            var cells = new HashSet<(int, int, int)>();
            var picked = new List<int>();
            for (int i = 0; i < verts.Length && picked.Count < MaxGems; i++)
            {
                var wv = Vector3.Scale(verts[i], sc);
                var key = (Mathf.FloorToInt(wv.x / Spacing), Mathf.FloorToInt(wv.y / Spacing), Mathf.FloorToInt(wv.z / Spacing));
                if (cells.Add(key)) picked.Add(i);
            }
            if (picked.Count == 0) return null;

            const float Radius = 0.0038f, Height = 0.0045f;
            var v = new List<Vector3>(picked.Count * 6);
            var n = new List<Vector3>(picked.Count * 6);
            var tri = new List<int>(picked.Count * 24);
            foreach (int i in picked)
            {
                Vector3 p = verts[i];
                Vector3 nn = norms[i].sqrMagnitude > 0.001f ? norms[i].normalized : Vector3.up;
                Vector3 t = Vector3.Cross(nn, Mathf.Abs(nn.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 b = Vector3.Cross(nn, t);
                // Convert world-size offsets into this mesh's (scaled) local space.
                Vector3 L(Vector3 world) => new Vector3(world.x / sc.x, world.y / sc.y, world.z / sc.z);
                Vector3 tip = p + L(nn * Height);
                Vector3 baseC = p + L(nn * 0.0008f);
                int i0 = v.Count;
                v.Add(tip); n.Add(nn);
                for (int k = 0; k < 4; k++)
                {
                    float ang = k * Mathf.PI / 2f + Mathf.PI / 4f;
                    Vector3 rim = p + L((t * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * Radius + nn * 0.0015f);
                    v.Add(rim); n.Add((rim - baseC).normalized * 0.5f + nn * 0.7f);
                }
                v.Add(baseC); n.Add(nn);
                for (int k = 0; k < 4; k++)
                {
                    int a = i0 + 1 + k, c = i0 + 1 + (k + 1) % 4;
                    tri.Add(i0); tri.Add(c); tri.Add(a);          // crown facet
                    tri.Add(i0 + 5); tri.Add(a); tri.Add(c);      // girdle underside (keeps it solid from grazing angles)
                }
            }
            var mesh = new Mesh { name = "HTF1v1_Gems" };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            track?.Add(mesh);

            var go = new GameObject("HTF1v1_Gems");
            go.layer = r.gameObject.layer;
            go.transform.SetParent(r.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = GemMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (track == null) go.AddComponent<MeshOwner>().Mesh = mesh;   // destroyed with the object
            return go;
        }

        /// <summary>
        /// Vertex positions and normals of a mesh. Game meshes are usually not CPU-readable, so when the plain read fails the
        /// vertex buffer is read back from the GPU and decoded from the mesh's attribute layout.
        /// </summary>
        private static bool TryReadPositions(Mesh m, out Vector3[] verts, out Vector3[] norms)
        {
            verts = null; norms = null;
            try
            {
                if (m.isReadable)
                {
                    verts = m.vertices; norms = m.normals;
                    if (verts != null && verts.Length > 0) return true;
                }
            }
            catch (System.Exception) { }
            try
            {
                var attrs = m.GetVertexAttributes();
                int count = m.vertexCount;
                if (count == 0) return false;
                UnityEngine.Rendering.VertexAttributeDescriptor pos = default, nrm = default;
                bool hasPos = false, hasNrm = false;
                foreach (var a in attrs)
                {
                    if (a.attribute == UnityEngine.Rendering.VertexAttribute.Position) { pos = a; hasPos = true; }
                    if (a.attribute == UnityEngine.Rendering.VertexAttribute.Normal) { nrm = a; hasNrm = true; }
                }
                if (!hasPos) return false;
                m.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
                var streams = new Dictionary<int, byte[]>();
                byte[] Stream(int idx)
                {
                    if (streams.TryGetValue(idx, out var d)) return d;
                    int stride = m.GetVertexBufferStride(idx);
                    using (var vb = m.GetVertexBuffer(idx))
                    {
                        d = new byte[stride * count];
                        vb.GetData(d);
                    }
                    streams[idx] = d;
                    return d;
                }
                verts = Decode(Stream(pos.stream), m.GetVertexBufferStride(pos.stream), AttrOffset(m, attrs, pos), pos.format, pos.dimension, count);
                if (hasNrm) norms = Decode(Stream(nrm.stream), m.GetVertexBufferStride(nrm.stream), AttrOffset(m, attrs, nrm), nrm.format, nrm.dimension, count);
                return verts != null && verts.Length == count;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogDebug("Mesh readback failed for " + m.name + ": " + e.Message);
                return false;
            }
        }

        /// <summary>Byte offset of an attribute inside its stream: the sum of the sizes of the attributes before it in that stream.</summary>
        private static int AttrOffset(Mesh m, UnityEngine.Rendering.VertexAttributeDescriptor[] attrs, UnityEngine.Rendering.VertexAttributeDescriptor target)
        {
            int off = 0;
            foreach (var a in attrs)
            {
                if (a.stream != target.stream) continue;
                if (a.attribute == target.attribute) return off;
                off += FormatSize(a.format) * a.dimension;
            }
            return off;
        }

        private static int FormatSize(UnityEngine.Rendering.VertexAttributeFormat f)
        {
            switch (f)
            {
                case UnityEngine.Rendering.VertexAttributeFormat.Float32: case UnityEngine.Rendering.VertexAttributeFormat.UInt32: case UnityEngine.Rendering.VertexAttributeFormat.SInt32: return 4;
                case UnityEngine.Rendering.VertexAttributeFormat.Float16: case UnityEngine.Rendering.VertexAttributeFormat.UNorm16: case UnityEngine.Rendering.VertexAttributeFormat.SNorm16:
                case UnityEngine.Rendering.VertexAttributeFormat.UInt16: case UnityEngine.Rendering.VertexAttributeFormat.SInt16: return 2;
                default: return 1;
            }
        }

        private static Vector3[] Decode(byte[] data, int stride, int offset, UnityEngine.Rendering.VertexAttributeFormat f, int dim, int count)
        {
            var outp = new Vector3[count];
            int size = FormatSize(f);
            for (int i = 0; i < count; i++)
            {
                int b = i * stride + offset;
                float x = Read(data, b, f), y = dim > 1 ? Read(data, b + size, f) : 0f, z = dim > 2 ? Read(data, b + size * 2, f) : 0f;
                outp[i] = new Vector3(x, y, z);
            }
            return outp;
        }

        private static float Read(byte[] d, int at, UnityEngine.Rendering.VertexAttributeFormat f)
        {
            switch (f)
            {
                case UnityEngine.Rendering.VertexAttributeFormat.Float32: return System.BitConverter.ToSingle(d, at);
                case UnityEngine.Rendering.VertexAttributeFormat.Float16: return Mathf.HalfToFloat(System.BitConverter.ToUInt16(d, at));
                case UnityEngine.Rendering.VertexAttributeFormat.SNorm16: return Mathf.Max(-1f, System.BitConverter.ToInt16(d, at) / 32767f);
                case UnityEngine.Rendering.VertexAttributeFormat.UNorm16: return System.BitConverter.ToUInt16(d, at) / 65535f;
                case UnityEngine.Rendering.VertexAttributeFormat.SNorm8: return Mathf.Max(-1f, (sbyte)d[at] / 127f);
                case UnityEngine.Rendering.VertexAttributeFormat.UNorm8: return d[at] / 255f;
                case UnityEngine.Rendering.VertexAttributeFormat.SInt32: return System.BitConverter.ToInt32(d, at);
                case UnityEngine.Rendering.VertexAttributeFormat.UInt32: return System.BitConverter.ToUInt32(d, at);
                case UnityEngine.Rendering.VertexAttributeFormat.SInt16: return System.BitConverter.ToInt16(d, at);
                case UnityEngine.Rendering.VertexAttributeFormat.UInt16: return System.BitConverter.ToUInt16(d, at);
                default: return d[at];
            }
        }

        /// <summary>Frees a generated mesh when its object goes away.</summary>
        private sealed class MeshOwner : MonoBehaviour
        {
            public Mesh Mesh;
            private void OnDestroy() { if (Mesh) Object.Destroy(Mesh); }
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
                    case 3: c = Defs[3].Glow * (2.0f + 1.5f * Mathf.Abs(Mathf.Sin(t * 3f))); break;                  // magma pulse
                    case 7: c = Color.HSVToRGB((t * 0.25f) % 1f, 1f, 1f) * 2.5f; break;                              // rainbow cycle
                    case 6: c = Defs[6].Glow * (1.2f + 0.8f * Mathf.Abs(Mathf.Sin(t * 1.5f))); break;                // ghost breathe
                    case Diamond: c = Defs[Diamond].Glow * (0.6f + 0.5f * Mathf.Abs(Mathf.Sin(t * 2.2f))); break;    // sparkle
                    case Galaxy: c = Color.Lerp(Defs[Galaxy].Glow, new Color(0.2f, 0.6f, 1f), 0.5f + 0.5f * Mathf.Sin(t * 0.8f)) * 1.4f; break;
                    case Dragon: c = Defs[Dragon].Glow * (0.8f + 0.8f * Mathf.Abs(Mathf.Sin(t * 4f)) + 0.4f * Mathf.PerlinNoise(t * 6f, 0.3f)); break;   // ember flicker
                    default: continue;
                }
                foreach (var m in a.Mats) if (m && m.HasProperty(EmissionId)) m.SetColor(EmissionId, c);
            }
        }

        // ------------------------------------------------------------------ dragon

        private static void EnsureDragon(Item item)
        {
            if (_dragons.TryGetValue(item, out var go) && go) return;
            Transform fp = null;
            try { if (item is Weapon w && w.Attachments) fp = w.Attachments.FirePoint; } catch (System.Exception) { }
            if (!fp) return;
            go = BuildDragonHead(fp, true);
            _dragons[item] = go;
        }

        private static void RemoveDragon(Item item)
        {
            if (_dragons.TryGetValue(item, out var go)) { if (go) Object.Destroy(go); _dragons.Remove(item); }
        }

        private static void RemoveAllDragons()
        {
            foreach (var go in _dragons.Values) if (go) Object.Destroy(go);
            _dragons.Clear();
        }

        /// <summary>A shot from a dragon-skinned gun: a big gout of flame out of the mouth.</summary>
        public static void OnShot(Weapon w)
        {
            if (!w || !_dragons.TryGetValue(w, out var go) || !go) return;
            var ps = go.GetComponentInChildren<ParticleSystem>();
            if (!ps) return;
            var ep = new ParticleSystem.EmitParams { startSize = 0.16f, startLifetime = 0.45f, startColor = new Color(1f, 0.75f, 0.2f) };
            for (int i = 0; i < 28; i++)
            {
                var dir = go.transform.forward + go.transform.right * Random.Range(-0.18f, 0.18f) + go.transform.up * Random.Range(-0.12f, 0.16f);
                ep.velocity = dir.normalized * Random.Range(4f, 8f);
                ep.startSize = Random.Range(0.1f, 0.22f);
                ps.Emit(ep, 1);
            }
        }

        /// <summary>The dragon head: built from primitives on the muzzle, mouth open along the barrel, fire pouring out.</summary>
        public static GameObject BuildDragonHead(Transform firePoint, bool withFire, List<Object> track = null)
        {
            var root = new GameObject("HTF1v1_DragonHead");
            root.transform.SetParent(firePoint, false);
            root.transform.localPosition = new Vector3(0f, -0.005f, -0.06f);
            root.transform.localRotation = Quaternion.identity;
            var shader = Arena.ArenaMaterials.LitShader;
            var scale = new Material(shader ? shader : Shader.Find("Sprites/Default")) { name = "HTF1v1_DragonScale" };
            var scaleTex = TextureFor(Dragon);
            if (scale.HasProperty(BaseMapId)) scale.SetTexture(BaseMapId, scaleTex);
            if (scale.HasProperty(BaseColorId)) scale.SetColor(BaseColorId, new Color(0.75f, 0.12f, 0.06f));
            if (scale.HasProperty(MetallicId)) scale.SetFloat(MetallicId, 0.35f);
            if (scale.HasProperty(SmoothnessId)) scale.SetFloat(SmoothnessId, 0.55f);
            scale.EnableKeyword("_EMISSION");
            if (scale.HasProperty(EmissionId)) scale.SetColor(EmissionId, new Color(0.9f, 0.25f, 0.02f) * 0.5f);
            var eye = new Material(shader ? shader : Shader.Find("Sprites/Default")) { name = "HTF1v1_DragonEye" };
            if (eye.HasProperty(BaseColorId)) eye.SetColor(BaseColorId, new Color(1f, 0.9f, 0.3f));
            eye.EnableKeyword("_EMISSION");
            if (eye.HasProperty(EmissionId)) eye.SetColor(EmissionId, new Color(1f, 0.8f, 0.2f) * 3f);
            var horn = new Material(shader ? shader : Shader.Find("Sprites/Default")) { name = "HTF1v1_DragonHorn" };
            if (horn.HasProperty(BaseColorId)) horn.SetColor(BaseColorId, new Color(0.12f, 0.08f, 0.06f));
            if (horn.HasProperty(SmoothnessId)) horn.SetFloat(SmoothnessId, 0.5f);
            track?.Add(scale); track?.Add(eye); track?.Add(horn);

            Part(root, PrimitiveType.Sphere, "Skull", new Vector3(0f, 0.01f, 0f), Vector3.zero, new Vector3(0.11f, 0.095f, 0.15f), scale);
            Part(root, PrimitiveType.Sphere, "Snout", new Vector3(0f, -0.005f, 0.105f), Vector3.zero, new Vector3(0.085f, 0.06f, 0.16f), scale);
            Part(root, PrimitiveType.Cube, "Jaw", new Vector3(0f, -0.05f, 0.1f), new Vector3(16f, 0f, 0f), new Vector3(0.07f, 0.022f, 0.15f), scale);
            Part(root, PrimitiveType.Sphere, "BrowL", new Vector3(-0.038f, 0.045f, 0.045f), Vector3.zero, new Vector3(0.04f, 0.025f, 0.05f), scale);
            Part(root, PrimitiveType.Sphere, "BrowR", new Vector3(0.038f, 0.045f, 0.045f), Vector3.zero, new Vector3(0.04f, 0.025f, 0.05f), scale);
            Part(root, PrimitiveType.Sphere, "EyeL", new Vector3(-0.036f, 0.032f, 0.062f), Vector3.zero, new Vector3(0.024f, 0.024f, 0.024f), eye);
            Part(root, PrimitiveType.Sphere, "EyeR", new Vector3(0.036f, 0.032f, 0.062f), Vector3.zero, new Vector3(0.024f, 0.024f, 0.024f), eye);
            Part(root, PrimitiveType.Cylinder, "HornL", new Vector3(-0.04f, 0.07f, -0.03f), new Vector3(-40f, 0f, 25f), new Vector3(0.018f, 0.06f, 0.018f), horn);
            Part(root, PrimitiveType.Cylinder, "HornR", new Vector3(0.04f, 0.07f, -0.03f), new Vector3(-40f, 0f, -25f), new Vector3(0.018f, 0.06f, 0.018f), horn);
            for (int i = 0; i < 3; i++)
                Part(root, PrimitiveType.Cube, "Spike" + i, new Vector3(0f, 0.055f - i * 0.004f, -0.005f - i * 0.03f), new Vector3(-30f, 0f, 45f), new Vector3(0.018f, 0.03f, 0.018f), horn);
            for (int i = 0; i < 4; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                Part(root, PrimitiveType.Cube, "Tooth" + i, new Vector3(side * 0.025f, -0.032f, 0.14f + (i / 2) * 0.02f), new Vector3(0f, 0f, 45f), new Vector3(0.008f, 0.008f, 0.008f), eye);
            }
            if (withFire) BuildFire(root.transform, new Vector3(0f, -0.02f, 0.19f));
            return root;
        }

        private static void Part(GameObject root, PrimitiveType type, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = "HTF1v1_" + name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.layer = root.layer;
        }

        /// <summary>A small, constant lick of flame out of the mouth; the shot burst is emitted on top of it.</summary>
        private static void BuildFire(Transform parent, Vector3 localPos)
        {
            var go = new GameObject("HTF1v1_DragonFire");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.08f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.3f), new Color(1f, 0.45f, 0.08f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            main.gravityModifier = -0.15f;
            var em = ps.emission;
            em.rateOverTime = 45f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 9f;
            shape.radius = 0.008f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.95f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.5f, 0.05f), 0.45f), new GradientColorKey(new Color(0.6f, 0.08f, 0.02f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.1f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Arena.ArenaMaterials.LitShader;
            var mat = new Material(shader) { name = "HTF1v1_Fire" };
            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, new Color(1f, 0.6f, 0.15f));
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);   // transparent
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 1f);       // additive
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty(EmissionId)) mat.SetColor(EmissionId, new Color(1f, 0.5f, 0.1f) * 3f);
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }

        // ------------------------------------------------------------------ generated patterns

        private static Texture2D TextureFor(byte skin)
        {
            if (_textures.TryGetValue(skin, out var t) && t) return t;
            var d = Defs[Mathf.Clamp(skin, 0, Defs.Length - 1)];
            t = d.Tex != null ? d.Tex() : null;
            if (t) { t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Bilinear; _textures[skin] = t; }
            return t;
        }

        private const int N = 256;

        private static Texture2D Make(System.Func<int, int, Color> f)
        {
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true);
            var px = new Color[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) px[y * N + x] = f(x, y);
            t.SetPixels(px); t.Apply();
            return t;
        }

        private static float Hash(int x, int y, int seed) { unchecked { int h = x * 374761393 + y * 668265263 + seed * 1274126177; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xFFFF) / 65535f; } }

        /// <summary>Tiling value noise in [0,1].</summary>
        private static float Noise(float x, float y, int cells, int seed)
        {
            float fx = x * cells / N, fy = y * cells / N;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
            float a = Hash(x0 % cells, y0 % cells, seed), b = Hash((x0 + 1) % cells, y0 % cells, seed);
            float c = Hash(x0 % cells, (y0 + 1) % cells, seed), d = Hash((x0 + 1) % cells, (y0 + 1) % cells, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        /// <summary>Tiling Voronoi: nearest and second-nearest feature distance plus the nearest cell's random value.</summary>
        private static void Voronoi(int x, int y, int cells, int seed, out float d1, out float d2, out float id)
        {
            float cs = (float)N / cells;
            int cx = Mathf.FloorToInt(x / cs), cy = Mathf.FloorToInt(y / cs);
            d1 = d2 = float.MaxValue; id = 0f;
            for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    int gx = cx + ox, gy = cy + oy;
                    int wx = ((gx % cells) + cells) % cells, wy = ((gy % cells) + cells) % cells;
                    float px = (gx + Hash(wx, wy, seed)) * cs, py = (gy + Hash(wx, wy, seed + 7)) * cs;
                    float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                    if (d < d1) { d2 = d1; d1 = d; id = Hash(wx, wy, seed + 13); }
                    else if (d < d2) d2 = d;
                }
        }

        /// <summary>Black Ops 2 style diamond: bright faceted gems, icy blue and white, with sharp edges.</summary>
        private static Texture2D DiamondTex() => Make((x, y) =>
        {
            Voronoi(x, y, 9, 21, out float d1, out float d2, out float id);
            float edge = Mathf.Clamp01((d2 - d1) / 2.2f);
            float facet = 0.55f + 0.45f * id;
            Color gem = Color.Lerp(new Color(0.55f, 0.8f, 1f), Color.white, facet);
            Color c = Color.Lerp(Color.white, gem, edge);
            float sparkle = Hash(x, y, 3) > 0.995f ? 0.35f : 0f;
            return c + new Color(sparkle, sparkle, sparkle);
        });

        /// <summary>Carbon fibre weave: alternating diagonal tiles.</summary>
        private static Texture2D CarbonTex() => Make((x, y) =>
        {
            int tile = 16;
            bool alt = ((x / tile) + (y / tile)) % 2 == 0;
            float stripe = alt ? ((x + y) % 4 < 2 ? 1f : 0f) : ((x - y + N) % 4 < 2 ? 1f : 0f);
            float v = 0.10f + 0.10f * stripe + 0.04f * Noise(x, y, 8, 5);
            return new Color(v, v, v + 0.01f);
        });

        /// <summary>Deep space: purple and blue nebula with scattered stars.</summary>
        private static Texture2D GalaxyTex() => Make((x, y) =>
        {
            float n = 0.6f * Noise(x, y, 4, 11) + 0.3f * Noise(x, y, 9, 12) + 0.1f * Noise(x, y, 20, 13);
            Color c = Color.Lerp(new Color(0.06f, 0.02f, 0.14f), new Color(0.35f, 0.12f, 0.6f), n);
            c = Color.Lerp(c, new Color(0.1f, 0.35f, 0.75f), Mathf.Clamp01((Noise(x, y, 6, 14) - 0.55f) * 3f));
            float star = Hash(x, y, 17);
            if (star > 0.992f) c = Color.white * Mathf.Lerp(0.6f, 1.2f, (star - 0.992f) / 0.008f);
            return c;
        });

        /// <summary>Frost: pale ice with crack lines.</summary>
        private static Texture2D FrostTex() => Make((x, y) =>
        {
            Voronoi(x, y, 7, 31, out float d1, out float d2, out float id);
            float crack = Mathf.Clamp01((d2 - d1) / 1.6f);
            Color ice = Color.Lerp(new Color(0.78f, 0.9f, 1f), new Color(0.6f, 0.82f, 0.98f), Noise(x, y, 6, 32));
            return Color.Lerp(Color.white, ice, crack);
        });

        /// <summary>Bloodshot: black steel with dried blood splatter.</summary>
        private static Texture2D BloodTex() => Make((x, y) =>
        {
            float n = Noise(x, y, 5, 41) * 0.6f + Noise(x, y, 14, 42) * 0.4f;
            Color steel = new Color(0.13f, 0.12f, 0.12f);
            Color blood = new Color(0.5f, 0.02f, 0.02f);
            float splat = Mathf.Clamp01((n - 0.52f) * 5f);
            return Color.Lerp(steel, blood, splat);
        });

        /// <summary>Dragon scales: overlapping rows of dark red scales with ember edges.</summary>
        private static Texture2D DragonTex() => Make((x, y) =>
        {
            float sx = 24f, sy = 20f;
            int row = Mathf.FloorToInt(y / sy);
            float ox = (row % 2 == 0) ? 0f : sx / 2f;
            float cx = Mathf.Floor((x + ox) / sx) * sx - ox + sx / 2f, cy = row * sy + sy;
            float dx = (x - cx) / (sx * 0.55f), dy = (y - cy) / (sy * 0.9f);
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float edge = Mathf.Clamp01((d - 0.85f) * 6f);
            float shade = 0.65f + 0.35f * Mathf.Clamp01(1f - d) + 0.1f * Noise(x, y, 12, 51);
            Color scale = new Color(0.62f * shade, 0.07f * shade, 0.04f * shade);
            Color ember = new Color(1f, 0.45f, 0.08f);
            return Color.Lerp(scale, ember, edge * 0.8f);
        });
    }
}
