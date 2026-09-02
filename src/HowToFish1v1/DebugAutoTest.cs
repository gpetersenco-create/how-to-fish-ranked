using System.Linq;
using HowToFish1v1.Core;
using HowToFish1v1.Match;
using HowToFish1v1.Net;
using UnityEngine;

namespace HowToFish1v1
{
    /// <summary>
    /// Testing only (config Debug/AutoSoloMatch). On the host, once the local player exists: open the mode, pick the first gun,
    /// start, kill the local player during the first live round to force a reset/respawn, then quit after the next live phase.
    /// Everything it does is logged so the run can be checked from BepInEx/LogOutput.log.
    /// </summary>
    internal static class DebugAutoTest
    {
        private enum Step { WaitPlayer, Started, WaitLive1, KilledSelf, WaitLive2, Quit, Done }

        private static Step _step = Step.WaitPlayer;
        private static float _at;
        private static int _liveCount;
        private static MatchPhase _prev = MatchPhase.Inactive;

        public static void Update()
        {
            if (!Plugin.Cfg.AutoSoloMatch.Value || _step == Step.Done) return;
            var phase = ModState.Phase;
            if (phase == MatchPhase.Live && _prev != MatchPhase.Live) _liveCount++;
            _prev = phase;
            bool ffa = ClientMatchView.IsFfa;

            switch (_step)
            {
                case Step.WaitPlayer:
                    if (!Player.LocalPlayer || !ModNet.IsHost) return;
                    if (_at == 0f) { _at = Time.time + 6f; return; }
                    if (Time.time < _at) return;
                    Plugin.Host.Open();
                    Plugin.Host.SetMode((MatchMode)Mathf.Clamp(Plugin.Cfg.AutoSoloMode.Value, 0, 3));
                    Plugin.Host.SetMap(Plugin.Cfg.AutoSoloMap.Value);
                    var gun = LoadoutService.Weapons().FirstOrDefault();
                    var ids = gun ? new[] { gun.ID } : new byte[0];
                    Plugin.Log.LogInfo($"AutoTest: open + loadout [{(gun ? gun.name : "none")}] + start");
                    Plugin.Host.SetLocalLoadout(ids, true, RankService.Points);
                    Plugin.Host.Start();
                    _step = Step.Started;
                    _at = Time.time + 30f;
                    break;
                case Step.Started:
                    if (_liveCount >= 1) { _step = Step.WaitLive1; _at = Time.time + 4f; }
                    else if (Time.time > _at) { Plugin.Log.LogError("AutoTest: never went live"); _step = Step.Done; }
                    break;
                case Step.WaitLive1:
                    if (Time.time < _at) return;
                    LogPlayer("before self-kill");
                    Plugin.Log.LogInfo("AutoTest: killing local player");
                    Player.LocalPlayer.Vitals.TakeDamage(1000, Vector3.zero, Vector3.zero, true);
                    _step = Step.KilledSelf;
                    _at = Time.time + (ffa ? 8f : 30f);
                    break;
                case Step.KilledSelf:
                    if (!ffa && _liveCount >= 2) { _step = Step.WaitLive2; _at = Time.time + 4f; }
                    else if (ffa && Time.time > _at) { _step = Step.WaitLive2; _at = Time.time; }
                    else if (!ffa && Time.time > _at) { Plugin.Log.LogError("AutoTest: round 2 never went live"); _step = Step.Done; }
                    break;
                case Step.WaitLive2:
                    if (Time.time < _at) return;
                    LogPlayer(ffa ? "after respawn" : "round 2 live");
                    Plugin.Log.LogInfo("AutoTest: quitting");
                    Plugin.Host.Quit();
                    _step = Step.Quit;
                    _at = Time.time + 14f;
                    break;
                case Step.Quit:
                    if (Time.time < _at) return;
                    LogPlayer("after quit");
                    Plugin.Log.LogInfo($"AutoTest: done. island={OnlineIslandManager.CurIsland} curIsland={(Island.CurIsland ? Island.CurIsland.name : "none")} arenaBuilt={Arena.ArenaBuilder.IsBuilt}");
                    _step = Step.Done;
                    break;
            }
        }

        private static void LogPlayer(string when)
        {
            var p = Player.LocalPlayer;
            if (!p) { Plugin.Log.LogInfo($"AutoTest[{when}]: no local player"); return; }
            string held = p.Holding.HeldItem ? p.Holding.HeldItem.name : "nothing";
            int ammo = p.Holding.HeldItem is Weapon w ? w.Ammo : -1;
            Plugin.Log.LogInfo($"AutoTest[{when}]: pos={p.Transform.position} vel={(p.Rigidbody ? p.Rigidbody.linearVelocity : Vector3.zero)} hp={p.Vitals.Health} full={p.Vitals.Fullness} fire={p.Vitals._syncedFire.Value} poison={p.Vitals._syncedPoison.Value} dead={p.Dying.IsDead} held={held} ammo={ammo} phase={ModState.Phase}");
        }
    }
}
