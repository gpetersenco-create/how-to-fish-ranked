using System.Collections.Generic;
using System.Linq;
using HowToFish1v1.Core;
using HowToFish1v1.UI;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// A charm hanging off the left side of every held gun: the owner's rank emblem on a small card at the end of a short chain,
    /// swinging with real pendulum physics as the gun moves. One account gets a pink DEV tag instead. Charms live under the
    /// item, so every client sees them and killcam replays record them like any other part of the gun.
    /// </summary>
    public static class RankCharms
    {
        private sealed class Charm
        {
            public GameObject Root;
            public Transform Chain, Card;
            public Item Item;
            public int Tier;
            public bool Dev;
            public Transform Muzzle;
            public GameObject LayerProbe;
            public Vector3 Pos, Prev;
            public bool Init;
        }

        private const float Length = 0.07f;
        /// <summary>Only the mod author's account may wear the DEV tag.</summary>
        public static bool CanUseDev => RankService.LocalId == WeaponSkins.DragonOwner.ToString();
        private static readonly Dictionary<Item, Charm> _charms = new Dictionary<Item, Charm>();
        private static readonly Dictionary<int, Material> _tierMats = new Dictionary<int, Material>();
        private static Material _devMat, _chainMat;
        private static Texture2D _devTex;
        private static float _nextScan;

        public static void Update()
        {
            if (!ModState.IsActive)
            {
                if (_charms.Count > 0) Clear();
                return;
            }
            if (Time.unscaledTime >= _nextScan) { _nextScan = Time.unscaledTime + 0.4f; Scan(); }
            Simulate();
        }

        private static void Clear()
        {
            foreach (var c in _charms.Values) if (c.Root) Object.Destroy(c.Root);
            _charms.Clear();
        }

        private static void Scan()
        {
            var live = new HashSet<Item>();
            foreach (var p in PlayerManager.Players)
            {
                if (!p || !p.Holding) continue;
                var item = p.Holding.HeldItem;
                if (!item || !(item is Weapon)) continue;
                live.Add(item);
                int tier = 0; byte choice = 1;
                foreach (var e in ClientMatchView.Players) if (e.Id == p.OwnerId) { tier = RankService.Ladder.TierIndex(e.RankPoints); choice = e.Charm; break; }
                bool dev = false;
                if (choice == 2) { try { dev = p._steamID.Value == WeaponSkins.DragonOwner; } catch (System.Exception) { } if (!dev) choice = 1; }
                if (choice == 0) { if (_charms.TryGetValue(item, out var old) && old.Root) Object.Destroy(old.Root); _charms.Remove(item); continue; }
                if (_charms.TryGetValue(item, out var c) && c.Root && c.Tier == tier && c.Dev == dev) continue;
                if (c != null && c.Root) Object.Destroy(c.Root);
                _charms[item] = Build(item, tier, dev);
            }
            foreach (var item in _charms.Keys.ToList())
                if (!item || !live.Contains(item)) { if (_charms[item].Root) Object.Destroy(_charms[item].Root); _charms.Remove(item); }
        }

        /// <summary>Verlet pendulum per charm: the card trails behind the gun's motion and settles under gravity.</summary>
        private static void Simulate()
        {
            float dt = Mathf.Clamp(Time.deltaTime, 0.001f, 0.05f);
            foreach (var c in _charms.Values)
            {
                if (!c.Root || !c.Item) continue;
                var root = c.Item.transform;
                Vector3 anchor = Anchor(c, root);
                if (c.LayerProbe && c.Root.layer != c.LayerProbe.layer) SetLayer(c.Root, c.LayerProbe.layer);
                if (!c.Init) { c.Pos = anchor + Vector3.down * Length; c.Prev = c.Pos; c.Init = true; }
                Vector3 vel = (c.Pos - c.Prev) * 0.965f;
                if (vel.magnitude > 0.08f) vel = vel.normalized * 0.08f;    // a teleport must not fling it across the map
                c.Prev = c.Pos;
                c.Pos += vel + Vector3.down * (6.5f * dt * dt);
                Vector3 d = c.Pos - anchor;
                float dist = d.magnitude;
                if (dist < 1e-5f) { d = Vector3.down; dist = 1f; }
                c.Pos = anchor + d / dist * Length;
                Vector3 down = (c.Pos - anchor) / Length;
                // The card faces out from the gun's side, kept perpendicular to the chain.
                Vector3 n = Vector3.ProjectOnPlane(-root.right, down);
                if (n.sqrMagnitude < 1e-4f) n = Vector3.ProjectOnPlane(root.forward, down);
                var rot = Quaternion.LookRotation(n.normalized, -down);
                c.Root.transform.SetPositionAndRotation(anchor, rot);
                var ls = root.lossyScale;
                var want = new Vector3(1f / Mathf.Max(1e-4f, Mathf.Abs(ls.x)), 1f / Mathf.Max(1e-4f, Mathf.Abs(ls.y)), 1f / Mathf.Max(1e-4f, Mathf.Abs(ls.z)));
                if ((c.Root.transform.localScale - want).sqrMagnitude > 1e-6f) c.Root.transform.localScale = want;
            }
        }

        /// <summary>
        /// Where the chain hangs from: a point on the line from the grip (the item root) to the muzzle, a little over
        /// half way along, pushed to the gun's left and slightly down. Works in first person, in other players' hands and
        /// while the gun is still being picked up, because it never depends on mesh bounds.
        /// </summary>
        private static Vector3 Anchor(Charm c, Transform root)
        {
            Vector3 muzzle = c.Muzzle ? c.Muzzle.position : root.position + root.forward * 0.6f;
            Vector3 axis = muzzle - root.position;
            float len = axis.magnitude;
            if (len < 0.05f) { axis = root.forward; len = 0.6f; muzzle = root.position + axis * len; }
            axis /= len;
            Vector3 left = Vector3.Cross(root.up, axis);
            if (left.sqrMagnitude < 1e-4f) left = -root.right;
            left.Normalize();
            Vector3 down = Vector3.Cross(axis, left).normalized;
            if (Vector3.Dot(down, Vector3.up) > 0f) down = -down;
            return root.position + axis * (len * 0.55f) + left * 0.045f + down * 0.035f;
        }

        private static void SetLayer(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        private static Charm Build(Item item, int tier, bool dev)
        {
            var root = new GameObject("HTF1v1_Charm");
            root.transform.SetParent(item.transform, false);
            var c = new Charm { Root = root, Item = item, Tier = tier, Dev = dev };

            // The anchor is recomputed every frame (see Anchor): guns are built the moment they spawn, before they are in hand.
            c.Muzzle = null;
            try { if (item is Weapon w && w.Attachments) c.Muzzle = w.Attachments.FirePoint; } catch (System.Exception) { }
            var probe = item.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r && !r.name.StartsWith("HTF1v1_"));
            c.LayerProbe = probe ? probe.gameObject : item.gameObject;
            root.layer = c.LayerProbe.layer;
            string dbg = $"root {item.transform.position} muzzle {(c.Muzzle ? c.Muzzle.position.ToString() : "none")}";
            EnsureMaterials();
            var ring = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Prep(ring, "HTF1v1_CharmRing", root, new Vector3(0f, 0f, 0f), new Vector3(0.008f, 0.008f, 0.008f), _chainMat);
            var chain = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Prep(chain, "HTF1v1_CharmChain", root, new Vector3(0f, -Length * 0.5f, 0f), new Vector3(0.0035f, Length * 0.5f, 0.0035f), _chainMat);
            c.Chain = chain.transform;
            var card = GameObject.CreatePrimitive(PrimitiveType.Cube);
            float cw = dev ? 0.06f : 0.046f, ch = dev ? 0.036f : 0.052f;
            Prep(card, "HTF1v1_CharmCard", root, new Vector3(0f, -Length - ch * 0.5f, 0f), new Vector3(cw, ch, 0.004f), dev ? _devMat : TierMaterial(tier));
            c.Card = card.transform;
            Plugin.Log.LogInfo($"Charm built on {LoadoutService.DisplayName(item)} (tier {tier}{(dev ? ", DEV" : "")}) layer {LayerMask.LayerToName(root.layer)} {dbg}");
            return c;
        }

        private static void Prep(GameObject go, string name, GameObject parent, Vector3 pos, Vector3 scale, Material mat)
        {
            go.name = name;
            go.layer = parent.layer;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = scale;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static void EnsureMaterials()
        {
            if (_chainMat) return;
            var shader = Arena.ArenaMaterials.LitShader;
            _chainMat = new Material(shader ? shader : Shader.Find("Sprites/Default")) { name = "HTF1v1_CharmChain" };
            if (_chainMat.HasProperty("_BaseColor")) _chainMat.SetColor("_BaseColor", new Color(0.85f, 0.85f, 0.9f));
            if (_chainMat.HasProperty("_Metallic")) _chainMat.SetFloat("_Metallic", 0.9f);
            if (_chainMat.HasProperty("_Smoothness")) _chainMat.SetFloat("_Smoothness", 0.8f);
            _devTex = DevTexture();
            _devMat = CardMaterial(_devTex, "Dev", new Color(1f, 0.45f, 0.8f) * 1.2f);
        }

        private static Material TierMaterial(int tier)
        {
            if (_tierMats.TryGetValue(tier, out var m) && m) return m;
            m = CardMaterial(RankEmblems.Get(tier), "Tier" + tier, RankEmblems.ColorFor(tier) * 0.35f);
            _tierMats[tier] = m;
            return m;
        }

        private static Material CardMaterial(Texture2D tex, string name, Color glow)
        {
            var shader = Arena.ArenaMaterials.LitShader;
            var m = new Material(shader ? shader : Shader.Find("Sprites/Default")) { name = "HTF1v1_Charm" + name };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            else if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.4f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.75f);
            // Cut out the transparent parts of the emblem so the card takes the shield's shape.
            if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 1f);
            if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.4f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_EMISSION");
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", glow);
            return m;
        }

        /// <summary>A pink tag with "DEV" in bold white block letters.</summary>
        private static Texture2D DevTexture()
        {
            const int W = 192, H = 112;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[W * H];
            Color pink = new Color(1f, 0.42f, 0.78f), edge = new Color(0.55f, 0.1f, 0.4f), white = Color.white;
            // Letters as line segments (in pixel space, origin bottom-left); thickness 11 px.
            var segs = new List<(Vector2 a, Vector2 b)>();
            // D
            segs.Add((new Vector2(30, 28), new Vector2(30, 84)));
            segs.Add((new Vector2(30, 84), new Vector2(50, 84)));
            segs.Add((new Vector2(30, 28), new Vector2(50, 28)));
            for (int i = 0; i < 8; i++)
            {
                float a0 = -Mathf.PI / 2f + Mathf.PI * i / 8f, a1 = -Mathf.PI / 2f + Mathf.PI * (i + 1) / 8f;
                segs.Add((new Vector2(50 + 28 * Mathf.Cos(a0), 56 + 28 * Mathf.Sin(a0)), new Vector2(50 + 28 * Mathf.Cos(a1), 56 + 28 * Mathf.Sin(a1))));
            }
            // E
            segs.Add((new Vector2(96, 28), new Vector2(96, 84)));
            segs.Add((new Vector2(96, 84), new Vector2(128, 84)));
            segs.Add((new Vector2(96, 56), new Vector2(122, 56)));
            segs.Add((new Vector2(96, 28), new Vector2(128, 28)));
            // V
            segs.Add((new Vector2(140, 84), new Vector2(156, 28)));
            segs.Add((new Vector2(156, 28), new Vector2(172, 84)));
            float Seg(Vector2 p, Vector2 a, Vector2 b)
            {
                Vector2 ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                return Vector2.Distance(p, a + ab * t);
            }
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float rx = Mathf.Min(x, W - 1 - x), ry = Mathf.Min(y, H - 1 - y);
                    float corner = Mathf.Min(rx, ry);
                    Color c = corner < 5 ? edge : pink;
                    // rounded corners: cut outside a radius of 14 px
                    float cxr = Mathf.Max(0, 14 - rx), cyr = Mathf.Max(0, 14 - ry);
                    if (cxr * cxr + cyr * cyr > 14 * 14) { px[y * W + x] = Color.clear; continue; }
                    float dmin = float.MaxValue;
                    var p = new Vector2(x, y);
                    foreach (var s in segs) dmin = Mathf.Min(dmin, Seg(p, s.a, s.b));
                    if (dmin < 6f) c = white;
                    else if (dmin < 8f) c = Color.Lerp(white, c, (dmin - 6f) / 2f);
                    // subtle top-lit sheen
                    c = Color.Lerp(c, Color.white, 0.08f * (y / (float)H));
                    c.a = 1f;
                    px[y * W + x] = c;
                }
            tex.SetPixels(px); tex.Apply();
            return tex;
        }
    }
}
