using System.Collections.Generic;
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
    /// Trickshot mode: you spawn on a high perch above a field of bot targets, some standing, some patrolling. Jump off
    /// and land a shot mid-air: a hit ends the match (and the final killcam replays it); touching the ground without one
    /// puts you straight back on the perch for another go. Bots are plain meshes with colliders, recorded for the replay.
    /// </summary>
    public static class Trickshot
    {
        private sealed class Bot { public GameObject Go; public Vector3 A, B; public bool Moving; public float Offset; }

        private const float BotSpeed = 2.6f;
        private const float FallMargin = 2.5f;      // metres below the deck that count as "off the tower"

        private static readonly List<Bot> _bots = new List<Bot>();
        private static readonly List<Object> _created = new List<Object>();
        private static int _builtMap = -1;
        private static bool _falling, _hitThisJump;
        private static int _attempts;
        private static float _lastTeleport = -10f;
        private static Material _skin, _shirtA, _shirtB, _pants;

        public static bool IsMode => ModState.IsActive && ClientMatchView.HasState && MatchModes.IsSolo((MatchMode)ClientMatchView.Latest.Mode);
        public static int Attempts => _attempts;
        public static string Status => _attempts == 0 ? "TRICKSHOT" : $"TRICKSHOT   |   {_attempts} missed";

        public static void Update()
        {
            if (!IsMode || !ArenaBuilder.IsBuilt)
            {
                if (_bots.Count > 0) Clear();
                _attempts = 0; _falling = false;
                return;
            }
            if (_builtMap != ArenaBuilder.MapIndex) BuildBots();
            MoveBots();

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

        /// <summary>Every shot the local player fires: does the aim line meet a bot first?</summary>
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

        /// <summary>A bot was hit (bullet or knife).</summary>
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

        // ------------------------------------------------------------------ bots

        private static void BuildBots()
        {
            Clear();
            _builtMap = ArenaBuilder.MapIndex;
            var layout = ArenaBuilder.Layout;
            var bots = layout.Bots;
            if (bots.Count == 0) return;
            EnsureMaterials();
            int layer = FirstLayer(GameInfo.LevelLayer);
            int i = 0;
            foreach (var b in bots)
            {
                var go = BuildBody(i++, layer);
                var bot = new Bot { Go = go, A = ArenaBuilder.Origin + new Vector3(b.X, 0f, b.Z), B = ArenaBuilder.Origin + new Vector3(b.X2, 0f, b.Z2), Moving = b.Moving, Offset = i * 1.7f };
                go.transform.position = bot.A;
                go.transform.rotation = Quaternion.Euler(0f, ArenaLayout.YawToCenter(b.X, b.Z) + 180f, 0f);
                _bots.Add(bot);
                Recorder.RegisterActor(go.transform);
            }
            Plugin.Log.LogInfo($"Trickshot: {_bots.Count} bots placed");
        }

        private static void MoveBots()
        {
            float t = Time.time;
            foreach (var bot in _bots)
            {
                if (!bot.Go || !bot.Moving) continue;
                float dist = Vector3.Distance(bot.A, bot.B);
                if (dist < 0.01f) continue;
                float k = Mathf.PingPong((t + bot.Offset) * BotSpeed / dist, 1f);
                Vector3 pos = Vector3.Lerp(bot.A, bot.B, k);
                Vector3 dir = pos - bot.Go.transform.position;
                bot.Go.transform.position = pos;
                if (dir.sqrMagnitude > 1e-6f) bot.Go.transform.rotation = Quaternion.Slerp(bot.Go.transform.rotation, Quaternion.LookRotation(dir.normalized, Vector3.up), 0.2f);
            }
        }

        private static void Clear()
        {
            foreach (var b in _bots) if (b.Go) { Recorder.UnregisterActor(b.Go.transform); Object.Destroy(b.Go); }
            _bots.Clear();
            _builtMap = -1;
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

        /// <summary>A simple humanoid target: legs, torso, arms, head, on one capsule collider the guns can hit.</summary>
        private static GameObject BuildBody(int index, int layer)
        {
            var root = new GameObject("HTF1v1_Bot" + index);
            root.layer = layer;
            root.AddComponent<TrickshotBot>();
            var col = root.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.95f, 0f); col.radius = 0.38f; col.height = 1.9f;
            var shirt = index % 2 == 0 ? _shirtA : _shirtB;
            Part(root, PrimitiveType.Capsule, "LegL", new Vector3(-0.14f, 0.45f, 0f), new Vector3(0.24f, 0.45f, 0.24f), _pants, layer);
            Part(root, PrimitiveType.Capsule, "LegR", new Vector3(0.14f, 0.45f, 0f), new Vector3(0.24f, 0.45f, 0.24f), _pants, layer);
            Part(root, PrimitiveType.Cube, "Torso", new Vector3(0f, 1.15f, 0f), new Vector3(0.52f, 0.62f, 0.3f), shirt, layer);
            Part(root, PrimitiveType.Capsule, "ArmL", new Vector3(-0.36f, 1.12f, 0f), new Vector3(0.16f, 0.32f, 0.16f), shirt, layer);
            Part(root, PrimitiveType.Capsule, "ArmR", new Vector3(0.36f, 1.12f, 0f), new Vector3(0.16f, 0.32f, 0.16f), shirt, layer);
            Part(root, PrimitiveType.Sphere, "Head", new Vector3(0f, 1.66f, 0f), new Vector3(0.32f, 0.34f, 0.32f), _skin, layer);
            Part(root, PrimitiveType.Cube, "Cap", new Vector3(0f, 1.8f, 0.02f), new Vector3(0.34f, 0.08f, 0.36f), _pants, layer);
            return root;
        }

        private static void Part(GameObject root, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat, int layer)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.layer = layer;
            Object.Destroy(go.GetComponent<Collider>());   // one collider on the root is enough
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pos;
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
