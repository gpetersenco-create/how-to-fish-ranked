using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using HowToFish1v1.Core;
using HowToFish1v1.Net;
using HowToFish1v1.Net.Proto2;
using HowToFish1v1.UI;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// Client-side reactions to the host's kill announcements: medal and killstreak popups, streak rewards for the local
    /// player (UAV radar at 3, a fresh magazine at 5, one-shot at 7), one-in-the-chamber ammo, announcer lines, and the
    /// per-player tally the results screen shows.
    /// </summary>
    public static class MatchEvents
    {
        public sealed class Tally { public int Kills, Deaths, BestStreak, Streak; public readonly List<string> Medals = new List<string>(); }

        private static readonly Dictionary<int, Tally> _tally = new Dictionary<int, Tally>();
        public static IReadOnlyDictionary<int, Tally> Tallies => _tally;
        public static float UavUntil { get; private set; } = -1f;
        public static bool UavActive => ModState.IsActive && Time.unscaledTime < UavUntil;
        public static bool OneShotActive { get; private set; }

        public static void Init()
        {
            ModNet.KillFeedReceived += OnKill;
        }

        /// <summary>Forget the previous match's tally (called when a match starts).</summary>
        public static void ResetTally()
        {
            _tally.Clear();
            UavUntil = -1f;
            OneShotActive = false;
        }

        public static Tally Of(int id)
        {
            if (!_tally.TryGetValue(id, out var t)) { t = new Tally(); _tally[id] = t; }
            return t;
        }

        private static void OnKill(KillFeedBroadcast k)
        {
            int me = ModState.LocalOwnerId;
            var medals = string.IsNullOrEmpty(k.Medals) ? new string[0] : k.Medals.Split(',');
            if (k.VictimId >= 0)
            {
                var v = Of(k.VictimId);
                v.Deaths++; v.Streak = 0;
                if (k.VictimId == me) OneShotActive = false;
            }
            if (!k.Suicide && k.KillerId >= 0 && k.KillerId != k.VictimId)
            {
                var t = Of(k.KillerId);
                t.Kills++; t.Streak = k.Streak; t.BestStreak = Mathf.Max(t.BestStreak, k.Streak);
                foreach (var m in medals) if (!string.IsNullOrEmpty(m)) t.Medals.Add(m);
            }
            if (k.KillerId != me || k.Suicide || k.KillerId == k.VictimId) return;

            // Our own kill: medals as popups, streak rewards, announcer.
            foreach (var m in medals)
            {
                if (string.IsNullOrEmpty(m)) continue;
                Hud.Popup(m);
                if (m == Streaks.FirstBlood) Announcer.Play("firstblood");
            }
            if (k.Streak == Streaks.Uav) { UavUntil = Time.unscaledTime + 12f; Announcer.Play("streak3"); }
            if (k.Streak == Streaks.ExtraMag) { LoadoutService.RefillLocalAmmo(); Announcer.Play("streak5"); }
            if (k.Streak == Streaks.OneShot) { OneShotActive = true; Announcer.Play("streak7"); }
            if (ClientMatchView.HasState && MatchModes.OneBullet((MatchMode)ClientMatchView.Latest.Mode)) OneInTheChamber.AddBullet();
        }
    }

    /// <summary>One in the Chamber on the client: one bullet, one more per kill, no reloading.</summary>
    public static class OneInTheChamber
    {
        public static bool Active => ModState.IsActive && ClientMatchView.HasState && MatchModes.OneBullet((MatchMode)ClientMatchView.Latest.Mode);

        private static IEnumerable<Weapon> MyWeapons()
        {
            var p = Player.LocalPlayer;
            if (!p) yield break;
            if (p.Holding && p.Holding.HeldItem is Weapon held) yield return held;
            if (p.Inventory != null) foreach (var kv in p.Inventory._items) if (kv.Value is Weapon w) yield return w;
        }

        /// <summary>Round start: exactly one bullet.</summary>
        public static void SetOneBullet()
        {
            foreach (var w in MyWeapons())
            {
                try { var t = Traverse.Create(w); t.Property("Ammo").SetValue(1); t.Field("_isReloading").SetValue(false); t.Field("_queueReload").SetValue(false); } catch (System.Exception) { }
            }
        }

        public static void AddBullet()
        {
            foreach (var w in MyWeapons())
            {
                try { var t = Traverse.Create(w); t.Property("Ammo").SetValue(w.Ammo + 1); } catch (System.Exception) { }
            }
            Hud.Popup("+1 BULLET");
        }
    }

    [HarmonyPatch]
    internal static class VariantPatches
    {
        // One in the Chamber: the magazine holds one round, and reloading is off.
        [HarmonyPatch(typeof(Attachments), nameof(Attachments.AmmoPerMag), MethodType.Getter)]
        [HarmonyPostfix]
        private static void OneRound(ref int __result)
        {
            if (OneInTheChamber.Active) __result = 1;
        }

        [HarmonyPatch(typeof(Weapon), "LocalReload")]
        [HarmonyPrefix]
        private static bool NoReload() => !OneInTheChamber.Active;
    }
}
