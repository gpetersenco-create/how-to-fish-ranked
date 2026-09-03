using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using HowToFish1v1.Net;
using HowToFish1v1.Net.Proto2;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// CoD-style killcam. After the local player dies to someone, the camera replays the killer's recorded first-person view
    /// from a few seconds before the kill, slowing to half speed for the final moments, with a ghost of the victim showing
    /// where they were. Afterwards it follows the killer live until the local player respawns. At match end everyone watches
    /// the final kill the same way. Drives whichever camera the game currently uses (death cam while dead, player cam while alive).
    /// </summary>
    public static class KillCam
    {
        private const float ReplayLead = 5f;       // seconds before the kill to start from
        private const float SlowWindow = 1.5f;     // seconds before the kill where the replay slows down
        private const float SlowSpeed = 0.5f;
        private const float ReplayTail = 0.3f;     // seconds after the kill to keep showing
        private const float LiveFollowMax = 8f;
        private const float EyeForward = 0.12f;    // keep the killer's own head mesh behind the near plane

        private enum Mode { Off, Replay, LiveFollow }

        private static Mode _mode = Mode.Off;
        private static bool _final;
        private static int _killerId = -1, _victimId = -1;
        private static float _killTime;
        private static float _replayT;
        private static float _startedAt;
        private static Vector3 _pos;
        private static Quaternion _rot;
        private static bool _snapped;

        private static GameObject _ghost;
        private static GameObject _viewGun;
        private static GameObject _flash;
        private static float _flashUntil;
        private static string _fireSound = "";
        private static float _lastShotCheck;
        private static readonly List<Renderer> _hiddenRenderers = new List<Renderer>();
        private static Camera _clipCam;
        private static float _savedNearClip = -1f;

        // The most recent kill seen this match, for the final killcam.
        private static int _lastKillerId = -1, _lastVictimId = -1;
        private static float _lastKillTime;
        private static string _lastKillerName = "", _lastVictimName = "";

        public static bool Active => _mode != Mode.Off;
        public static bool IsFinal => Active && _final;
        public static bool IsReplay => _mode == Mode.Replay;
        public static bool SlowMotion => IsReplay && _killTime - _replayT < SlowWindow && _replayT <= _killTime + ReplayTail;
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
            if (k.VictimId == ModState.LocalOwnerId) Begin(k.KillerId, k.VictimId, k.Killer, k.Victim, Time.unscaledTime, final: false);
        }

        /// <summary>Called when the match ends: replay the winning kill for everyone.</summary>
        public static void StartFinal()
        {
            if (_lastKillerId == -1 || Time.unscaledTime - _lastKillTime > 10f) return;
            Begin(_lastKillerId, _lastVictimId, _lastKillerName, _lastVictimName, _lastKillTime, final: true);
        }

        private static void Begin(int killerId, int victimId, string killerName, string victimName, float killTime, bool final)
        {
            Cleanup();
            _killerId = killerId;
            _victimId = victimId;
            _killTime = killTime;
            _final = final;
            _startedAt = Time.unscaledTime;
            _replayT = killTime - ReplayLead;
            _snapped = false;
            _mode = Mode.Replay;
            KillerName = killerName ?? "";
            VictimName = victimName ?? "";
            var entry = ClientMatchView.Players.FirstOrDefault(p => p.Id == killerId);
            string rank = entry.Name != null ? RankService.Ladder.TierName(entry.RankPoints) : "";
            string gun = entry.Name != null ? LoadoutService.Summary(entry.Loadout) : "";
            KillerInfo = string.IsNullOrEmpty(rank) ? gun : $"{rank.ToUpperInvariant()}   |   {gun}";
            BuildViewGun();
            HideKiller();
            _lastShotCheck = _replayT;
        }

        /// <summary>
        /// A render-only copy of the killer's held gun, parented so it keeps the same offset from the eyes that the real gun
        /// has right now. During the replay the copy rides along with the replayed eye pose, reading as the first-person gun.
        /// </summary>
        private static void BuildViewGun()
        {
            var killer = PlayerManager.Players.FirstOrDefault(p => p && p.OwnerId == _killerId);
            if (!killer || !killer.Transform) return;
            var item = killer.Holding ? killer.Holding.HeldItem : null;
            var head = killer.CamObject ? killer.CamObject : killer.Transform;
            if (!item || !head) return;
            _viewGun = new GameObject("HTF1v1_KillcamGun");
            _viewGun.transform.SetPositionAndRotation(head.position, head.rotation);
            foreach (var r in item.GetComponentsInChildren<Renderer>(true))
            {
                if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var copy = new GameObject(r.name);
                copy.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                copy.transform.localScale = r.transform.lossyScale;
                if (r is SkinnedMeshRenderer smr)
                {
                    var mesh = new Mesh();
                    smr.BakeMesh(mesh);
                    copy.AddComponent<MeshFilter>().sharedMesh = mesh;
                    copy.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                    copy.transform.localScale = Vector3.one;
                }
                else if (r is MeshRenderer mr)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (!mf || !mf.sharedMesh) { Object.Destroy(copy); continue; }
                    copy.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    copy.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
                }
                else { Object.Destroy(copy); continue; }
                copy.transform.SetParent(_viewGun.transform, true);
            }
            if (item is Weapon w && w.Attachments)
            {
                _fireSound = w.Attachments.FireSound ?? "";
                var fp = w.Attachments.FirePoint;
                _flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _flash.name = "HTF1v1_MuzzleFlash";
                Object.Destroy(_flash.GetComponent<Collider>());
                _flash.transform.localScale = Vector3.one * 0.12f;
                var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
                var c = new Color(1f, 0.85f, 0.35f, 1f);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                mat.color = c;
                _flash.GetComponent<MeshRenderer>().sharedMaterial = mat;
                _flash.transform.SetPositionAndRotation(fp ? fp.position : head.position + head.forward * 0.8f, head.rotation);
                _flash.transform.SetParent(_viewGun.transform, true);
                _flash.SetActive(false);
            }
        }

        private static void PlaceViewGun(Vector3 eyePos, Quaternion eyeRot)
        {
            if (_viewGun) _viewGun.transform.SetPositionAndRotation(eyePos, eyeRot);
        }

        /// <summary>Replays any shot the killer fired since the last frame of replay time: flash plus the gun's own sound.</summary>
        private static void ReplayShots()
        {
            if (Recorder.FiredBetween(_killerId, _lastShotCheck, _replayT))
            {
                _flashUntil = Time.unscaledTime + 0.07f;
                if (_flash) _flash.SetActive(true);
                if (!string.IsNullOrEmpty(_fireSound))
                {
                    try { AudioManager.PlayGlobalClip(_fireSound); } catch (System.Exception) { }
                }
            }
            else if (_flash && _flash.activeSelf && Time.unscaledTime > _flashUntil) _flash.SetActive(false);
            _lastShotCheck = _replayT;
        }

        public static void Stop()
        {
            _mode = Mode.Off;
            _final = false;
            _killerId = -1;
            Cleanup();
        }

        public static void OnMatchLeftEndPhase() { if (_final) Stop(); }

        private static void Cleanup()
        {
            foreach (var r in _hiddenRenderers) if (r) r.enabled = true;
            _hiddenRenderers.Clear();
            if (_ghost) Object.Destroy(_ghost);
            _ghost = null;
            if (_viewGun) Object.Destroy(_viewGun);
            _viewGun = null; _flash = null;
            if (_clipCam && _savedNearClip > 0f) _clipCam.nearClipPlane = _savedNearClip;
            _clipCam = null; _savedNearClip = -1f;
        }

        /// <summary>We look through the killer's eyes, so their own body must not be drawn during the replay.</summary>
        private static void HideKiller()
        {
            var killer = PlayerManager.Players.FirstOrDefault(p => p && p.OwnerId == _killerId);
            if (!killer || !killer.Transform) return;
            foreach (var r in killer.Transform.GetComponentsInChildren<Renderer>(true))
                if (r && r.enabled) { r.enabled = false; _hiddenRenderers.Add(r); }
        }

        private static void ShowKiller()
        {
            foreach (var r in _hiddenRenderers) if (r) r.enabled = true;
            _hiddenRenderers.Clear();
        }

        private static void UpdateGhost(bool show)
        {
            if (!show || !Recorder.TryGet(_victimId, _replayT, out var headPos, out var headRot))
            {
                if (_ghost) _ghost.SetActive(false);
                return;
            }
            if (!_ghost)
            {
                _ghost = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                _ghost.name = "HTF1v1_KillcamGhost";
                Object.Destroy(_ghost.GetComponent<Collider>());
                _ghost.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
                var mr = _ghost.GetComponent<MeshRenderer>();
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                var mat = new Material(shader);
                var c = new Color(1f, 0.82f, 0.3f, 0.55f);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                mat.color = c;
                if (mat.HasProperty("_Surface")) { mat.SetFloat("_Surface", 1f); mat.SetOverrideTag("RenderType", "Transparent"); mat.renderQueue = 3000; mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); mat.SetInt("_ZWrite", 0); }
                mr.sharedMaterial = mat;
            }
            _ghost.SetActive(true);
            // Head pose recorded; the capsule center sits about 0.75 m below the eyes.
            Vector3 fwd = Vector3.ProjectOnPlane(headRot * Vector3.forward, Vector3.up);
            _ghost.transform.SetPositionAndRotation(headPos - Vector3.up * 0.75f, fwd.sqrMagnitude > 0.001f ? Quaternion.LookRotation(fwd.normalized, Vector3.up) : Quaternion.identity);
        }

        /// <summary>Camera pose for this frame, or false when the killcam is not running.</summary>
        private static bool Pose(Camera cam, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            if (!Active) return false;
            var me = Player.LocalPlayer;
            if (!me) { Stop(); return false; }
            if (!_final && !me.Dying.IsDead) { Stop(); return false; }   // respawned: give the camera back

            if (_mode == Mode.Replay)
            {
                float speed = (_killTime - _replayT < SlowWindow) ? SlowSpeed : 1f;
                _replayT += Time.unscaledDeltaTime * speed;
                if (_replayT > _killTime + ReplayTail)
                {
                    UpdateGhost(false);
                    if (_viewGun) _viewGun.SetActive(false);
                    if (_final) { Stop(); return false; }
                    _mode = Mode.LiveFollow;
                    _startedAt = Time.unscaledTime;
                    _snapped = false;
                    ShowKiller();
                }
                else if (Recorder.TryGet(_killerId, _replayT, out var rp, out var rr))
                {
                    if (cam && _clipCam != cam) { if (_clipCam && _savedNearClip > 0f) _clipCam.nearClipPlane = _savedNearClip; _clipCam = cam; _savedNearClip = cam.nearClipPlane; cam.nearClipPlane = 0.04f; }
                    UpdateGhost(true);
                    PlaceViewGun(rp, rr);
                    ReplayShots();
                    pos = rp + rr * Vector3.forward * EyeForward;
                    rot = rr;
                    return true;
                }
                else
                {
                    UpdateGhost(false);
                    _mode = Mode.LiveFollow;
                    _startedAt = Time.unscaledTime;
                    ShowKiller();
                }
            }

            // Live follow: behind the killer's shoulder.
            if (_clipCam && _savedNearClip > 0f) { _clipCam.nearClipPlane = _savedNearClip; _clipCam = null; _savedNearClip = -1f; }
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
            if (cam && Pose(cam, out var p, out var r)) cam.transform.SetPositionAndRotation(p, r);
        }

        /// <summary>From the player camera's Update (Harmony postfix) while the local player is alive (final killcam).</summary>
        public static void ApplyPlayerCam(PlayerCamera playerCam)
        {
            if (!Active || !_final || !playerCam) return;
            var cam = playerCam.Cam;
            if (cam && Pose(cam, out var p, out var r)) cam.transform.SetPositionAndRotation(p, r);
        }
    }
}
