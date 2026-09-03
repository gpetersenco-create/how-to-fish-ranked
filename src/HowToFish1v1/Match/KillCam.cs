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
        private const float SlowLeadBeforeShot = 0.5f;   // slow motion starts this long before the killing shot
        private const float SlowFallback = 1.5f;         // if no shot was recorded, slow this long before the death
        private const float SlowSpeed = 0.4f;
        private static float _slowStart;
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

        private static int _lastAdvanceFrame = -1;
        private static float _adsFov = 40f;
        private static bool _sniperSight;
        private static Camera _fovCam;
        private static float _savedFov = -1f;
        private static Texture2D _scopeTex, _dotTex;

        /// <summary>The killer was aiming down sights at the current replay moment.</summary>
        public static bool ReplayAds { get; private set; }
        public static bool ShowScope => IsReplay && ReplayAds && _sniperSight;
        public static bool ShowCrosshair => IsReplay && ReplayAds && !_sniperSight;

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
        public static bool SlowMotion => IsReplay && _replayT >= _slowStart && _replayT <= _killTime + ReplayTail;
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
            _havePrevEye = false; _sway = Quaternion.identity; _recoil = 0f;
            // Slow down just before the shot that did it (the last shot the killer fired before the death), not a fixed window.
            float lastShot = Recorder.LastShotBefore(killerId, killTime);
            _slowStart = lastShot > 0f && killTime - lastShot < 3f ? lastShot - SlowLeadBeforeShot : killTime - SlowFallback;
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
                try { _adsFov = w.Attachments.AdsFov; _sniperSight = w.Attachments.UseSniperUi; } catch (System.Exception) { _adsFov = 40f; _sniperSight = false; }
                var fp = w.Attachments.FirePoint;
                _flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _flash.name = "HTF1v1_MuzzleFlash";
                Object.Destroy(_flash.GetComponent<Collider>());
                _flash.transform.localScale = Vector3.one * 0.14f;
                var mat = new Material(Arena.ArenaMaterials.For(Core.BoxKind.Yellow));
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.35f) * 4f);
                _flash.GetComponent<MeshRenderer>().sharedMaterial = mat;
                _flash.transform.SetPositionAndRotation(fp ? fp.position : head.position + head.forward * 0.8f, head.rotation);
                _flash.transform.SetParent(_viewGun.transform, true);
                _flash.SetActive(false);
            }
        }

        private static Vector3 _prevEyePos;
        private static Quaternion _prevEyeRot = Quaternion.identity;
        private static bool _havePrevEye;
        private static float _bobPhase;
        private static Vector3 _swayVel;
        private static Quaternion _sway = Quaternion.identity;
        private static float _recoil;

        /// <summary>
        /// Places the gun copy on the replayed eye pose with first-person motion: walk bob from the killer's speed, sway that
        /// lags behind turns, and a kick on each shot. Without this the gun looks glued to the screen.
        /// </summary>
        private static void PlaceViewGun(Vector3 eyePos, Quaternion eyeRot)
        {
            if (!_viewGun) return;
            float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            float speed = 0f;
            Vector3 angVel = Vector3.zero;
            if (_havePrevEye)
            {
                speed = Vector3.ProjectOnPlane(eyePos - _prevEyePos, Vector3.up).magnitude / dt;
                var delta = Quaternion.Inverse(_prevEyeRot) * eyeRot;
                delta.ToAngleAxis(out float ang, out Vector3 axis);
                if (ang > 180f) ang -= 360f;
                angVel = axis * (ang / dt);
            }
            _prevEyePos = eyePos; _prevEyeRot = eyeRot; _havePrevEye = true;

            // Walk bob: faster steps when moving faster, none when still.
            float walk = Mathf.Clamp01(speed / 4f);
            _bobPhase += dt * Mathf.Lerp(0f, 11f, walk);
            Vector3 bob = new Vector3(Mathf.Sin(_bobPhase) * 0.012f, Mathf.Abs(Mathf.Cos(_bobPhase)) * 0.010f - 0.005f, 0f) * walk;

            // Sway: the gun lags a little behind the turn, then settles.
            Quaternion swayTarget = Quaternion.Euler(Mathf.Clamp(-angVel.x * 0.02f, -6f, 6f), Mathf.Clamp(-angVel.y * 0.02f, -8f, 8f), Mathf.Clamp(-angVel.y * 0.01f, -4f, 4f));
            _sway = Quaternion.Slerp(_sway, swayTarget, 1f - Mathf.Exp(-dt * 8f));

            // Recoil: set by ReplayShots, decays.
            _recoil = Mathf.Lerp(_recoil, 0f, 1f - Mathf.Exp(-dt * 12f));
            Vector3 kick = new Vector3(0f, _recoil * 0.02f, -_recoil * 0.06f);
            Quaternion kickRot = Quaternion.Euler(-_recoil * 4f, 0f, 0f);

            _viewGun.transform.SetPositionAndRotation(eyePos + eyeRot * (bob + kick), eyeRot * _sway * kickRot);
        }

        /// <summary>Replays any shot the killer fired since the last frame of replay time: flash plus the gun's own sound.</summary>
        private static void ReplayShots()
        {
            if (Recorder.FiredBetween(_killerId, _lastShotCheck, _replayT))
            {
                _flashUntil = Time.unscaledTime + 0.07f;
                _recoil = 1f;
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
            RestoreFov();
            ReplayAds = false;
        }

        private static void RestoreFov()
        {
            if (_fovCam && _savedFov > 0f) _fovCam.fieldOfView = _savedFov;
            _fovCam = null; _savedFov = -1f;
        }

        /// <summary>Zoom the replay camera while the killer was aiming; the gun copy hides so the sight reads as scoped.</summary>
        private static void ApplyAim(Camera cam)
        {
            ReplayAds = Recorder.AdsAt(_killerId, _replayT);
            if (!cam) return;
            if (_fovCam != cam) { RestoreFov(); _fovCam = cam; _savedFov = cam.fieldOfView; }
            float target = ReplayAds ? Mathf.Clamp(_adsFov, 5f, 120f) : _savedFov;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 12f));
            if (_viewGun) _viewGun.SetActive(!ReplayAds);
        }

        /// <summary>IMGUI overlay: sniper scope or a simple crosshair while the replayed killer was aiming.</summary>
        public static void DrawOverlay()
        {
            if (!ShowScope && !ShowCrosshair) return;
            float w = Screen.width, h = Screen.height;
            if (ShowScope)
            {
                if (!_scopeTex) _scopeTex = MakeScopeTexture(512);
                float size = Mathf.Min(w, h);
                float x = (w - size) / 2f, y = (h - size) / 2f;
                UnityEngine.GUI.color = Color.black;
                if (x > 0) { UnityEngine.GUI.DrawTexture(new Rect(0, 0, x, h), Texture2D.whiteTexture); UnityEngine.GUI.DrawTexture(new Rect(x + size, 0, w - x - size, h), Texture2D.whiteTexture); }
                if (y > 0) { UnityEngine.GUI.DrawTexture(new Rect(0, 0, w, y), Texture2D.whiteTexture); UnityEngine.GUI.DrawTexture(new Rect(0, y + size, w, h - y - size), Texture2D.whiteTexture); }
                UnityEngine.GUI.color = Color.white;
                UnityEngine.GUI.DrawTexture(new Rect(x, y, size, size), _scopeTex);
            }
            else
            {
                UnityEngine.GUI.color = new Color(1f, 1f, 1f, 0.9f);
                float cx = w / 2f, cy = h / 2f, len = 14f, gap = 6f, th = 2f;
                UnityEngine.GUI.DrawTexture(new Rect(cx - gap - len, cy - th / 2, len, th), Texture2D.whiteTexture);
                UnityEngine.GUI.DrawTexture(new Rect(cx + gap, cy - th / 2, len, th), Texture2D.whiteTexture);
                UnityEngine.GUI.DrawTexture(new Rect(cx - th / 2, cy - gap - len, th, len), Texture2D.whiteTexture);
                UnityEngine.GUI.DrawTexture(new Rect(cx - th / 2, cy + gap, th, len), Texture2D.whiteTexture);
                UnityEngine.GUI.color = Color.white;
            }
        }

        private static Texture2D MakeScopeTexture(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color[n * n];
            float c = (n - 1) / 2f, r = n * 0.46f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    Color col;
                    if (d > r) col = Color.black;
                    else if (d > r - 6) col = new Color(0, 0, 0, 0.85f);
                    else
                    {
                        bool line = Mathf.Abs(x - c) < 1.2f || Mathf.Abs(y - c) < 1.2f;
                        bool tick = (Mathf.Abs(x - c) < 1.2f || Mathf.Abs(y - c) < 1.2f) && ((int)(d / 24f)) % 2 == 0 && d > 10 && (Mathf.Abs(x - c) < 6f || Mathf.Abs(y - c) < 6f);
                        col = line ? new Color(0, 0, 0, 0.95f) : (tick ? new Color(0, 0, 0, 0.6f) : new Color(0, 0, 0, Mathf.Clamp01((d / r - 0.75f) * 1.2f) * 0.5f));
                    }
                    px[y * n + x] = col;
                }
            t.SetPixels(px); t.Apply(); t.wrapMode = TextureWrapMode.Clamp;
            return t;
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
                // Same proven material path as the arena (URP Lit), bright gold with emission so it reads at any distance.
                var mat = new Material(Arena.ArenaMaterials.For(Core.BoxKind.Yellow));
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", new Color(1f, 0.75f, 0.2f) * 1.5f);
                _ghost.GetComponent<MeshRenderer>().sharedMaterial = mat;
                // A slim beam above the ghost so the victim is findable even behind cover.
                var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(beam.GetComponent<Collider>());
                beam.transform.SetParent(_ghost.transform, false);
                beam.transform.localPosition = new Vector3(0f, 1.6f, 0f);
                beam.transform.localScale = new Vector3(0.08f, 0.9f, 0.08f);
                beam.GetComponent<MeshRenderer>().sharedMaterial = mat;
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
                // Advance the replay clock once per frame even if more than one camera hook asks for the pose.
                if (_lastAdvanceFrame != Time.frameCount)
                {
                    _lastAdvanceFrame = Time.frameCount;
                    float speed = _replayT >= _slowStart ? SlowSpeed : 1f;
                    _replayT += Time.unscaledDeltaTime * speed;
                }
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
                    ApplyAim(cam);
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
            RestoreFov();
            ReplayAds = false;
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
