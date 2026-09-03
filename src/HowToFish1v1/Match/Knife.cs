using System.Collections.Generic;
using HarmonyLib;
using HowToFish1v1.Net;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// Black Ops 2 style knife on a key: a quick lunge-slash with a knife-in-hand viewmodel that takes the gun's place
    /// for the swing, a one-hit kill on any player within reach, and a hit on birds, fish and trickshot targets too.
    /// The damage goes through the game's own hit path (server, hitmarker and kill credit as for a bullet). Swings are
    /// announced to the host so every client can record them for the killcam replay.
    /// </summary>
    public static class Knife
    {
        public const float SwingSeconds = 0.42f;
        private const float HitAt = 0.36f;          // fraction of the swing at which the blade crosses the centre
        private const float Reach = 2.3f;
        private const float Radius = 0.4f;
        private const int Damage = HowToFish1v1.Core.GunBalance.KnifeDamage;
        private const float Cooldown = 0.55f;

        private static GameObject _model;
        private static readonly List<Object> _created = new List<Object>();
        private static byte _modelSkin = 255;
        private static float _swingStart = -10f;
        private static bool _swinging, _hitDone;
        private static readonly List<Renderer> _hidden = new List<Renderer>();
        private static bool _hooked;

        public static bool Swinging => _swinging;

        public static void Update()
        {
            if (!_hooked)
            {
                _hooked = true;
                // The game switches its arm meshes back on every LateUpdate; keep them off while the knife hand is out.
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += (ctx, cam) => { if (_swinging) foreach (var r in _hidden) if (r && r.enabled) r.enabled = false; };
            }
            var me = Player.LocalPlayer;
            if (!ModState.IsActive || !me || me.Dying.IsDead || ModState.PanelOpen || KillCam.Active || MainMenuManager.IsInMenu)
            {
                if (_swinging) EndSwing();
                return;
            }
            if (_swinging) Animate(me);
            else if (Input.GetKeyDown(Plugin.Cfg.KnifeKey.Value) && !me.BlockInputs && Time.unscaledTime - _swingStart > Cooldown) StartSwing(me);
        }

        private static byte ChosenSkin()
        {
            byte skin = (byte)Mathf.Clamp(Plugin.Cfg.KnifeSkin.Value, 0, WeaponSkins.Count - 1);
            return WeaponSkins.CanPick(skin) ? skin : (byte)0;
        }

        private static void StartSwing(Player me)
        {
            byte skin = ChosenSkin();
            if (!_model || _modelSkin != skin)
            {
                if (_model) Object.Destroy(_model);
                foreach (var o in _created) if (o) Object.Destroy(o);
                _created.Clear();
                Vector3 tone = new Vector3(0.85f, 0.65f, 0.5f);
                try { tone = me.Skin ? me.Skin.SkinColor : tone; } catch (System.Exception) { }
                _model = BuildModel(skin, _created, true, tone);
                _modelSkin = skin;
            }
            _swinging = true; _hitDone = false;
            _swingStart = Time.unscaledTime;
            _model.SetActive(true);
            // The gun and the game's own arms step aside for the swing.
            var item = me.Holding ? me.Holding.HeldItem : null;
            if (item) foreach (var r in item.GetComponentsInChildren<Renderer>(true)) if (r && r.enabled) { r.enabled = false; _hidden.Add(r); }
            try
            {
                var t = Traverse.Create(me.Hands);
                foreach (var f in new[] { "_handModelLeft", "_handModelRight" })
                {
                    var r = t.Field<Renderer>(f).Value;
                    if (r && r.enabled) { r.enabled = false; _hidden.Add(r); }
                }
            }
            catch (System.Exception) { }
            HitSounds.PlaySwoosh();
            Recorder.RecordKnife(me.OwnerId, skin);
            ModNet.SendKnife(skin);
        }

        private static void EndSwing()
        {
            _swinging = false;
            if (_model) _model.SetActive(false);
            foreach (var r in _hidden) if (r) r.enabled = true;
            _hidden.Clear();
        }

        private static void Animate(Player me)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - _swingStart) / SwingSeconds);
            if (t >= 1f) { EndSwing(); return; }
            var cam = me.CamObject ? me.CamObject : me.Transform;
            SwingPose(t, out var pos, out var rot);
            _model.transform.SetPositionAndRotation(cam.TransformPoint(pos), cam.rotation * rot);
            foreach (var r in _hidden) if (r && r.enabled) r.enabled = false;
            if (!_hitDone && t >= HitAt) { _hitDone = true; TryHit(me, cam); }
        }

        /// <summary>
        /// The swing in camera space: a slash. The blade is held sideways (point to the left, edge leading), cocked back on
        /// the right, then sweeps across the screen from right to left in a shallow downward arc, follows through past the
        /// left edge and comes back. The knife stays roughly the same distance from the eye the whole way.
        /// </summary>
        public static void SwingPose(float t, out Vector3 pos, out Quaternion rot)
        {
            // Rest / cocked back (right, low, blade pointing left-forward), mid-slash (centre), follow-through (far left).
            Vector3 p0 = new Vector3(0.40f, -0.26f, 0.42f), pc = new Vector3(0.44f, -0.16f, 0.36f), p1 = new Vector3(0.02f, -0.10f, 0.52f), p2 = new Vector3(-0.42f, -0.20f, 0.40f);
            Vector3 r0 = new Vector3(10f, -55f, 95f), rc = new Vector3(5f, -80f, 100f), r1 = new Vector3(-8f, -85f, 90f), r2 = new Vector3(-20f, -120f, 70f);
            Vector3 e;
            if (t < 0.18f) { float k = Mathf.SmoothStep(0f, 1f, t / 0.18f); pos = Vector3.Lerp(p0, pc, k); e = Vector3.Lerp(r0, rc, k); }                 // cock back
            else if (t < 0.36f) { float k = (t - 0.18f) / 0.18f; k = k * k; pos = Vector3.Lerp(pc, p1, k); e = Vector3.Lerp(rc, r1, k); }                  // accelerate across
            else if (t < 0.52f) { float k = (t - 0.36f) / 0.16f; pos = Vector3.Lerp(p1, p2, k); e = Vector3.Lerp(r1, r2, k); }                              // through the target
            else { float k = Mathf.SmoothStep(0f, 1f, (t - 0.52f) / 0.48f); pos = Vector3.Lerp(p2, p0, k); e = Vector3.Lerp(r2, r0, k); }                  // recover
            // A little dip through the middle of the arc so the slash reads as diagonal, not a flat wipe.
            float mid = Mathf.Clamp01(1f - Mathf.Abs((t - 0.36f) / 0.2f));
            pos.y -= 0.05f * mid;
            rot = Quaternion.Euler(e);
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
                    Patches.CombatPatches.SendingRaw = true;
                    try { victim.Vitals.LocalHit(point, fwd, me, Damage, false, fwd * GameInfo.PlayerKillForce); } catch (System.Exception e) { Plugin.Log.LogWarning("Knife hit: " + e.Message); }
                    finally { Patches.CombatPatches.SendingRaw = false; }
                    return;
                }
                if (h.collider && h.collider.GetComponentInParent<TrickshotBot>()) { Trickshot.RegisterHit(); return; }
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

        // ------------------------------------------------------------------ model

        /// <summary>The knife viewmodel: blade, guard, grip and pommel in the chosen skin, plus the hand and forearm holding it.</summary>
        public static GameObject BuildModel(byte skin, List<Object> track, bool withHand, Vector3 skinTone)
        {
            var model = new GameObject("HTF1v1_Knife");
            var shader = Arena.ArenaMaterials.LitShader;
            Material Mat(string name, Color c, float metal, float gloss)
            {
                var m = new Material(shader ? shader : Shader.Find("Sprites/Default")) { name = "HTF1v1_Knife" + name };
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", gloss);
                track?.Add(m);
                return m;
            }
            var steel = Mat("Steel", new Color(0.82f, 0.84f, 0.88f), 0.95f, 0.9f);
            var grip = Mat("Grip", new Color(0.08f, 0.08f, 0.09f), 0.1f, 0.4f);
            Material bladeMat = steel, gripMat = grip;
            if (skin > 0)
            {
                bladeMat = WeaponSkins.MaterialsFor(skin, new[] { steel }, track)[0];
                gripMat = WeaponSkins.MaterialsFor(skin, new[] { grip }, track)[0];
            }
            // Blade: a long flattened box with a tapered tip; guard, grip and pommel behind it. Forward is +z.
            Part(model, PrimitiveType.Cube, "Blade", new Vector3(0f, 0f, 0.12f), Vector3.zero, new Vector3(0.006f, 0.032f, 0.19f), bladeMat);
            Part(model, PrimitiveType.Cube, "Tip", new Vector3(0f, 0.006f, 0.225f), Vector3.zero, new Vector3(0.005f, 0.02f, 0.05f), bladeMat);
            Part(model, PrimitiveType.Cube, "Edge", new Vector3(0f, -0.014f, 0.11f), Vector3.zero, new Vector3(0.0025f, 0.006f, 0.17f), bladeMat);
            Part(model, PrimitiveType.Cube, "Guard", new Vector3(0f, 0f, 0.015f), Vector3.zero, new Vector3(0.016f, 0.07f, 0.014f), gripMat);
            Part(model, PrimitiveType.Capsule, "Grip", new Vector3(0f, 0f, -0.05f), new Vector3(90f, 0f, 0f), new Vector3(0.028f, 0.05f, 0.028f), gripMat);
            Part(model, PrimitiveType.Sphere, "Pommel", new Vector3(0f, 0f, -0.105f), Vector3.zero, new Vector3(0.03f, 0.03f, 0.03f), bladeMat);
            if (withHand)
            {
                // A fist wrapped around the grip, a wrist, and a forearm reaching back and down toward the shoulder.
                var skinMat = Mat("Skin", new Color(skinTone.x, skinTone.y, skinTone.z), 0f, 0.35f);
                var sleeve = Mat("Sleeve", new Color(0.16f, 0.18f, 0.2f), 0f, 0.3f);
                Part(model, PrimitiveType.Sphere, "Palm", new Vector3(0.012f, -0.005f, -0.05f), Vector3.zero, new Vector3(0.075f, 0.085f, 0.09f), skinMat);
                for (int i = 0; i < 4; i++)
                    Part(model, PrimitiveType.Capsule, "Finger" + i, new Vector3(-0.035f, 0.028f - i * 0.019f, -0.028f - i * 0.012f), new Vector3(0f, 0f, 90f), new Vector3(0.018f, 0.03f, 0.018f), skinMat);
                Part(model, PrimitiveType.Capsule, "Thumb", new Vector3(0.02f, 0.035f, -0.02f), new Vector3(60f, 0f, 20f), new Vector3(0.017f, 0.025f, 0.017f), skinMat);
                Part(model, PrimitiveType.Capsule, "Wrist", new Vector3(0.02f, -0.03f, -0.11f), new Vector3(60f, 0f, -15f), new Vector3(0.06f, 0.05f, 0.06f), skinMat);
                Part(model, PrimitiveType.Capsule, "Forearm", new Vector3(0.06f, -0.12f, -0.22f), new Vector3(55f, 0f, -20f), new Vector3(0.075f, 0.14f, 0.075f), sleeve);
            }
            model.SetActive(false);
            return model;
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
        }
    }

    /// <summary>
    /// Mod sounds: the hitmarker "tick" that replaces the game's during matches, and the knife swing. Both are made in
    /// code, but a sound file dropped next to the plugin overrides them: knife.wav / knife.ogg / knife.mp3 and
    /// hitmarker.wav / .ogg / .mp3 in BepInEx/plugins/HowToFish1v1. Played through the game's sound mixer group so the
    /// volume slider still applies.
    /// </summary>
    public static class HitSounds
    {
        private static AudioSource _source, _knifeSource;
        private static AudioClip _hitmarker, _swoosh;
        private static float _knifeTrim;
        private const int Rate = 44100;
        private static bool _filesRequested;

        /// <summary>Seconds of near-silence at the start of a clip (so a file with a quiet lead-in still snaps to the swing).</summary>
        private static float LeadingSilence(AudioClip clip)
        {
            try
            {
                int n = Mathf.Min(clip.samples, clip.frequency * 2) * clip.channels;
                var data = new float[n];
                if (!clip.GetData(data, 0)) return 0f;
                for (int i = 0; i < n; i++)
                    if (Mathf.Abs(data[i]) > 0.035f) return Mathf.Max(0f, (float)(i / clip.channels) / clip.frequency - 0.005f);
            }
            catch (System.Exception) { }
            return 0f;
        }

        /// <summary>Starts loading knife/hitmarker sound files from the plugin folder, if any exist.</summary>
        public static System.Collections.IEnumerator LoadFiles()
        {
            if (_filesRequested) yield break;
            _filesRequested = true;
            string dir = System.IO.Path.GetDirectoryName(typeof(HitSounds).Assembly.Location);
            foreach (var (name, apply) in new (string, System.Action<AudioClip>)[] { ("knife", c => _swoosh = c), ("hitmarker", c => _hitmarker = c) })
            {
                foreach (var ext in new[] { ".wav", ".ogg", ".mp3" })
                {
                    string path = System.IO.Path.Combine(dir, name + ext);
                    if (!System.IO.File.Exists(path)) continue;
                    var type = ext == ".wav" ? AudioType.WAV : ext == ".ogg" ? AudioType.OGGVORBIS : AudioType.MPEG;
                    using (var req = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace(System.IO.Path.DirectorySeparatorChar, '/'), type))
                    {
                        yield return req.SendWebRequest();
                        if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                        {
                            var clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(req);
                            if (clip && clip.length > 0f)
                            {
                                Ensure(); apply(clip);
                                if (name == "knife") _knifeTrim = LeadingSilence(clip);
                                Plugin.Log.LogInfo($"Loaded custom sound {name}{ext} ({clip.length:0.00} s{(name == "knife" ? $", lead-in {_knifeTrim:0.000} s" : "")})");
                            }
                        }
                        else Plugin.Log.LogWarning($"Could not load {path}: {req.error}");
                    }
                    break;
                }
            }
        }

        private static void Ensure()
        {
            if (_source) return;
            var go = new GameObject("HTF1v1_Sounds");
            Object.DontDestroyOnLoad(go);
            _source = go.AddComponent<AudioSource>();
            _source.spatialBlend = 0f;
            _source.playOnAwake = false;
            _source.priority = 0;
            _knifeSource = go.AddComponent<AudioSource>();
            _knifeSource.spatialBlend = 0f;
            _knifeSource.playOnAwake = false;
            _knifeSource.priority = 0;
            try
            {
                var global = Traverse.Create(typeof(AudioManager)).Field<AudioSource>("_globalSource").Value;
                if (global) { _source.outputAudioMixerGroup = global.outputAudioMixerGroup; _knifeSource.outputAudioMixerGroup = global.outputAudioMixerGroup; }
            }
            catch (System.Exception) { }
            _hitmarker = MakeHitmarker();
            _swoosh = MakeKnifeSwing();
        }

        public static void PlayHitmarker(bool kill = false)
        {
            Ensure();
            _source.pitch = kill ? 0.92f : 1f;
            _source.PlayOneShot(_hitmarker, Mathf.Clamp(Plugin.Cfg.HitmarkerVolume.Value, 0f, 2f));
        }

        private static AudioClip _ricochet;

        /// <summary>A ricochet ping at the wall: a descending metallic whine with a sharp crack at the front.</summary>
        public static void PlayRicochet(Vector3 at)
        {
            Ensure();
            if (!_ricochet)
            {
                int n = (int)(Rate * 0.32f);
                var data = new float[n];
                var rng = new System.Random(23);
                float phase = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / Rate;
                    float u = t / 0.32f;
                    float f = Mathf.Lerp(4200f, 900f, Mathf.Pow(u, 0.55f));
                    phase += 2f * Mathf.PI * f / Rate;
                    float whine = Mathf.Sin(phase) * Mathf.Exp(-t * 9f) * 0.55f + Mathf.Sin(phase * 2.01f) * Mathf.Exp(-t * 14f) * 0.2f;
                    float crack = t < 0.012f ? (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 400f) * 0.8f : 0f;
                    data[i] = Mathf.Clamp(whine + crack, -1f, 1f);
                }
                _ricochet = AudioClip.Create("HTF1v1_Ricochet", n, 1, Rate, false);
                _ricochet.SetData(data, 0);
            }
            // A short-range 3D source: inaudible past 35 m, and the further away the more muffled and quieter it gets.
            const float Range = 35f;
            Vector3 ear = at;
            try { var me = Player.LocalPlayer; if (me) ear = (me.CamObject ? me.CamObject : me.Transform).position; } catch (System.Exception) { }
            float d = Vector3.Distance(ear, at);
            if (d > Range) return;
            float far = Mathf.Clamp01(d / Range);
            var go = new GameObject("HTF1v1_RicochetSound");
            go.transform.position = at;
            var src = go.AddComponent<AudioSource>();
            src.clip = _ricochet;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1.5f;
            src.maxDistance = Range;
            src.dopplerLevel = 0f;
            src.volume = Mathf.Lerp(0.9f, 0.25f, far);
            src.pitch = Random.Range(0.94f, 1.08f) * Mathf.Lerp(1f, 0.85f, far);
            if (_source) src.outputAudioMixerGroup = _source.outputAudioMixerGroup;
            var lp = go.AddComponent<AudioLowPassFilter>();
            lp.cutoffFrequency = Mathf.Lerp(18000f, 900f, far * far);
            src.Play();
            Object.Destroy(go, _ricochet.length / Mathf.Max(0.5f, src.pitch) + 0.1f);
        }

        private static AudioClip _beep, _boom;

        /// <summary>The planted bomb's beep: higher and sharper as time runs out.</summary>
        public static void PlayBeep(Vector3 at, float urgency)
        {
            Ensure();
            if (!_beep)
            {
                int n = (int)(Rate * 0.09f);
                var data = new float[n];
                for (int i = 0; i < n; i++) { float t = (float)i / Rate; data[i] = Mathf.Sin(2f * Mathf.PI * 1900f * t) * Mathf.Exp(-t * 40f) * 0.7f; }
                _beep = AudioClip.Create("HTF1v1_Beep", n, 1, Rate, false); _beep.SetData(data, 0);
            }
            var go = new GameObject("HTF1v1_BeepSound");
            go.transform.position = at;
            var src = go.AddComponent<AudioSource>();
            src.clip = _beep; src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Linear; src.minDistance = 3f; src.maxDistance = 60f;
            src.pitch = Mathf.Lerp(1f, 1.5f, urgency); src.volume = 0.8f;
            if (_source) src.outputAudioMixerGroup = _source.outputAudioMixerGroup;
            src.Play();
            Object.Destroy(go, 0.3f);
        }

        /// <summary>The bomb going off: a deep boom with a long rumble, heard across the map.</summary>
        public static void PlayExplosion(Vector3 at)
        {
            Ensure();
            if (!_boom)
            {
                int n = (int)(Rate * 1.6f);
                var data = new float[n];
                var rng = new System.Random(5);
                float lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / Rate;
                    float noise = (float)(rng.NextDouble() * 2 - 1);
                    lp += (noise - lp) * Mathf.Lerp(0.35f, 0.02f, Mathf.Clamp01(t / 1.2f));
                    float thump = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(90f, 35f, Mathf.Clamp01(t * 3f)) * t) * Mathf.Exp(-t * 4f);
                    data[i] = Mathf.Clamp((lp * 2.5f * Mathf.Exp(-t * 2.2f) + thump * 0.9f) * Mathf.Min(1f, t / 0.004f), -1f, 1f);
                }
                _boom = AudioClip.Create("HTF1v1_Boom", n, 1, Rate, false); _boom.SetData(data, 0);
            }
            var go = new GameObject("HTF1v1_BoomSound");
            go.transform.position = at;
            var src = go.AddComponent<AudioSource>();
            src.clip = _boom; src.spatialBlend = 0.6f; src.rolloffMode = AudioRolloffMode.Linear; src.minDistance = 10f; src.maxDistance = 200f; src.volume = 1f;
            if (_source) src.outputAudioMixerGroup = _source.outputAudioMixerGroup;
            src.Play();
            Object.Destroy(go, 2f);
        }

        public static void PlaySwoosh()
        {
            Ensure();
            // Start a little into the clip: its own lead-in silence plus the configured trim.
            float trim = Mathf.Clamp(_knifeTrim + Plugin.Cfg.KnifeSoundTrim.Value, 0f, Mathf.Max(0f, _swoosh.length - 0.05f));
            _knifeSource.Stop();
            _knifeSource.clip = _swoosh;
            _knifeSource.volume = 0.85f;
            _knifeSource.pitch = Random.Range(0.97f, 1.04f);
            _knifeSource.time = trim;
            _knifeSource.Play();
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

        /// <summary>
        /// The Black Ops 2 knife swing: a fast, airy "shwiff" (band-passed noise whose centre sweeps upward through the
        /// swing) with a thin steel edge ringing on top. About a quarter of a second.
        /// </summary>
        private static AudioClip MakeKnifeSwing()
        {
            int n = (int)(Rate * 0.26f);
            var data = new float[n];
            var rng = new System.Random(11);
            // Two-pole resonant band-pass, re-tuned every sample as the centre frequency sweeps.
            float y1 = 0f, y2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float u = t / 0.26f;
                float env = Mathf.Pow(Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI), 0.7f) * (u < 0.55f ? 1f : Mathf.Exp(-(u - 0.55f) * 6f));
                float centre = Mathf.Lerp(700f, 5200f, Mathf.SmoothStep(0f, 1f, u * 1.4f));
                float q = 6f;
                float w0 = 2f * Mathf.PI * centre / Rate;
                float r = Mathf.Exp(-w0 / (2f * q));
                float a1 = 2f * r * Mathf.Cos(w0), a2 = -r * r;
                float x = (float)(rng.NextDouble() * 2 - 1);
                float y = x * (1f - r) + a1 * y1 + a2 * y2;
                y2 = y1; y1 = y;
                float whoosh = y * env * 2.2f;
                float edge = t > 0.05f ? Mathf.Sin(2f * Mathf.PI * 5600f * t) * Mathf.Exp(-(t - 0.05f) * 45f) * 0.12f + Mathf.Sin(2f * Mathf.PI * 8200f * t) * Mathf.Exp(-(t - 0.05f) * 80f) * 0.06f : 0f;
                data[i] = Mathf.Clamp(whoosh + edge, -1f, 1f);
            }
            var clip = AudioClip.Create("HTF1v1_KnifeSwing", n, 1, Rate, false);
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
