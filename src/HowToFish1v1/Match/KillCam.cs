using System.Linq;
using HarmonyLib;
using HowToFish1v1.Net;
using HowToFish1v1.Net.Proto2;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// Killcam. After the local player dies to someone, the camera replays the killer's recorded viewpoint from the seconds
    /// before the kill, then follows the killer live until the local player respawns. At match end everyone watches the
    /// final kill the same way. Drives whichever camera the game currently uses (death cam while dead, player cam while alive).
    /// </summary>
    public static class KillCam
    {
        private const float ReplayLead = 4f;      // seconds before the kill to start from
        private const float ReplayTail = 0.6f;    // seconds after the kill to keep showing
        private const float LiveFollowMax = 8f;

        private enum Mode { Off, Replay, LiveFollow }

        private static Mode _mode = Mode.Off;
        private static bool _final;
        private static int _killerId = -1;
        private static float _killTime;
        private static float _startedAt;
        private static Vector3 _pos;
        private static Quaternion _rot;
        private static bool _snapped;

        // The most recent kill seen this match, for the final killcam.
        private static int _lastKillerId = -1, _lastVictimId = -1;
        private static float _lastKillTime;
        private static string _lastKillerName = "", _lastVictimName = "";

        public static bool Active => _mode != Mode.Off;
        public static bool IsFinal => Active && _final;
        public static bool IsReplay => _mode == Mode.Replay;
        public static string KillerName { get; private set; } = "";
        public static string VictimName { get; private set; } = "";
        public static string KillerInfo { get; private set; } = "";

        public static void Init()
        {
            ModNet.KillFeedReceived += OnKill;
        }

        private static void OnKill(KillFeedBroadcast k)
        {
            if (k.Suicide || k.KillerId == -1 || k.KillerId == k.VictimId) return;
            _lastKillerId = k.KillerId; _lastVictimId = k.VictimId; _lastKillTime = Time.unscaledTime;
            _lastKillerName = k.Killer ?? ""; _lastVictimName = k.Victim ?? "";
            if (k.VictimId == ModState.LocalOwnerId) Begin(k.KillerId, k.Killer, k.Victim, Time.unscaledTime, final: false);
        }

        /// <summary>Called when the match ends: replay the winning kill for everyone.</summary>
        public static void StartFinal()
        {
            if (_lastKillerId == -1 || Time.unscaledTime - _lastKillTime > 10f) return;
            Begin(_lastKillerId, _lastKillerName, _lastVictimName, _lastKillTime, final: true);
        }

        private static void Begin(int killerId, string killerName, string victimName, float killTime, bool final)
        {
            _killerId = killerId;
            _killTime = killTime;
            _final = final;
            _startedAt = Time.unscaledTime;
            _snapped = false;
            _mode = Mode.Replay;
            KillerName = killerName ?? "";
            VictimName = victimName ?? "";
            var entry = ClientMatchView.Players.FirstOrDefault(p => p.Id == killerId);
            string rank = entry.Name != null ? RankService.Ladder.TierName(entry.RankPoints) : "";
            string gun = entry.Name != null ? LoadoutService.Summary(entry.Loadout) : "";
            KillerInfo = string.IsNullOrEmpty(rank) ? gun : $"{rank.ToUpperInvariant()}   |   {gun}";
        }

        public static void Stop()
        {
            _mode = Mode.Off;
            _final = false;
            _killerId = -1;
        }

        public static void OnMatchLeftEndPhase() { if (_final) Stop(); }

        /// <summary>Camera pose for this frame, or false when the killcam is not running.</summary>
        private static bool Pose(out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            if (!Active) return false;
            var me = Player.LocalPlayer;
            if (!me) { Stop(); return false; }
            if (!_final && !me.Dying.IsDead) { Stop(); return false; }   // respawned: give the camera back

            if (_mode == Mode.Replay)
            {
                float elapsed = Time.unscaledTime - _startedAt;
                float replayT = _killTime - ReplayLead + elapsed;
                if (replayT > _killTime + ReplayTail)
                {
                    if (_final) { Stop(); return false; }
                    _mode = Mode.LiveFollow;
                    _startedAt = Time.unscaledTime;
                    _snapped = false;
                }
                else if (Recorder.TryGet(_killerId, replayT, out var rp, out var rr))
                {
                    pos = rp; rot = rr;
                    return true;
                }
                else
                {
                    _mode = Mode.LiveFollow;
                    _startedAt = Time.unscaledTime;
                }
            }

            // Live follow: behind the killer's shoulder.
            if (Time.unscaledTime - _startedAt > LiveFollowMax) { Stop(); return false; }
            var killer = PlayerManager.Players.FirstOrDefault(p => p && p.OwnerId == _killerId);
            if (!killer || killer.Dying.IsDead || !killer.Transform || !killer.Transform.gameObject.activeInHierarchy) { Stop(); return false; }
            var head = killer.CamObject ? killer.CamObject : killer.Transform;
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = killer.Transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 anchor = head.position + Vector3.up * 0.15f;
            Vector3 wanted = anchor - forward * 2.4f + Vector3.up * 0.55f + right * 0.55f;
            if (Physics.Linecast(anchor, wanted, out var hit, GameInfo.LevelLayer))
                wanted = hit.point + (anchor - wanted).normalized * 0.25f;
            Quaternion wantedRot = Quaternion.LookRotation((anchor + head.forward * 10f - wanted).normalized, Vector3.up);
            if (!_snapped) { _pos = wanted; _rot = wantedRot; _snapped = true; }
            float t = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 10f);
            _pos = Vector3.Lerp(_pos, wanted, t);
            _rot = Quaternion.Slerp(_rot, wantedRot, t);
            pos = _pos; rot = _rot;
            return true;
        }

        /// <summary>From the death camera's LateUpdate (Harmony postfix) while the local player is dead.</summary>
        public static void ApplyDeathCam(PlayerDeathCam deathCam)
        {
            if (!Active || !deathCam || !deathCam.Owner.IsLocalClient) return;
            var cam = Traverse.Create(deathCam).Field<Camera>("_deathCam").Value;
            if (cam && Pose(out var p, out var r)) cam.transform.SetPositionAndRotation(p, r);
        }

        /// <summary>From the player camera's Update (Harmony postfix) while the local player is alive (final killcam).</summary>
        public static void ApplyPlayerCam(PlayerCamera playerCam)
        {
            if (!Active || !_final || !playerCam) return;
            var cam = playerCam.Cam;
            if (cam && Pose(out var p, out var r)) cam.transform.SetPositionAndRotation(p, r);
        }
    }
}
