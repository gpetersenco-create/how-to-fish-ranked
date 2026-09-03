using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using HowToFish1v1.Core;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// Dead in a team round with teammates still alive: follow one of them over the shoulder until the round ends.
    /// Runs after the killcam (which owns the camera first); click or press Space to switch teammate.
    /// </summary>
    public static class Spectate
    {
        private static int _targetId = -1;
        private static Vector3 _pos;
        private static Quaternion _rot;
        private static bool _snapped;

        public static int TargetId => _targetId;
        public static string TargetName
        {
            get
            {
                var p = Target();
                return p ? p.SteamName : "";
            }
        }

        public static bool Active
        {
            get
            {
                var me = Player.LocalPlayer;
                if (!ModState.IsActive || !ClientMatchView.HasState || !me || !me.Dying.IsDead) return false;
                var mode = (MatchMode)ClientMatchView.Latest.Mode;
                if (MatchModes.IsFfa(mode) || MatchModes.IsSolo(mode)) return false;
                if (ModState.Phase != MatchPhase.Live || KillCam.Active) return false;
                return Target() != null;
            }
        }

        private static Player Target()
        {
            if (_targetId < 0) return null;
            var p = PlayerManager.Players.FirstOrDefault(x => x && x.OwnerId == _targetId);
            return p && !p.Dying.IsDead && p.Transform ? p : null;
        }

        private static List<Player> Teammates()
        {
            int me = ModState.LocalOwnerId, team = ClientMatchView.MyTeam;
            var ids = new HashSet<int>(ClientMatchView.Players.Where(e => e.Team == team && e.Id != me).Select(e => e.Id));
            return PlayerManager.Players.Where(p => p && ids.Contains(p.OwnerId) && !p.Dying.IsDead && p.Transform).OrderBy(p => p.OwnerId).ToList();
        }

        public static void Update()
        {
            var me = Player.LocalPlayer;
            if (!ModState.IsActive || !me || !me.Dying.IsDead || ModState.Phase != MatchPhase.Live || KillCam.Active)
            {
                _targetId = -1; _snapped = false;
                return;
            }
            var mode = (MatchMode)ClientMatchView.Latest.Mode;
            if (MatchModes.IsFfa(mode) || MatchModes.IsSolo(mode)) { _targetId = -1; return; }
            var mates = Teammates();
            if (mates.Count == 0) { _targetId = -1; return; }
            if (Target() == null) { _targetId = mates[0].OwnerId; _snapped = false; }
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space))
            {
                int i = mates.FindIndex(p => p.OwnerId == _targetId);
                _targetId = mates[(i + 1) % mates.Count].OwnerId;
                _snapped = false;
            }
        }

        /// <summary>Drives the death camera while spectating (called from the death cam's LateUpdate postfix).</summary>
        public static void ApplyDeathCam(PlayerDeathCam deathCam)
        {
            if (!Active || !deathCam || !deathCam.Owner.IsLocalClient) return;
            var cam = Traverse.Create(deathCam).Field<Camera>("_deathCam").Value;
            var target = Target();
            if (!cam || !target) return;
            var head = target.CamObject ? target.CamObject : target.Transform;
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = target.Transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 anchor = head.position + Vector3.up * 0.15f;
            Vector3 wanted = anchor - forward * 2.6f + Vector3.up * 0.6f + right * 0.6f;
            if (Physics.Linecast(anchor, wanted, out var hit, GameInfo.LevelLayer)) wanted = hit.point + (anchor - wanted).normalized * 0.25f;
            Quaternion wantedRot = Quaternion.LookRotation((anchor + head.forward * 10f - wanted).normalized, Vector3.up);
            if (!_snapped) { _pos = wanted; _rot = wantedRot; _snapped = true; }
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 10f);
            _pos = Vector3.Lerp(_pos, wanted, k);
            _rot = Quaternion.Slerp(_rot, wantedRot, k);
            cam.transform.SetPositionAndRotation(_pos, _rot);
        }
    }
}
