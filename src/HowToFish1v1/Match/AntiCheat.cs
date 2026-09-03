using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FishNet;
using FishNet.Connection;
using HarmonyLib;
using HowToFish1v1.Core;
using HowToFish1v1.Net;
using HowToFish1v1.Net.Proto2;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// Host-side cheat detection during ranked matches. Every check looks for behaviour a human cannot produce and
    /// needs several strikes inside a window before it acts, so lag and lucky flicks do not trip it:
    ///  - silent aim: hits that land far from where the shooter was actually looking;
    ///  - snap aim: near-instant turns straight onto a target that then connect;
    ///  - rapid fire: shots well faster than the gun allows;
    ///  - damage hacks: a hit reporting more damage than the gun can do (one strike is enough);
    ///  - speed hacks: sustained running far above the game's speed.
    /// A caught player is announced to everyone ("CAUGHT BY THE FIREHORN... stop cheating") and kicked from the session.
    /// </summary>
    public static class AntiCheat
    {
        public const string Message = "CAUGHT BY THE FIREHORN... stop cheating";

        private sealed class Record
        {
            public readonly List<float> SilentAim = new List<float>(), Snaps = new List<float>(), Rapid = new List<float>();
            public float LastShot = -10f;
            public Vector3 LastPos; public float LastPosT = -1f, FastSince = -1f;
            public int Strikes;
            public bool Punished;
        }

        private static readonly Dictionary<int, Record> _records = new Dictionary<int, Record>();
        private static readonly Dictionary<Weapon, float> _minInterval = new Dictionary<Weapon, float>();

        private static bool On => Plugin.Cfg.AntiCheat.Value && ModNet.IsHost && ModState.IsActive;

        private static Record Rec(int id)
        {
            if (!_records.TryGetValue(id, out var r)) { r = new Record(); _records[id] = r; }
            return r;
        }

        /// <summary>A hit reported to the server (from the game's HitPlayer RPC, before it is applied).</summary>
        public static void OnHit(Player attacker, Player victim, int damage, Vector3 point)
        {
            if (!On || !attacker || !victim || attacker == victim) return;
            var rec = Rec(attacker.OwnerId);
            if (rec.Punished) return;
            float now = Time.unscaledTime;
            var head = attacker.CamObject ? attacker.CamObject : attacker.Transform;
            if (head)
            {
                Vector3 to = point - head.position;
                float dist = to.magnitude;
                // Silent aim: the shot landed somewhere the shooter was not looking.
                if (dist > 2.5f && Vector3.Angle(head.forward, to) > 16f) { rec.SilentAim.Add(now); Note(attacker, $"hit {Vector3.Angle(head.forward, to):0}° off the aim line"); }
                // Snap: the view turned faster than a human flick in the instant before the hit.
                if (Recorder.TryGet(attacker.OwnerId, now - 0.12f, out _, out var before) && Recorder.TryGet(attacker.OwnerId, now, out _, out var at))
                {
                    float degPerSec = Quaternion.Angle(before, at) / 0.12f;
                    if (degPerSec > 1400f && dist > 4f) { rec.Snaps.Add(now); Note(attacker, $"snapped {degPerSec:0}°/s onto the target"); }
                }
            }
            // Damage: more than the gun can possibly do.
            int max = MaxDamage(attacker);
            if (max > 0 && damage > max * 1.6f + 5f) { rec.Strikes += 3; Note(attacker, $"reported {damage} damage, gun max {max}"); }
            Evaluate(attacker, rec);
        }

        /// <summary>Every shot the host sees (own and remote players).</summary>
        public static void OnShot(Weapon w)
        {
            if (!On || !w || !w.Holder) return;
            var rec = Rec(w.Holder.OwnerId);
            if (rec.Punished) return;
            float now = Time.unscaledTime;
            float min = MinInterval(w);
            float gap = now - rec.LastShot;
            rec.LastShot = now;
            if (min > 0.05f && gap > 0.004f && gap < min * 0.5f) { rec.Rapid.Add(now); Note(w.Holder, $"fired every {gap * 1000f:0} ms, gun min {min * 1000f:0} ms"); }
            Evaluate(w.Holder, rec);
        }

        /// <summary>Speed check for everyone but the host.</summary>
        public static void Update()
        {
            if (!On) { if (_records.Count > 0) _records.Clear(); return; }
            float now = Time.unscaledTime;
            foreach (var p in PlayerManager.Players)
            {
                if (!p || p.Owner == null || p.Owner.IsLocalClient || !p.Transform || p.Dying.IsDead) continue;
                var rec = Rec(p.OwnerId);
                if (rec.Punished) continue;
                Vector3 pos = p.Transform.position;
                if (rec.LastPosT > 0f)
                {
                    float dt = now - rec.LastPosT;
                    Vector3 d = Vector3.ProjectOnPlane(pos - rec.LastPos, Vector3.up);
                    if (dt > 0.01f && d.magnitude < 20f)   // a jump of 20 m in one frame is a teleport, not running
                    {
                        float speed = d.magnitude / dt;
                        if (speed > 16f) { if (rec.FastSince < 0f) rec.FastSince = now; }
                        else rec.FastSince = -1f;
                        if (rec.FastSince > 0f && now - rec.FastSince > 1.5f) { rec.Strikes += 3; rec.FastSince = -1f; Note(p, $"moved at {speed:0} m/s for over a second"); Evaluate(p, rec); }
                    }
                    else rec.FastSince = -1f;
                }
                rec.LastPos = pos; rec.LastPosT = now;
            }
        }

        private static void Evaluate(Player p, Record rec)
        {
            float now = Time.unscaledTime;
            rec.SilentAim.RemoveAll(t => now - t > 60f);
            rec.Snaps.RemoveAll(t => now - t > 60f);
            rec.Rapid.RemoveAll(t => now - t > 20f);
            string reason = null;
            if (rec.SilentAim.Count >= 4) reason = "silent aim";
            else if (rec.Snaps.Count >= 6) reason = "aimbot snaps";
            else if (rec.Rapid.Count >= 8) reason = "rapid fire";
            else if (rec.Strikes >= 3) reason = "impossible damage or speed";
            if (reason != null) Punish(p, rec, reason);
        }

        private static void Punish(Player p, Record rec, string reason)
        {
            rec.Punished = true;
            Plugin.Log.LogWarning($"ANTI-CHEAT: {p.SteamName} ({p.OwnerId}) caught: {reason}");
            ModNet.BroadcastCheat(new CheatBroadcast { OwnerId = p.OwnerId, Name = p.SteamName ?? "", Reason = reason });
            if (p.Owner != null && !p.Owner.IsLocalClient) Plugin.Instance.StartCoroutine(KickLater(p.Owner));
        }

        private static IEnumerator KickLater(NetworkConnection conn)
        {
            yield return new WaitForSecondsRealtime(2.5f);   // let the message land on their screen first
            try { conn.Disconnect(true); } catch (System.Exception e) { Plugin.Log.LogWarning("Kick failed: " + e.Message); }
        }

        private static void Note(Player p, string what)
        {
            Plugin.Log.LogInfo($"Anti-cheat note: {p.SteamName}: {what}");
        }

        private static int MaxDamage(Player p)
        {
            try
            {
                if (!(p.Holding?.HeldItem is Weapon w) || !w.Attachments) return 0;
                var ups = Traverse.Create(w.Attachments).Field<BulletUpgrade[]>("_bulletUpgrades").Value;
                int max = 0;
                if (ups != null) foreach (var u in ups) if (u != null) max = Mathf.Max(max, u.Damage);
                float mult = Mathf.Max(1f, Plugin.Cfg.DamageMultiplier.Value);
                return Mathf.RoundToInt(max * mult);
            }
            catch (System.Exception) { return 0; }
        }

        private static float MinInterval(Weapon w)
        {
            if (_minInterval.TryGetValue(w, out float v)) return v;
            try { v = Traverse.Create(w).Field<float>("_timeBetweenShots").Value; } catch (System.Exception) { v = 0f; }
            if (ModAttachments.GunOf(w.Holder ? w.Holder.OwnerId : -1, w.ID) is LoadoutGun g && g.Switch) v *= ModAttachments.SwitchRateMultiplier;
            _minInterval[w] = v;
            return v;
        }
    }
}
