using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using HowToFish1v1.Arena;
using HowToFish1v1.Core;
using HowToFish1v1.Net;
using HowToFish1v1.UI;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>Marks a practice target so shots and knife swings can recognise it.</summary>
    public sealed class TrickshotBot : MonoBehaviour { }

    /// <summary>
    /// Trickshot mode: you spawn on a high perch above a field of targets: player-model bots (some standing, some
    /// patrolling, some hovering in the air) and birds circling overhead. Jump off and land a shot mid-air: a hit ends
    /// the match (and the final killcam replays it); touching the ground without one puts you straight back on the perch.
    /// Bots are copies of the game's own character prefabs with the scripts stripped, so they look like real players
    /// and idle-animate when the prefab carries an animator; primitives are the fallback.
    /// </summary>
    public static class Trickshot
    {
        private sealed class Bot { public GameObject Go; public Vector3 A, B; public bool Moving; public float Offset; }
        private sealed class BirdActor { public GameObject Go; public Vector3 Center; public float Radius, Height, Speed, Phase; }

        private const float BotSpeed = 2.6f;
        private const float FallMargin = 2.5f;      // metres below the deck that count as "off the tower"

        private static readonly List<Bot> _bots = new List<Bot>();
        private static readonly List<BirdActor> _birds = new List<BirdActor>();
        private static readonly List<Object> _created = new List<Object>();
        private static int _builtMap = -1;
        private static bool _falling, _hitThisJump;
        private static int _attempts;
        private static float _lastTeleport = -10f;
        private static Material _skin, _shirtA, _shirtB, _pants;
        private static string _botSource = "";

        public static bool IsMode => ModState.IsActive && ClientMatchView.HasState && MatchModes.IsSolo((MatchMode)ClientMatchView.Latest.Mode);
        public static int Attempts => _attempts;
        public static string Status => _attempts == 0 ? "TRICKSHOT" : $"TRICKSHOT   {_attempts} missed";

        public static void Update()
        {
            if (!IsMode || !ArenaBuilder.IsBuilt)
            {
                if (_bots.Count > 0 || _birds.Count > 0) Clear();
                _attempts = 0; _falling = false;
                return;
            }
            if (_builtMap != ArenaBuilder.MapIndex) BuildTargets();
            MoveBots();
            MoveBirds();

            var me = Player.LocalPlayer;
            if (!me || me.Dying.IsDead) { _falling = false; return; }
            if (ModState.Phase == MatchPhase.Countdown) { _attempts = 0; _falling = false; return; }
            if (ModState.Phase != MatchPhase.Live) { _falling = false; return; }

            var perch = ArenaBuilder.Spawn(Side.Left);
            float deckY = perch.pos.y - 1.6f;
            float y = me.Transform.position.y;
            if (!_falling && y < deckY - FallMargin) { _falling = true; _hitThisJump = false; }
            if (_falling && !_hitThisJump && y < deckY - FallMargin && Grounded(me) && Time.unscaledTime - _lastTeleport > 1f)
            {
                _attempts++;
                Hud.Popup("MISSED");
                Teleport(me, perch);
                _lastTeleport = Time.unscaledTime;
                _falling = false;
            }
        }

        private static bool Grounded(Player me)
        {
            try { return me.Movement && me.Movement.Grounded; } catch (System.Exception) { return false; }
        }

        private static void Teleport(Player me, (Vector3 pos, float yaw) perch)
        {
            if (!ModNet.IsHost) return;   // trickshot is a one-player mode, so the player is always the host
            try { Server.Instance.TeleportPlayer(me, perch.pos, perch.yaw); } catch (System.Exception e) { Plugin.Log.LogWarning("Trickshot teleport: " + e.Message); }
        }

        /// <summary>Every shot the local player fires: does the aim line meet a target first?</summary>
        public static void OnShot(Weapon w)
        {
            if (!IsMode || !w || !w.Holder || w.Holder.Owner == null || !w.Holder.Owner.IsLocalClient) return;
            var cam = w.Holder.CamObject ? w.Holder.CamObject : w.Holder.Transform;
            if (!cam) return;
            int mask = ~0;
            int local = LayerMask.NameToLayer("LocalPlayer");
            if (local >= 0) mask &= ~(1 << local);
            if (Physics.Raycast(cam.position, cam.forward, out var hit, 400f, mask, QueryTriggerInteraction.Ignore)
                && hit.collider && hit.collider.GetComponentInParent<TrickshotBot>())
                RegisterHit();
        }

        /// <summary>A target was hit (bullet or knife).</summary>
        public static void RegisterHit()
        {
            if (!IsMode || ModState.Phase != MatchPhase.Live) return;
            HitSounds.PlayHitmarker(true);
            if (!_falling) { Hud.Popup("JUMP OFF FIRST"); return; }
            if (_hitThisJump) return;
            _hitThisJump = true;
            Hud.Popup("+100");
            Hud.Popup("TRICKSHOT!");
            Plugin.Host.EndTrickshot(_attempts + 1);
        }

        // ------------------------------------------------------------------ targets

        private static void BuildTargets()
        {
            Clear();
            _builtMap = ArenaBuilder.MapIndex;
            var layout = ArenaBuilder.Layout;
            EnsureMaterials();
            int layer = FirstLayer(GameInfo.LevelLayer);
            int i = 0;
            foreach (var b in layout.Bots)
            {
                var go = BuildCharacter(i, layer);
                var bot = new Bot
                {
                    Go = go, Moving = b.Moving, Offset = i * 1.7f,
                    A = ArenaBuilder.Origin + new Vector3(b.X, b.Y, b.Z),
                    B = ArenaBuilder.Origin + new Vector3(b.X2, b.Y2, b.Z2)
                };
                go.transform.position = bot.A;
                go.transform.rotation = Quaternion.Euler(0f, ArenaLayout.YawToCenter(b.X, b.Z) + 180f, 0f);
                _bots.Add(bot);
                Recorder.RegisterActor(go.transform);
                i++;
            }
            foreach (var bd in layout.Birds)
            {
                var go = BuildBird(_birds.Count, layer);
                var bird = new BirdActor { Go = go, Center = ArenaBuilder.Origin + new Vector3(bd.X, 0f, bd.Z), Radius = bd.Radius, Height = bd.Height, Speed = bd.Speed, Phase = bd.Phase };
                _birds.Add(bird);
                Recorder.RegisterActor(go.transform);
            }
            MoveBirds();
            Plugin.Log.LogInfo($"Trickshot: {_bots.Count} bots ({_botSource}) and {_birds.Count} birds placed");
        }

        private static void MoveBots()
        {
            float t = Time.time;
            foreach (var bot in _bots)
            {
                if (!bot.Go) continue;
                if (!bot.Moving)
                {
                    // Hovering bots bob gently so they read as floating, not parked.
                    if (bot.A.y - ArenaBuilder.Origin.y > 1f) bot.Go.transform.position = bot.A + Vector3.up * (0.35f * Mathf.Sin(t * 1.3f + bot.Offset));
                    continue;
                }
                float dist = Vector3.Distance(bot.A, bot.B);
                if (dist < 0.01f) continue;
                float k = Mathf.PingPong((t + bot.Offset) * BotSpeed / dist, 1f);
                Vector3 pos = Vector3.Lerp(bot.A, bot.B, k);
                Vector3 dir = Vector3.ProjectOnPlane(pos - bot.Go.transform.position, Vector3.up);
                bot.Go.transform.position = pos;
                if (dir.sqrMagnitude > 1e-6f) bot.Go.transform.rotation = Quaternion.Slerp(bot.Go.transform.rotation, Quaternion.LookRotation(dir.normalized, Vector3.up), 0.2f);
            }
        }

        private static void MoveBirds()
        {
            float t = Time.time;
            foreach (var b in _birds)
            {
                if (!b.Go) continue;
                float ang = b.Phase + t * b.Speed / Mathf.Max(1f, b.Radius);
                Vector3 pos = b.Center + new Vector3(Mathf.Cos(ang) * b.Radius, b.Height + Mathf.Sin(t * 0.7f + b.Phase) * 1.2f, Mathf.Sin(ang) * b.Radius);
                Vector3 dir = new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                b.Go.transform.position = pos;
                b.Go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, 0f, -18f);   // banked into the turn
            }
        }

        private static void Clear()
        {
            foreach (var b in _bots) if (b.Go) { Recorder.UnregisterActor(b.Go.transform); Object.Destroy(b.Go); }
            foreach (var b in _birds) if (b.Go) { Recorder.UnregisterActor(b.Go.transform); Object.Destroy(b.Go); }
            _bots.Clear(); _birds.Clear();
            _builtMap = -1;
        }

        // ------------------------------------------------------------------ character bodies

        private static GameObject _npcSource, _playerSource, _birdSource;
        private static bool _searched;

        /// <summary>Finds prefab bodies to copy: the game's NPC characters, or the player's third-person body.</summary>
        private static void FindSources()
        {
            if (_searched) return;
            _searched = true;
            try
            {
                foreach (var npc in Resources.FindObjectsOfTypeAll<NPC>())
                {
                    if (!npc || npc.gameObject.scene.IsValid()) continue;   // assets only, not scene objects
                    if (npc.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) continue;
                    if (npc.GetComponentInChildren<Animator>(true) == null && _npcSource) continue;
                    _npcSource = npc.gameObject;
                    if (npc.GetComponentInChildren<Animator>(true) != null) break;
                }
            }
            catch (System.Exception e) { Plugin.Log.LogDebug("NPC prefab search: " + e.Message); }
            try
            {
                foreach (var p in Resources.FindObjectsOfTypeAll<Player>())
                {
                    if (!p || p.gameObject.scene.IsValid()) continue;
                    var others = Traverse.Create(p).Field<List<GameObject>>("_otherObjects").Value;
                    if (others == null) continue;
                    foreach (var o in others)
                        if (o && o.GetComponentInChildren<SkinnedMeshRenderer>(true) != null) { _playerSource = o; break; }
                    if (_playerSource) break;
                }
            }
            catch (System.Exception e) { Plugin.Log.LogDebug("Player prefab search: " + e.Message); }
            try
            {
                foreach (var b in Resources.FindObjectsOfTypeAll<global::Bird>())
                    if (b && !b.gameObject.scene.IsValid() && b.GetComponentInChildren<Renderer>(true) != null) { _birdSource = b.gameObject; break; }
            }
            catch (System.Exception e) { Plugin.Log.LogDebug("Bird prefab search: " + e.Message); }
            Plugin.Log.LogInfo($"Trickshot sources: npc={(_npcSource ? _npcSource.name : "none")} player={(_playerSource ? _playerSource.name : "none")} bird={(_birdSource ? _birdSource.name : "none")}");
        }

        /// <summary>A copy of a character prefab with every script removed: renderers, bones and animators only.</summary>
        private static GameObject CopyBody(GameObject source, string name)
        {
            var copy = Object.Instantiate(source);
            copy.name = name;
            copy.SetActive(true);
            foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true)) if (mb) Object.DestroyImmediate(mb);
            foreach (var rb in copy.GetComponentsInChildren<Rigidbody>(true)) if (rb) Object.DestroyImmediate(rb);
            foreach (var j in copy.GetComponentsInChildren<Joint>(true)) if (j) Object.DestroyImmediate(j);
            foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);
            foreach (var r in copy.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            foreach (var a in copy.GetComponentsInChildren<Animator>(true)) { a.enabled = true; a.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
            return copy;
        }

        private static readonly Vector3[] SkinTones = { new Vector3(0.92f, 0.76f, 0.62f), new Vector3(0.80f, 0.60f, 0.45f), new Vector3(0.55f, 0.36f, 0.25f), new Vector3(0.95f, 0.82f, 0.70f) };
        private static readonly Vector3[] Cloth = { new Vector3(0.85f, 0.2f, 0.15f), new Vector3(0.15f, 0.4f, 0.85f), new Vector3(0.2f, 0.65f, 0.3f), new Vector3(0.9f, 0.75f, 0.2f), new Vector3(0.6f, 0.2f, 0.7f), new Vector3(0.95f, 0.95f, 0.95f) };

        private static GameObject BuildCharacter(int index, int layer)
        {
            FindSources();
            GameObject root = null;
            var rnd = new System.Random(index * 7919 + 13);
            if (_npcSource || _playerSource)
            {
                try
                {
                    // Alternate the two sources when both exist, for more variety.
                    var src = _npcSource && _playerSource ? (index % 3 == 2 ? _playerSource : _npcSource) : (_npcSource ? _npcSource : _playerSource);
                    var body = CopyBody(src, "HTF1v1_BotBody");
                    root = new GameObject("HTF1v1_Bot" + index);
                    body.transform.SetParent(root.transform, false);
                    body.transform.localPosition = Vector3.zero;
                    body.transform.localRotation = Quaternion.identity;
                    // Random colours through the game's own character shader properties.
                    var skin = SkinTones[rnd.Next(SkinTones.Length)];
                    var c1 = Cloth[rnd.Next(Cloth.Length)]; var c2 = Cloth[rnd.Next(Cloth.Length)]; var c3 = Cloth[rnd.Next(Cloth.Length)];
                    foreach (var r in body.GetComponentsInChildren<Renderer>(true))
                    {
                        try
                        {
                            var mats = r.materials;
                            foreach (var m in mats)
                            {
                                if (!m) continue;
                                _created.Add(m);
                                if (m.HasProperty(ShaderManager.PlayerSkinID)) m.SetVector(ShaderManager.PlayerSkinID, skin);
                                if (m.HasProperty(ShaderManager.PlayerPrimaryColorID)) m.SetVector(ShaderManager.PlayerPrimaryColorID, c1);
                                if (m.HasProperty(ShaderManager.PlayerSecondColorID)) m.SetVector(ShaderManager.PlayerSecondColorID, c2);
                                if (m.HasProperty(ShaderManager.PlayerThirdColorID)) m.SetVector(ShaderManager.PlayerThirdColorID, c3);
                            }
                        }
                        catch (System.Exception) { }
                    }
                    _botSource = src == _npcSource ? "npc prefab" : "player prefab";
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning("Trickshot: character copy failed, using dummies: " + e.Message);
                    if (root) Object.Destroy(root);
                    root = null;
                }
            }
            if (!root)
            {
                root = BuildDummy(index, layer);
                _botSource = "dummies";
            }
            root.layer = layer;
            root.AddComponent<TrickshotBot>();
            // One capsule the guns can hit, whatever the body's own colliders do.
            var col = root.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.95f, 0f); col.radius = 0.38f; col.height = 1.9f;
            return root;
        }

        private static GameObject BuildBird(int index, int layer)
        {
            FindSources();
            GameObject root = new GameObject("HTF1v1_Bird" + index);
            root.layer = layer;
            bool made = false;
            if (_birdSource)
            {
                try
                {
                    var body = CopyBody(_birdSource, "HTF1v1_BirdBody");
                    body.transform.SetParent(root.transform, false);
                    body.transform.localPosition = Vector3.zero;
                    body.transform.localRotation = Quaternion.identity;
                    made = true;
                }
                catch (System.Exception e) { Plugin.Log.LogWarning("Trickshot: bird copy failed: " + e.Message); }
            }
            if (!made)
            {
                EnsureMaterials();
                Part(root, PrimitiveType.Capsule, "Body", new Vector3(0f, 0f, 0f), new Vector3(0.25f, 0.22f, 0.25f), _pants, layer, new Vector3(90f, 0f, 0f));
                Part(root, PrimitiveType.Cube, "WingL", new Vector3(-0.45f, 0.05f, 0f), new Vector3(0.8f, 0.03f, 0.3f), _pants, layer, Vector3.zero);
                Part(root, PrimitiveType.Cube, "WingR", new Vector3(0.45f, 0.05f, 0f), new Vector3(0.8f, 0.03f, 0.3f), _pants, layer, Vector3.zero);
                Part(root, PrimitiveType.Sphere, "Head", new Vector3(0f, 0.08f, 0.32f), new Vector3(0.18f, 0.18f, 0.18f), _pants, layer, Vector3.zero);
            }
            root.AddComponent<TrickshotBot>();
            var col = root.AddComponent<SphereCollider>();
            col.radius = 0.55f;
            return root;
        }

        private static void EnsureMaterials()
        {
            if (_skin) return;
            _skin = Solid("Skin", new Color(0.85f, 0.65f, 0.5f), 0.35f);
            _shirtA = Solid("ShirtA", new Color(0.85f, 0.2f, 0.15f), 0.5f);
            _shirtB = Solid("ShirtB", new Color(0.15f, 0.4f, 0.85f), 0.5f);
            _pants = Solid("Pants", new Color(0.12f, 0.12f, 0.15f), 0.4f);
        }

        private static Material Solid(string name, Color c, float gloss)
        {
            var m = new Material(ArenaMaterials.For(BoxKind.White)) { name = "HTF1v1_Bot" + name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", gloss);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            m.DisableKeyword("_NORMALMAP");
            _created.Add(m);
            return m;
        }

        /// <summary>Fallback humanoid from primitives.</summary>
        private static GameObject BuildDummy(int index, int layer)
        {
            var root = new GameObject("HTF1v1_Bot" + index);
            var shirt = index % 2 == 0 ? _shirtA : _shirtB;
            Part(root, PrimitiveType.Capsule, "LegL", new Vector3(-0.14f, 0.45f, 0f), new Vector3(0.24f, 0.45f, 0.24f), _pants, layer, Vector3.zero);
            Part(root, PrimitiveType.Capsule, "LegR", new Vector3(0.14f, 0.45f, 0f), new Vector3(0.24f, 0.45f, 0.24f), _pants, layer, Vector3.zero);
            Part(root, PrimitiveType.Cube, "Torso", new Vector3(0f, 1.15f, 0f), new Vector3(0.52f, 0.62f, 0.3f), shirt, layer, Vector3.zero);
            Part(root, PrimitiveType.Capsule, "ArmL", new Vector3(-0.36f, 1.12f, 0f), new Vector3(0.16f, 0.32f, 0.16f), shirt, layer, Vector3.zero);
            Part(root, PrimitiveType.Capsule, "ArmR", new Vector3(0.36f, 1.12f, 0f), new Vector3(0.16f, 0.32f, 0.16f), shirt, layer, Vector3.zero);
            Part(root, PrimitiveType.Sphere, "Head", new Vector3(0f, 1.66f, 0f), new Vector3(0.32f, 0.34f, 0.32f), _skin, layer, Vector3.zero);
            Part(root, PrimitiveType.Cube, "Cap", new Vector3(0f, 1.8f, 0.02f), new Vector3(0.34f, 0.08f, 0.36f), _pants, layer, Vector3.zero);
            return root;
        }

        private static void Part(GameObject root, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat, int layer, Vector3 euler)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.layer = layer;
            Object.Destroy(go.GetComponent<Collider>());   // one collider on the root is enough
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static int FirstLayer(LayerMask mask)
        {
            int v = mask.value;
            for (int i = 0; i < 32; i++) if ((v & (1 << i)) != 0) return i;
            return 0;
        }
    }
}
