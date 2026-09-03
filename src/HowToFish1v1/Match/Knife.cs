using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// Black Ops 2 style knife on a key: a quick lunge-slash with a knife viewmodel that takes the gun's place for the
    /// swing, a one-hit kill on any player within reach, and a hit on birds and fish too. The damage goes through the
    /// game's own hit path (so the server, the hitmarker and the kill credit all work as for a bullet).
    /// The knife is a local viewmodel only; other players do not see it.
    /// </summary>
    public static class Knife
    {
        private const float SwingSeconds = 0.42f;
        private const float HitAt = 0.38f;          // fraction of the swing at which the blade connects
        private const float Reach = 2.3f;
        private const float Radius = 0.4f;
        private const int Damage = 150;
        private const float Cooldown = 0.55f;

        private static GameObject _model;
        private static Renderer[] _modelRenderers = System.Array.Empty<Renderer>();
        private static readonly List<Object> _created = new List<Object>();
        private static byte _modelSkin = 255;
        private static float _swingStart = -10f;
        private static bool _swinging, _hitDone;
        private static readonly List<Renderer> _hiddenGun = new List<Renderer>();
        private static Item _hiddenItem;

        public static bool Swinging => _swinging;

        public static void Update()
        {
            var me = Player.LocalPlayer;
            if (!ModState.IsActive || !me || me.Dying.IsDead || ModState.PanelOpen || KillCam.Active || MainMenuManager.IsInMenu)
            {
                if (_swinging) EndSwing();
                return;
            }
            if (_swinging) Animate(me);
            else if (Input.GetKeyDown(Plugin.Cfg.KnifeKey.Value) && !me.BlockInputs && Time.unscaledTime - _swingStart > Cooldown) StartSwing(me);
        }

        private static void StartSwing(Player me)
        {
            EnsureModel(me);
            if (!_model) return;
            _swinging = true; _hitDone = false;
            _swingStart = Time.unscaledTime;
            _model.SetActive(true);
            // The gun steps aside for the swing.
            _hiddenItem = me.Holding ? me.Holding.HeldItem : null;
            if (_hiddenItem)
                foreach (var r in _hiddenItem.GetComponentsInChildren<Renderer>(true))
                    if (r && r.enabled) { r.enabled = false; _hiddenGun.Add(r); }
            HitSounds.PlaySwoosh();
        }

        private static void EndSwing()
        {
            _swinging = false;
            if (_model) _model.SetActive(false);
            foreach (var r in _hiddenGun) if (r) r.enabled = true;
            _hiddenGun.Clear();
            _hiddenItem = null;
        }

        /// <summary>The swing: the knife starts low right, lunges up and across to the left with a twist, then snaps back.</summary>
        private static void Animate(Player me)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - _swingStart) / SwingSeconds);
            if (t >= 1f) { EndSwing(); return; }
            var cam = me.CamObject ? me.CamObject : me.Transform;
            // Keyframes in camera space.
            Vector3 p0 = new Vector3(0.32f, -0.30f, 0.45f), p1 = new Vector3(0.10f, -0.06f, 0.62f), p2 = new Vector3(-0.30f, 0.04f, 0.55f);
            Vector3 r0 = new Vector3(20f, -60f, 70f), r1 = new Vector3(-10f, -20f, 20f), r2 = new Vector3(-25f, 40f, -30f);
            Vector3 pos; Vector3 rot;
            if (t < 0.45f)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / 0.45f);           // wind-up to lunge
                pos = Vector3.Lerp(p0, p1, k); rot = Vector3.Lerp(r0, r1, k);
            }
            else if (t < 0.7f)
            {
                float k = (t - 0.45f) / 0.25f;                            // the slash, fast and straight
                pos = Vector3.Lerp(p1, p2, k); rot = Vector3.Lerp(r1, r2, k);
            }
            else
            {
                float k = Mathf.SmoothStep(0f, 1f, (t - 0.7f) / 0.3f);     // return
                pos = Vector3.Lerp(p2, p0, k); rot = Vector3.Lerp(r2, r0, k);
            }
            _model.transform.SetPositionAndRotation(cam.TransformPoint(pos), cam.rotation * Quaternion.Euler(rot));
            foreach (var r in _hiddenGun) if (r && r.enabled) r.enabled = false;   // the game re-enables tool hands each frame
            if (!_hitDone && t >= HitAt) { _hitDone = true; TryHit(me, cam); }
        }

        private static void TryHit(Player me, Transform cam)
        {
            Vector3 fwd = cam.forward;
            RaycastHit[] hits;
            try { hits = Physics.SphereCastAll(cam.position, Radius, fwd, Reach, GameInfo.ProjectileHitLayer, QueryTriggerInteraction.Ignore); }
            catch (System.Exception) { return; }
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (!h.transform) continue;
                Player victim = null;
                try { victim = PlayerManager.GetPlayerFromBodyPart(h.transform); } catch (System.Exception) { }
                if (victim)
                {
                    if (victim == me || victim.Dying.IsDead) continue;
                    Vector3 point = h.point == Vector3.zero ? victim.Transform.position + Vector3.up : h.point;
                    try { victim.Vitals.LocalHit(point, fwd, me, Damage, false, fwd * GameInfo.PlayerKillForce); } catch (System.Exception e) { Plugin.Log.LogWarning("Knife hit: " + e.Message); }
                    return;
                }
                Item item = null;
                try { item = ItemManager.Get(h.transform); } catch (System.Exception) { }
                if (item && item is Creature)
                {
                    try { item.LocalHit(h.transform, h.point, fwd, me, Damage, false, fwd * 4f); } catch (System.Exception) { }
                    return;
                }
                if (h.transform.CompareTag("Level")) return;   // hit a wall first: no reach-through
            }
        }

        /// <summary>The knife viewmodel: blade, guard, handle and pommel from primitives, in the chosen skin.</summary>
        private static void EnsureModel(Player me)
        {
            byte skin = (byte)Mathf.Clamp(Plugin.Cfg.KnifeSkin.Value, 0, WeaponSkins.Count - 1);
            if (!WeaponSkins.CanPick(skin)) skin = 0;
            if (_model && _modelSkin == skin) return;
            if (_model) Object.Destroy(_model);
            foreach (var o in _created) if (o) Object.Destroy(o);
            _created.Clear();
            _model = new GameObject("HTF1v1_Knife");
            var shader = Arena.ArenaMaterials.LitShader;
            var steel = new Material(shader ? shader : Shader.Find("Sprites/Default")) { name = "HTF1v1_KnifeSteel" };
            if (steel.HasProperty("_BaseColor")) steel.SetColor("_BaseColor", new Color(0.82f, 0.84f, 0.88f));
            if (steel.HasProperty("_Metallic")) steel.SetFloat("_Metallic", 0.95f);
            if (steel.HasProperty("_Smoothness")) steel.SetFloat("_Smoothness", 0.9f);
            var grip = new Material(shader ? shader : Shader.Find("Sprites/Default")) { name = "HTF1v1_KnifeGrip" };
            if (grip.HasProperty("_BaseColor")) grip.SetColor("_BaseColor", new Color(0.08f, 0.08f, 0.09f));
            if (grip.HasProperty("_Smoothness")) grip.SetFloat("_Smoothness", 0.4f);
            _created.Add(steel); _created.Add(grip);
            Material bladeMat = steel, gripMat = grip;
            if (skin > 0)
            {
                bladeMat = WeaponSkins.MaterialsFor(skin, new[] { steel }, _created)[0];
                gripMat = WeaponSkins.MaterialsFor(skin, new[] { grip }, _created)[0];
            }
            // Blade: a long flattened box with a tapered tip; guard, grip and pommel behind it. Forward is +z.
            Part(PrimitiveType.Cube, "Blade", new Vector3(0f, 0f, 0.12f), new Vector3(0f, 0f, 0f), new Vector3(0.006f, 0.032f, 0.19f), bladeMat);
            Part(PrimitiveType.Cube, "Tip", new Vector3(0f, 0.006f, 0.225f), new Vector3(0f, 0f, 0f), new Vector3(0.005f, 0.02f, 0.05f), bladeMat);
            Part(PrimitiveType.Cube, "Edge", new Vector3(0f, -0.014f, 0.11f), new Vector3(0f, 0f, 0f), new Vector3(0.0025f, 0.006f, 0.17f), bladeMat);
            Part(PrimitiveType.Cube, "Guard", new Vector3(0f, 0f, 0.015f), new Vector3(0f, 0f, 0f), new Vector3(0.016f, 0.07f, 0.014f), gripMat);
            Part(PrimitiveType.Capsule, "Grip", new Vector3(0f, 0f, -0.05f), new Vector3(90f, 0f, 0f), new Vector3(0.028f, 0.05f, 0.028f), gripMat);
            Part(PrimitiveType.Sphere, "Pommel", new Vector3(0f, 0f, -0.105f), Vector3.zero, new Vector3(0.03f, 0.03f, 0.03f), bladeMat);
            _modelRenderers = _model.GetComponentsInChildren<Renderer>(true);
            _modelSkin = skin;
            _model.SetActive(false);
        }

        private static void Part(PrimitiveType type, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = "HTF1v1_" + name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_model.transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    /// <summary>
    /// Mod sounds made in code (no audio files to ship): the hitmarker "tick" that replaces the game's during matches,
    /// and the knife swoosh. Played through the game's sound mixer group so the volume slider still applies.
    /// </summary>
    public static class HitSounds
    {
        private static AudioSource _source;
        private static AudioClip _hitmarker, _swoosh;
        private const int Rate = 44100;

        private static void Ensure()
        {
            if (_source) return;
            var go = new GameObject("HTF1v1_Sounds");
            Object.DontDestroyOnLoad(go);
            _source = go.AddComponent<AudioSource>();
            _source.spatialBlend = 0f;
            _source.playOnAwake = false;
            _source.priority = 0;
            try
            {
                var inst = Traverse.Create(typeof(AudioManager)).Field<AudioManager>("_instance").Value;
                var global = Traverse.Create(typeof(AudioManager)).Field<AudioSource>("_globalSource").Value;
                if (global) _source.outputAudioMixerGroup = global.outputAudioMixerGroup;
            }
            catch (System.Exception) { }
            _hitmarker = MakeHitmarker();
            _swoosh = MakeSwoosh();
        }

        public static void PlayHitmarker(bool kill = false)
        {
            Ensure();
            _source.pitch = kill ? 0.92f : 1f;
            _source.PlayOneShot(_hitmarker, Mathf.Clamp(Plugin.Cfg.HitmarkerVolume.Value, 0f, 2f));
        }

        public static void PlaySwoosh()
        {
            Ensure();
            _source.pitch = Random.Range(0.95f, 1.08f);
            _source.PlayOneShot(_swoosh, 0.6f);
        }

        /// <summary>The classic Call of Duty hitmarker: a sharp metallic tick, about a tenth of a second.</summary>
        private static AudioClip MakeHitmarker()
        {
            int n = (int)(Rate * 0.11f);
            var data = new float[n];
            var rng = new System.Random(7);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float click = t < 0.006f ? (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 600f) * 0.9f : 0f;
                float ring = Mathf.Sin(2f * Mathf.PI * 2950f * t) * Mathf.Exp(-t * 55f) * 0.55f
                           + Mathf.Sin(2f * Mathf.PI * 4400f * t) * Mathf.Exp(-t * 70f) * 0.35f
                           + Mathf.Sin(2f * Mathf.PI * 6200f * t) * Mathf.Exp(-t * 110f) * 0.2f
                           + Mathf.Sin(2f * Mathf.PI * 1480f * t) * Mathf.Exp(-t * 40f) * 0.18f;
                float env = Mathf.Min(1f, t / 0.0015f);
                data[i] = Mathf.Clamp(env * (click + ring), -1f, 1f);
            }
            var clip = AudioClip.Create("HTF1v1_Hitmarker", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>A short air swoosh: shaped noise with a sweeping low-pass.</summary>
        private static AudioClip MakeSwoosh()
        {
            int n = (int)(Rate * 0.22f);
            var data = new float[n];
            var rng = new System.Random(11);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float u = t / 0.22f;
                float env = Mathf.Sin(u * Mathf.PI);
                env *= env;
                float cutoff = Mathf.Lerp(0.08f, 0.5f, Mathf.Sin(u * Mathf.PI));
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lp += (noise - lp) * cutoff;
                data[i] = Mathf.Clamp(lp * env * 1.6f, -1f, 1f);
            }
            var clip = AudioClip.Create("HTF1v1_Swoosh", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }

    [HarmonyPatch]
    internal static class HitSoundPatches
    {
        // During matches the game's hitmarker sound is replaced by ours.
        [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.PlayGlobalClip))]
        [HarmonyPrefix]
        private static bool ReplaceHitmarker(string clip)
        {
            if (!ModState.IsActive || clip != "Hitmarker") return true;
            HitSounds.PlayHitmarker();
            return false;
        }
    }
}
