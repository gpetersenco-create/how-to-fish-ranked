using System.Collections.Generic;
using HarmonyLib;
using HowToFish1v1.Net;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// Ricochets. When one of your bullets hits an arena surface it bounces once off the surface and keeps going: a
    /// player, bot or bird on the bounced path takes reduced damage through the game's normal hit path. The bounce draws
    /// a bright tracer and pings on the wall. Bounces are announced to the host so every client sees the trail live and
    /// the killcam can replay it.
    /// </summary>
    public static class Ricochet
    {
        public const float DamageScale = Core.GunBalance.RicochetScale;
        private const float MaxBounceRange = 90f;
        private const float StreakSeconds = 0.09f;
        private const float StreakLength = 2.4f;

        private sealed class Streak { public GameObject Go; public Vector3 From, To; public float T0; }
        private static readonly List<Streak> _streaks = new List<Streak>();
        private static Material _mat;

        /// <summary>The local player's shot: bounce it once off the level and see what it reaches.</summary>
        public static void OnShot(Weapon w)
        {
            if (!ModState.IsActive || !w || !w.Holder || w.Holder.Owner == null || !w.Holder.Owner.IsLocalClient) return;
            var me = w.Holder;
            var cam = me.CamObject ? me.CamObject : me.Transform;
            if (!cam) return;
            int mask = ~0;
            int local = LayerMask.NameToLayer("LocalPlayer");
            if (local >= 0) mask &= ~(1 << local);
            if (!Physics.Raycast(cam.position + cam.forward * 0.5f, cam.forward, out var hit1, 300f, mask, QueryTriggerInteraction.Ignore)) return;
            if (!CanBounce(hit1, cam.forward)) return;
            if (Random.value > Mathf.Clamp01(Plugin.Cfg.RicochetChance.Value)) return;   // most bullets just bury themselves

            Vector3 dir = Vector3.Reflect(cam.forward, hit1.normal).normalized;
            Vector3 start = hit1.point + hit1.normal * 0.02f;
            Vector3 end = start + dir * MaxBounceRange;
            bool reached = Physics.Raycast(start, dir, out var hit2, MaxBounceRange, mask, QueryTriggerInteraction.Ignore);
            if (reached) end = hit2.point;

            Show(start, end);
            HitSounds.PlayRicochet(start);
            Recorder.RecordBounce(me.OwnerId, start, end);
            ModNet.SendBounce(start, end);

            if (!reached || !hit2.collider) return;
            int damage = Core.GunBalance.RicochetDamageFor(LoadoutService.DisplayName(w));
            Player victim = null;
            try { victim = PlayerManager.GetPlayerFromBodyPart(hit2.transform); } catch (System.Exception) { }
            if (victim && victim != me && !victim.Dying.IsDead)
            {
                Patches.CombatPatches.SendingRaw = true;
                try { victim.Vitals.LocalHit(hit2.point, dir, me, damage, true, dir * GameInfo.PlayerKillForce * 0.5f); } catch (System.Exception e) { Plugin.Log.LogWarning("Ricochet hit: " + e.Message); }
                finally { Patches.CombatPatches.SendingRaw = false; }
                return;
            }
            if (hit2.collider.GetComponentInParent<TrickshotBot>()) { Trickshot.RegisterHit(); return; }
            Item item = null;
            try { item = ItemManager.Get(hit2.transform); } catch (System.Exception) { }
            if (item && item is Creature)
            {
                try { item.LocalHit(hit2.transform, hit2.point, dir, me, damage, true, dir * 3f); } catch (System.Exception) { }
            }
        }

        /// <summary>Any real arena surface can bounce a bullet (the chance is rolled separately); the invisible borders never do.</summary>
        public static bool CanBounce(RaycastHit hit, Vector3 dir)
        {
            if (!hit.collider) return false;
            var surf = hit.collider.GetComponent<Arena.ArenaSurface>();
            return surf && surf.Bounces;
        }

        private static int Damage(Weapon w)
        {
            try
            {
                var att = w.Attachments;
                var ups = Traverse.Create(att).Field<BulletUpgrade[]>("_bulletUpgrades").Value;
                int idx = att._syncedBulletIndex.Value;
                if (ups != null && idx >= 0 && idx < ups.Length && ups[idx] != null) return ups[idx].Damage;
            }
            catch (System.Exception) { }
            return 20;
        }

        /// <summary>A bounce someone else fired, relayed by the host: draw it live and record it for replays.</summary>
        public static void OnRemoteBounce(int ownerId, Vector3 from, Vector3 to)
        {
            if (!ModState.IsActive) return;
            Recorder.RecordBounce(ownerId, from, to);
            Show(from, to);
            HitSounds.PlayRicochet(from);
        }

        public static void Show(Vector3 from, Vector3 to)
        {
            if (!_mat)
            {
                _mat = new Material(Arena.ArenaMaterials.For(Core.BoxKind.Yellow));
                if (_mat.HasProperty("_BaseMap")) _mat.SetTexture("_BaseMap", null);
                if (_mat.HasProperty("_BumpMap")) _mat.SetTexture("_BumpMap", null);
                _mat.DisableKeyword("_NORMALMAP");
                if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", new Color(1f, 0.95f, 0.7f));
                _mat.EnableKeyword("_EMISSION");
                if (_mat.HasProperty("_EmissionColor")) _mat.SetColor("_EmissionColor", new Color(1f, 0.75f, 0.3f) * 6f);
            }
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "HTF1v1_Ricochet";
            Object.Destroy(go.GetComponent<Collider>());
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _streaks.Add(new Streak { Go = go, From = from, To = to, T0 = Time.unscaledTime });
        }

        /// <summary>Animates the live streaks: a bright segment flying from the wall to where the bounce ended.</summary>
        public static void Update()
        {
            for (int i = _streaks.Count - 1; i >= 0; i--)
            {
                var s = _streaks[i];
                if (!s.Go) { _streaks.RemoveAt(i); continue; }
                float dist = Vector3.Distance(s.From, s.To);
                float dur = Mathf.Clamp(StreakSeconds * dist / 25f, 0.05f, 0.35f);
                float k = (Time.unscaledTime - s.T0) / dur;
                if (k > 1.2f) { Object.Destroy(s.Go); _streaks.RemoveAt(i); continue; }
                k = Mathf.Clamp01(k);
                Vector3 dir = dist > 0.001f ? (s.To - s.From) / dist : Vector3.forward;
                Vector3 head = s.From + dir * (dist * k);
                Vector3 tail = head - dir * Mathf.Min(StreakLength, dist * k);
                s.Go.transform.position = (head + tail) * 0.5f;
                s.Go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                s.Go.transform.localScale = new Vector3(0.03f, 0.03f, Mathf.Max(0.05f, Vector3.Distance(head, tail)));
            }
        }
    }
}
