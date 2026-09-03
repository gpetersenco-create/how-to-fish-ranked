using System.Collections.Generic;
using System.Linq;
using HowToFish1v1.Core;
using HowToFish1v1.Net;
using HowToFish1v1.Net.Proto2;
using HowToFish1v1.UI;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// Frag and flash grenades. Hold the key to cook (the fuse runs in your hand), release to throw along the previewed
    /// arc. One of each per life. Throws are announced through the host so every client simulates the grenade with
    /// physics; the host alone applies frag damage through the game's hit path (so kills are credited and the
    /// anti-cheat is untouched), and every client decides its own flash from where it was looking.
    /// </summary>
    public static class Grenades
    {
        public const byte Frag = 0, Flash = 1;
        public const float FragFuse = 4f, FlashFuse = 2.2f;
        public const float FragRadius = 7f, FlashRadius = 14f;
        private const float ThrowSpeed = 15f;

        private sealed class Nade { public GameObject Go; public Rigidbody Body; public byte Kind; public int Owner; public float ExplodeAt; public bool Mine; }

        private static readonly List<Nade> _live = new List<Nade>();
        private static Material _fragMat, _flashMat, _arcMat;
        private static readonly List<GameObject> _arc = new List<GameObject>();
        private static byte _cooking = 255;
        private static float _cookStart;
        private static int _fragsLeft = 1, _flashesLeft = 1;
        private static float _flashUntil = -1f, _flashStrength;
        private static int _lastResetRound = -1;
        private static bool _wasDead;

        public static int FragsLeft => _fragsLeft;
        public static int FlashesLeft => _flashesLeft;
        public static bool Cooking => _cooking != 255;
        public static float CookFraction => Cooking ? Mathf.Clamp01((Time.unscaledTime - _cookStart) / (_cooking == Frag ? FragFuse : FlashFuse)) : 0f;
        /// <summary>0..1 whiteout for the local player's screen.</summary>
        public static float FlashAmount => Time.unscaledTime < _flashUntil ? Mathf.Clamp01((_flashUntil - Time.unscaledTime) / 2.5f) * _flashStrength : 0f;

        public static void Update()
        {
            if (!ModState.IsActive) { if (_live.Count > 0 || _arc.Count > 0) Clear(); _cooking = 255; return; }
            var me = Player.LocalPlayer;
            // Fresh grenades every round / respawn.
            if (ClientMatchView.HasState)
            {
                int round = ClientMatchView.Latest.Round;
                if (ModState.Phase == MatchPhase.Countdown && round != _lastResetRound) { _lastResetRound = round; _fragsLeft = 1; _flashesLeft = 1; }
                bool dead = me && me.Dying.IsDead;
                if (_wasDead && !dead) { _fragsLeft = 1; _flashesLeft = 1; }
                _wasDead = dead;
            }
            Simulate();
            if (!me || me.Dying.IsDead || ModState.Phase != MatchPhase.Live || ModState.PanelOpen || KillCam.Active || Results.Visible || me.BlockInputs)
            {
                if (Cooking) Throw(me);   // whatever happens, a cooked grenade leaves the hand
                HideArc();
                return;
            }
            var cam = me.CamObject ? me.CamObject : me.Transform;
            if (!Cooking)
            {
                if (Input.GetKeyDown(Plugin.Cfg.FragKey.Value) && _fragsLeft > 0) { _cooking = Frag; _cookStart = Time.unscaledTime; }
                else if (Input.GetKeyDown(Plugin.Cfg.FlashKey.Value) && _flashesLeft > 0) { _cooking = Flash; _cookStart = Time.unscaledTime; }
            }
            if (Cooking)
            {
                ShowArc(cam);
                bool key = _cooking == Frag ? Input.GetKey(Plugin.Cfg.FragKey.Value) : Input.GetKey(Plugin.Cfg.FlashKey.Value);
                float fuse = _cooking == Frag ? FragFuse : FlashFuse;
                if (!key || Time.unscaledTime - _cookStart >= fuse - 0.05f) Throw(me);
            }
            else HideArc();
        }

        private static void Throw(Player me)
        {
            byte kind = _cooking;
            _cooking = 255;
            HideArc();
            if (!me) return;
            var cam = me.CamObject ? me.CamObject : me.Transform;
            float fuseLeft = Mathf.Max(0.05f, (kind == Frag ? FragFuse : FlashFuse) - (Time.unscaledTime - _cookStart));
            Vector3 pos = cam.position + cam.forward * 0.5f + cam.right * 0.25f;
            Vector3 vel = (cam.forward + Vector3.up * 0.18f).normalized * ThrowSpeed + PlayerVelocity(me) * 0.5f;
            if (kind == Frag) _fragsLeft--; else _flashesLeft--;
            Spawn(me.OwnerId, kind, pos, vel, fuseLeft, true);
            ModNet.SendGrenade(kind, pos, vel, fuseLeft);
            HitSounds.PlayThrow();
        }

        private static Vector3 PlayerVelocity(Player p)
        {
            try { return p.Movement ? p.Movement.Velocity : Vector3.zero; } catch (System.Exception) { return Vector3.zero; }
        }

        /// <summary>A grenade thrown by someone else, relayed by the host.</summary>
        public static void OnRemote(int owner, byte kind, Vector3 pos, Vector3 vel, float fuse)
        {
            if (!ModState.IsActive || owner == ModState.LocalOwnerId) return;
            Spawn(owner, kind, pos, vel, fuse, false);
        }

        private static void Spawn(int owner, byte kind, Vector3 pos, Vector3 vel, float fuse, bool mine)
        {
            EnsureMaterials();
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = kind == Frag ? "HTF1v1_Frag" : "HTF1v1_Flash";
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * (kind == Frag ? 0.16f : 0.14f);
            go.GetComponent<MeshRenderer>().sharedMaterial = kind == Frag ? _fragMat : _flashMat;
            var col = go.GetComponent<SphereCollider>();
            col.material = new PhysicsMaterial { bounciness = 0.35f, dynamicFriction = 0.6f, staticFriction = 0.6f, bounceCombine = PhysicsMaterialCombine.Maximum };
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.4f; rb.drag = 0.1f; rb.angularDrag = 0.5f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.velocity = vel;
            rb.angularVelocity = Random.insideUnitSphere * 8f;
            // The frag's pin light.
            if (kind == Frag)
            {
                var light = new GameObject("light").AddComponent<Light>();
                light.transform.SetParent(go.transform, false);
                light.type = LightType.Point; light.range = 1.5f; light.intensity = 1.2f; light.color = new Color(1f, 0.3f, 0.2f);
            }
            _live.Add(new Nade { Go = go, Body = rb, Kind = kind, Owner = owner, ExplodeAt = Time.unscaledTime + fuse, Mine = mine });
        }

        private static void Simulate()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var n = _live[i];
                if (!n.Go) { _live.RemoveAt(i); continue; }
                if (Time.unscaledTime >= n.ExplodeAt)
                {
                    Explode(n);
                    Object.Destroy(n.Go);
                    _live.RemoveAt(i);
                }
            }
        }

        private static void Explode(Nade n)
        {
            Vector3 at = n.Go.transform.position;
            var me = Player.LocalPlayer;
            if (n.Kind == Frag)
            {
                Fx.Burst(at, new Color(1f, 0.6f, 0.2f), 18, 3.5f);
                HitSounds.PlayExplosion(at, 0.7f);
                if (me && me.Transform && Vector3.Distance(me.Transform.position, at) < FragRadius + 4f) HitReactions.Hit(at, 50);
                if (ModNet.IsHost) HostDamage(n.Owner, at);
            }
            else
            {
                Fx.Burst(at, Color.white, 10, 5f);
                HitSounds.PlayFlashPop(at);
                if (me && !me.Dying.IsDead && me.Transform)
                {
                    var cam = me.CamObject ? me.CamObject : me.Transform;
                    float d = Vector3.Distance(cam.position, at);
                    if (d < FlashRadius)
                    {
                        Vector3 to = (at - cam.position).normalized;
                        float facing = Vector3.Dot(cam.forward, to);          // -1 away .. 1 straight at it
                        bool seen = !Physics.Linecast(cam.position, at, GameInfo.LevelLayer);
                        if (seen && facing > -0.2f)
                        {
                            float strength = Mathf.Clamp01(1f - d / FlashRadius) * Mathf.Lerp(0.35f, 1f, Mathf.Clamp01((facing + 0.2f) / 1.2f));
                            _flashUntil = Time.unscaledTime + 2.5f * Mathf.Max(0.4f, strength);
                            _flashStrength = Mathf.Max(_flashStrength * FlashAmount, strength);
                            HitSounds.PlayRing(strength);
                        }
                    }
                }
            }
        }

        /// <summary>Host only: frag damage by distance, applied through the game's hit RPC so the kill is credited.</summary>
        private static void HostDamage(int ownerId, Vector3 at)
        {
            var attacker = PlayerManager.Players.FirstOrDefault(p => p && p.OwnerId == ownerId);
            if (!attacker) return;
            foreach (var p in PlayerManager.Players)
            {
                if (!p || !p.Transform || p.Dying.IsDead) continue;
                Vector3 centre = p.Transform.position + Vector3.up;
                float d = Vector3.Distance(centre, at);
                if (d > FragRadius) continue;
                if (Physics.Linecast(at + Vector3.up * 0.1f, centre, GameInfo.LevelLayer)) continue;   // behind cover
                int dmg = Mathf.RoundToInt(Mathf.Lerp(GunBalance.Health + 10, 25f, Mathf.Clamp01((d - 1.5f) / (FragRadius - 1.5f))));
                Vector3 force = (centre - at).normalized * 6f;
                Patches.KillAttribution.ExplosionDamage = true;
                try { Server.Instance.HitPlayer(p, dmg, force, centre, 1, attacker); }
                catch (System.Exception e) { Plugin.Log.LogWarning("Frag damage: " + e.Message); }
                finally { Patches.KillAttribution.ExplosionDamage = false; }
            }
        }

        // ------------------------------------------------------------------ arc preview

        private static void ShowArc(Transform cam)
        {
            EnsureMaterials();
            Vector3 pos = cam.position + cam.forward * 0.5f + cam.right * 0.25f;
            Vector3 vel = (cam.forward + Vector3.up * 0.18f).normalized * ThrowSpeed;
            const int Points = 24; const float Step = 0.09f;
            while (_arc.Count < Points)
            {
                var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dot.name = "HTF1v1_ArcDot";
                Object.Destroy(dot.GetComponent<Collider>());
                dot.transform.localScale = Vector3.one * 0.06f;
                dot.GetComponent<MeshRenderer>().sharedMaterial = _arcMat;
                _arc.Add(dot);
            }
            Vector3 p = pos, v = vel;
            bool stopped = false;
            for (int i = 0; i < Points; i++)
            {
                if (stopped) { _arc[i].SetActive(false); continue; }
                Vector3 next = p + v * Step;
                if (Physics.Linecast(p, next, out var hit, GameInfo.LevelLayer)) { next = hit.point; stopped = true; }
                v += Physics.gravity * Step;
                p = next;
                _arc[i].SetActive(true);
                _arc[i].transform.position = p;
                _arc[i].transform.localScale = Vector3.one * Mathf.Lerp(0.07f, 0.03f, i / (float)Points);
            }
        }

        private static void HideArc() { foreach (var d in _arc) if (d && d.activeSelf) d.SetActive(false); }

        private static void EnsureMaterials()
        {
            if (_fragMat) return;
            _fragMat = new Material(Arena.ArenaMaterials.For(BoxKind.Steel)) { name = "HTF1v1_FragMat" };
            if (_fragMat.HasProperty("_BaseColor")) _fragMat.SetColor("_BaseColor", new Color(0.25f, 0.3f, 0.22f));
            _flashMat = new Material(Arena.ArenaMaterials.For(BoxKind.White)) { name = "HTF1v1_FlashMat" };
            _arcMat = new Material(Arena.ArenaMaterials.For(BoxKind.Yellow)) { name = "HTF1v1_ArcMat" };
            foreach (var m in new[] { _fragMat, _flashMat, _arcMat })
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
                if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
                m.DisableKeyword("_NORMALMAP");
            }
            _arcMat.EnableKeyword("_EMISSION");
            if (_arcMat.HasProperty("_EmissionColor")) _arcMat.SetColor("_EmissionColor", new Color(1f, 0.8f, 0.3f) * 2f);
        }

        private static void Clear()
        {
            foreach (var n in _live) if (n.Go) Object.Destroy(n.Go);
            _live.Clear();
            foreach (var d in _arc) if (d) Object.Destroy(d);
            _arc.Clear();
        }
    }

    /// <summary>Cheap particle bursts from primitives, for explosions and flashes.</summary>
    public static class Fx
    {
        private sealed class Bit { public GameObject Go; public Vector3 Vel; public float Until; }
        private static readonly List<Bit> _bits = new List<Bit>();
        private static readonly Dictionary<Color, Material> _mats = new Dictionary<Color, Material>();

        public static void Burst(Vector3 at, Color color, int count, float speed)
        {
            if (!_mats.TryGetValue(color, out var mat) || !mat)
            {
                mat = new Material(Arena.ArenaMaterials.For(BoxKind.Yellow)) { name = "HTF1v1_Fx" };
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", null);
                if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", null);
                mat.DisableKeyword("_NORMALMAP");
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * 4f);
                _mats[color] = mat;
            }
            for (int i = 0; i < count; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "HTF1v1_FxBit";
                Object.Destroy(go.GetComponent<Collider>());
                go.transform.position = at;
                go.transform.localScale = Vector3.one * Random.Range(0.12f, 0.35f);
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                _bits.Add(new Bit { Go = go, Vel = Random.onUnitSphere * speed * Random.Range(0.4f, 1f) + Vector3.up * speed * 0.3f, Until = Time.unscaledTime + Random.Range(0.35f, 0.7f) });
            }
        }

        public static void Update()
        {
            for (int i = _bits.Count - 1; i >= 0; i--)
            {
                var b = _bits[i];
                if (!b.Go || Time.unscaledTime > b.Until) { if (b.Go) Object.Destroy(b.Go); _bits.RemoveAt(i); continue; }
                b.Vel += Physics.gravity * Time.deltaTime * 0.6f;
                b.Vel *= 0.96f;
                b.Go.transform.position += b.Vel * Time.deltaTime;
                b.Go.transform.localScale *= 0.97f;
            }
        }
    }
}
