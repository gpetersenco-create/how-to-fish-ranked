using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using HowToFish1v1.Core;
using HowToFish1v1.Net;
using HowToFish1v1.Net.Proto2;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// CoD-style killcam. After the local player dies to someone, the camera replays the killer's recorded first-person view
    /// from a few seconds before the kill, slowing down just before the killing shot, with a ghost of the victim showing
    /// where they were. The replay is sized to fit the time until the respawn / next round; if time remains it follows the
    /// killer live, otherwise it holds the last frame. At match end everyone watches the final kill the same way.
    /// Drives whichever camera the game currently uses (death cam while dead, player cam while alive).
    /// </summary>
    public static class KillCam
    {
        public const float MaxLead = 5f;                 // seconds before the kill to start from, when time allows
        private const float MinLead = 1.5f;
        private const float SlowLeadBeforeShot = 0.5f;   // slow motion starts this long before the killing shot
        private const float SlowFallback = 0.6f;         // if no shot was recorded, slow this long before the death
        private const float SlowSpeed = 0.4f;
        private const float ReplayTail = 0.3f;           // seconds after the kill to keep showing
        private const float BudgetMargin = 0.4f;         // finish the replay this long before the phase can end
        private const float DefaultBudget = 7f;          // when the host did not send its timing rules
        private const float LiveFollowMax = 8f;
        private const float EyeForward = 0.12f;          // keep the killer's own head mesh behind the near plane
        private const float FadeSpeed = 4f;

        private enum Mode { Off, Replay, Hold, LiveFollow }

        private static Mode _mode = Mode.Off;
        private static bool _final, _preview;
        private static int _killerId = -1, _victimId = -1;
        private static float _killTime, _replayT, _slowStart, _startedAt, _budget;
        private static Vector3 _pos;
        private static Quaternion _rot;
        private static bool _snapped;
        private static int _lastAdvanceFrame = -1;
        private static float _fade;

        // Weapon of the kill (reset for every replay so a missing gun never reuses the previous kill's values).
        private static float _adsFov = 40f, _adsDamping = 0.1f, _sniperPct = 0.9f;
        private static Vector3 _adsPos;
        private static bool _sniperSight;
        private static string _fireSound = "";
        private static int _fireSoundCount = 1;
        private static float _fireVolume = 1f;
        private static float _aimPercent, _aimVel;

        // Camera state we override and restore.
        private static Camera _fovCam;
        private static float _baseFov = -1f, _curFov = -1f;
        private static Camera _clipCam;
        private static float _savedNearClip = -1f;
        private static Texture2D _scopeTex;

        // Scene objects and everything hidden or created for the replay.
        private static GameObject _ghost, _viewGun, _flash;
        private static readonly List<Renderer> _gunRenderers = new List<Renderer>();
        private static readonly List<Object> _created = new List<Object>();
        private static readonly List<Renderer> _hiddenRenderers = new List<Renderer>();
        private static readonly List<Canvas> _hiddenCanvases = new List<Canvas>();
        private static Item _hiddenItem;
        private static DeadPlayer _hiddenCorpse;
        private static bool _hudHidden, _handsHidden;
        private static float _flashUntil, _lastShotCheck;
        private static Vector3 _previewGhostPos;
        private static Quaternion _previewGhostRot;

        // The most recent kill seen this match, for the final killcam.
        private static int _lastKillerId = -1, _lastVictimId = -1;
        private static float _lastKillTime;
        private static string _lastKillerName = "", _lastVictimName = "";

        public static bool Active => _mode != Mode.Off;
        public static bool IsFinal => Active && _final;
        public static bool IsPreview => Active && _preview;
        public static bool IsReplay => _mode == Mode.Replay || _mode == Mode.Hold;
        public static bool SlowMotion => _mode == Mode.Replay && _replayT >= _slowStart && _replayT <= _killTime + ReplayTail;
        /// <summary>The final killcam and the self-test preview drive the normal player camera while the viewer is alive.</summary>
        public static bool UsesPlayerCam => Active && (_final || _preview) && Player.LocalPlayer && !Player.LocalPlayer.Dying.IsDead;
        /// <summary>Seconds until the respawn / phase change this killcam was fitted into.</summary>
        public static float SecondsLeft => Active ? Mathf.Max(0f, _budget - (Time.unscaledTime - _startedAt)) : 0f;
        /// <summary>The killer was aiming down sights at the current replay moment.</summary>
        public static bool ReplayAds { get; private set; }
        public static bool ShowScope => IsReplay && _sniperSight && _aimPercent > 0.6f;
        public static bool ShowCrosshair => IsReplay && ReplayAds && !_sniperSight;
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
            if (k.VictimId != ModState.LocalOwnerId) return;
            float budget = ClientMatchView.HasState ? (ClientMatchView.IsFfa ? ClientMatchView.Latest.RespawnSeconds : ClientMatchView.Latest.RoundEndSeconds) : 0f;
            Begin(k.KillerId, k.VictimId, k.Killer, k.Victim, Time.unscaledTime, final: false, preview: false, budget: budget > 0f ? budget : DefaultBudget);
        }

        /// <summary>Called when the match ends: replay the winning kill for everyone.</summary>
        public static void StartFinal()
        {
            if (_lastKillerId == -1 || Time.unscaledTime - _lastKillTime > Recorder.KeepSeconds - 3f) return;
            float budget = ClientMatchView.HasState && ClientMatchView.Latest.MatchEndSeconds > 0f ? ClientMatchView.Latest.MatchEndSeconds : 5f;
            // The victim is usually already watching this very kill; keep that replay and just make it the final one.
            if (Active && !_preview && _mode != Mode.LiveFollow && _killerId == _lastKillerId && _victimId == _lastVictimId && Mathf.Abs(_killTime - _lastKillTime) < 0.01f)
            {
                _final = true;
                _budget = Mathf.Max(_budget, budget - (Time.unscaledTime - _startedAt));
                return;
            }
            Begin(_lastKillerId, _lastVictimId, _lastKillerName, _lastVictimName, _lastKillTime, final: true, preview: false, budget: budget);
        }

        /// <summary>Self test without other players: replays your own last seconds as if you had just made a kill. Press again to stop.</summary>
        public static void StartPreview()
        {
            var me = Player.LocalPlayer;
            if (Active) { Stop(); return; }
            if (!me || me.Dying.IsDead) return;
            var head = me.CamObject ? me.CamObject : me.Transform;
            Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
            fwd.Normalize();
            _previewGhostPos = head.position + fwd * 6f;
            _previewGhostRot = Quaternion.LookRotation(-fwd, Vector3.up);
            Begin(me.OwnerId, -1, me.SteamName, "Practice target", Time.unscaledTime, final: false, preview: true, budget: 8f);
        }

        /// <summary>Real seconds a replay takes when it starts this many seconds before the kill.</summary>
        private static float RealLength(float lead)
        {
            float start = _killTime - lead;
            float s = Mathf.Max(_slowStart, start);
            return (s - start) + (_killTime + ReplayTail - s) / SlowSpeed;
        }

        private static void Begin(int killerId, int victimId, string killerName, string victimName, float killTime, bool final, bool preview, float budget)
        {
            Cleanup();
            ResetWeaponState();
            _killerId = killerId;
            _victimId = victimId;
            _killTime = killTime;
            _final = final;
            _preview = preview;
            _budget = budget;
            _startedAt = Time.unscaledTime;
            _havePrevEye = false; _sway = Quaternion.identity; _recoil = 0f; _bobPhase = 0f;

            // Slow down just before the shot that did it (the last shot the killer fired before the death), not a fixed window.
            float lastShot = Recorder.LastShotBefore(killerId, killTime);
            _slowStart = lastShot > 0f && killTime - lastShot < 3f ? lastShot - SlowLeadBeforeShot : killTime - SlowFallback;

            // Start as early as the recording and the time budget allow.
            float history = Recorder.KeepSeconds - (Time.unscaledTime - killTime) - 0.5f;
            if (history < MinLead) { _mode = Mode.Off; return; }
            float lead = Mathf.Min(MaxLead, history);
            while (lead > MinLead && RealLength(lead) > budget - BudgetMargin) lead -= 0.25f;
            _replayT = killTime - lead;
            _lastShotCheck = _replayT;
            _snapped = false;
            _fade = 1f;
            _mode = Mode.Replay;

            KillerName = killerName ?? "";
            VictimName = victimName ?? "";
            var entry = ClientMatchView.Players.FirstOrDefault(p => p.Id == killerId);
            string rank = entry.Name != null ? RankService.Ladder.TierName(entry.RankPoints) : "";
            string gun = entry.Name != null ? LoadoutService.Summary(entry.Loadout) : "";
            KillerInfo = string.IsNullOrEmpty(rank) ? gun : $"{rank.ToUpperInvariant()}   |   {gun}";

            BuildViewGun();
            HideKiller();
            HideLocalViewmodel();
            try { PlayerUI.ToggleMainCanvas(false); _hudHidden = true; } catch (System.Exception) { }
        }

        private static void ResetWeaponState()
        {
            _fireSound = ""; _fireSoundCount = 1; _fireVolume = 1f;
            _adsFov = 40f; _adsDamping = 0.1f; _sniperPct = 0.9f; _adsPos = Vector3.zero; _sniperSight = false;
            _aimPercent = 0f; _aimVel = 0f; _flashUntil = 0f;
            ReplayAds = false;
        }

        private static Player FindPlayer(int ownerId) => PlayerManager.Players.FirstOrDefault(p => p && p.OwnerId == ownerId);

        /// <summary>
        /// A render-only copy of the killer's held gun. Remote players' guns are pushed forward by the game's third-person
        /// offset; that is undone so the copy sits where the killer saw it. During the replay the copy rides along with the
        /// replayed eye pose, reading as the first-person gun.
        /// </summary>
        private static void BuildViewGun()
        {
            var killer = FindPlayer(_killerId);
            if (!killer || !killer.Transform) return;
            var item = killer.Holding ? killer.Holding.HeldItem : null;
            var head = killer.CamObject ? killer.CamObject : killer.Transform;
            if (!item || !head) return;
            Vector3 fix = Vector3.zero;
            try { if (item is Tool tool && killer.Owner != null && !killer.Owner.IsLocalClient) fix = -head.forward * tool.ThirdPersonOffset; }
            catch (System.Exception) { }

            _viewGun = new GameObject("HTF1v1_KillcamGun");
            _viewGun.transform.SetPositionAndRotation(head.position, head.rotation);
            _gunRenderers.Clear();
            foreach (var r in item.GetComponentsInChildren<Renderer>(true))
            {
                if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var copy = new GameObject(r.name);
                copy.transform.SetPositionAndRotation(r.transform.position + fix, r.transform.rotation);
                copy.transform.localScale = r.transform.lossyScale;
                Renderer made = null;
                if (r is SkinnedMeshRenderer smr)
                {
                    var mesh = new Mesh();
                    smr.BakeMesh(mesh);
                    _created.Add(mesh);
                    copy.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = copy.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = smr.sharedMaterials;
                    copy.transform.localScale = Vector3.one;
                    made = mr;
                }
                else if (r is MeshRenderer src)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (!mf || !mf.sharedMesh) { Object.Destroy(copy); continue; }
                    copy.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    var mr = copy.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = src.sharedMaterials;
                    made = mr;
                }
                else { Object.Destroy(copy); continue; }
                made.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                copy.transform.SetParent(_viewGun.transform, true);
                _gunRenderers.Add(made);
            }
            if (item is Weapon w && w.Attachments)
            {
                try
                {
                    _fireSound = w.Attachments.FireSound ?? "";
                    _fireSoundCount = Mathf.Max(1, w.Attachments.FireSoundCount);
                    _fireVolume = w.Attachments.FireSoundVolume;
                }
                catch (System.Exception) { _fireSound = ""; }
                try
                {
                    _adsFov = w.Attachments.AdsFov;
                    _adsPos = w.Attachments.AdsPos;
                    _adsDamping = w.Attachments.AdsSpeedDamping;
                    _sniperSight = w.Attachments.UseSniperUi;
                    _sniperPct = w.Attachments.SniperUiAimPercent;
                }
                catch (System.Exception) { _adsFov = 40f; _sniperSight = false; }
                Transform fp = null;
                try { fp = w.Attachments.FirePoint; } catch (System.Exception) { }
                _flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _flash.name = "HTF1v1_MuzzleFlash";
                Object.Destroy(_flash.GetComponent<Collider>());
                _flash.transform.localScale = Vector3.one * 0.14f;
                var mat = new Material(Arena.ArenaMaterials.For(BoxKind.Yellow));
                _created.Add(mat);
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", new Color(1f, 0.85f, 0.35f) * 4f);
                _flash.GetComponent<MeshRenderer>().sharedMaterial = mat;
                _flash.transform.SetPositionAndRotation((fp ? fp.position : head.position + head.forward * 0.8f) + fix, head.rotation);
                _flash.transform.SetParent(_viewGun.transform, true);
                _flash.SetActive(false);
            }
        }

        private static Vector3 _prevEyePos;
        private static Quaternion _prevEyeRot = Quaternion.identity;
        private static bool _havePrevEye;
        private static float _bobPhase;
        private static Quaternion _sway = Quaternion.identity;
        private static float _recoil;

        /// <summary>
        /// Places the gun copy on the replayed eye pose with first-person motion: walk bob from the killer's speed, sway that
        /// lags behind turns, a kick on each shot, and the slide towards the sight while aiming. Sniper sights hide the gun
        /// once the scope overlay takes over, like the game does.
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

            float walk = Mathf.Clamp01(speed / 4f) * (1f - _aimPercent);
            _bobPhase += dt * Mathf.Lerp(0f, 11f, walk);
            Vector3 bob = new Vector3(Mathf.Sin(_bobPhase) * 0.012f, Mathf.Abs(Mathf.Cos(_bobPhase)) * 0.010f - 0.005f, 0f) * walk;

            float swayScale = 1f - 0.7f * _aimPercent;
            Quaternion swayTarget = Quaternion.Euler(Mathf.Clamp(-angVel.x * 0.02f, -6f, 6f) * swayScale, Mathf.Clamp(-angVel.y * 0.02f, -8f, 8f) * swayScale, Mathf.Clamp(-angVel.y * 0.01f, -4f, 4f) * swayScale);
            _sway = Quaternion.Slerp(_sway, swayTarget, 1f - Mathf.Exp(-dt * 8f));

            _recoil = Mathf.Lerp(_recoil, 0f, 1f - Mathf.Exp(-dt * 12f));
            Vector3 kick = new Vector3(0f, _recoil * 0.02f, -_recoil * 0.06f);
            Quaternion kickRot = Quaternion.Euler(-_recoil * 4f, 0f, 0f);

            Vector3 ads = _adsPos * _aimPercent;
            _viewGun.transform.SetPositionAndRotation(eyePos + eyeRot * (bob + kick + ads), eyeRot * _sway * kickRot);

            bool hideGun = _sniperSight && _aimPercent > _sniperPct;
            foreach (var r in _gunRenderers) if (r && r.enabled == hideGun) r.enabled = !hideGun;
            if (_flash && _flash.activeSelf && Time.unscaledTime > _flashUntil) _flash.SetActive(false);
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
                    // The game names its clips "<sound><n>"; this is the same call it uses, without the 3D position.
                    try { AudioManager.PlayRandomGlobalClip(_fireSound, 1, _fireSoundCount, false, _fireVolume, 0.02f); } catch (System.Exception) { }
                }
            }
            _lastShotCheck = _replayT;
        }

        public static void Stop()
        {
            _mode = Mode.Off;
            _final = false;
            _preview = false;
            _killerId = -1;
            _victimId = -1;
            Cleanup();
        }

        public static void OnMatchLeftEndPhase() { if (_final) Stop(); }

        /// <summary>Every frame from the plugin: stops a killcam that no camera hook is driving any more and runs the fade.</summary>
        public static void Update()
        {
            _fade = Mathf.MoveTowards(_fade, 0f, Time.unscaledDeltaTime * FadeSpeed);
            if (!Active) return;
            var me = Player.LocalPlayer;
            if (!ModState.IsActive || !me) { Stop(); return; }
            if (_final && ModState.Phase != MatchPhase.MatchEnd) { Stop(); return; }
            if (!_final && !_preview && !me.Dying.IsDead) { Stop(); return; }
            if (Time.unscaledTime - _startedAt > _budget + LiveFollowMax + 3f) { Stop(); return; }
        }

        private static void Cleanup()
        {
            foreach (var r in _hiddenRenderers) if (r) r.enabled = true;
            _hiddenRenderers.Clear();
            foreach (var c in _hiddenCanvases) if (c) c.enabled = true;
            _hiddenCanvases.Clear();
            _hiddenItem = null; _hiddenCorpse = null;
            if (_ghost) Object.Destroy(_ghost);
            _ghost = null;
            if (_viewGun) Object.Destroy(_viewGun);
            _viewGun = null; _flash = null;
            _gunRenderers.Clear();
            foreach (var o in _created) if (o) Object.Destroy(o);
            _created.Clear();
            if (_clipCam && _savedNearClip > 0f) _clipCam.nearClipPlane = _savedNearClip;
            _clipCam = null; _savedNearClip = -1f;
            RestoreFov();
            ReplayAds = false;
            _aimPercent = 0f; _aimVel = 0f;
            var me = Player.LocalPlayer;
            if (_handsHidden)
            {
                _handsHidden = false;
                try { if (me && me.Hands && !me.Dying.IsDead) me.Hands.ToggleHandMeshes(true); } catch (System.Exception) { }
            }
            if (_hudHidden)
            {
                _hudHidden = false;
                try { PlayerUI.ToggleMainCanvas(true); } catch (System.Exception) { }
            }
        }

        private static void RestoreFov()
        {
            if (_fovCam && _baseFov > 0f) _fovCam.fieldOfView = _baseFov;
            _fovCam = null; _baseFov = -1f; _curFov = -1f;
        }

        /// <summary>
        /// Zooms the replay camera the way the killer's own camera zoomed, following the game's aim curve. The value is
        /// assigned every frame because the game rewrites the field of view every frame too.
        /// </summary>
        private static void ApplyAim(Camera cam, float baseFov)
        {
            ReplayAds = Recorder.AdsAt(_killerId, _replayT);
            float dt = _mode == Mode.Hold ? 0f : Time.unscaledDeltaTime * (SlowMotion ? SlowSpeed : 1f);
            if (dt > 0f) _aimPercent = Mathf.SmoothDamp(_aimPercent, ReplayAds ? 1f : 0f, ref _aimVel, Mathf.Max(0.02f, _adsDamping), float.MaxValue, dt);
            if (!cam) return;
            if (_fovCam != cam)
            {
                RestoreFov();
                _fovCam = cam;
                _baseFov = baseFov > 0f ? baseFov : cam.fieldOfView;
            }
            _curFov = Mathf.Lerp(_baseFov, Mathf.Clamp(_adsFov, 5f, 120f), _aimPercent);
            cam.fieldOfView = _curFov;
        }

        /// <summary>IMGUI overlay: fade from black, plus the sniper scope or a crosshair while the replayed killer was aiming.</summary>
        public static void DrawOverlay()
        {
            float w = Screen.width, h = Screen.height;
            if (ShowScope)
            {
                if (!_scopeTex) _scopeTex = MakeScopeTexture(512);
                float a = Mathf.InverseLerp(0.6f, 0.95f, _aimPercent);
                float size = Mathf.Min(w, h);
                float x = (w - size) / 2f, y = (h - size) / 2f;
                UnityEngine.GUI.color = new Color(0f, 0f, 0f, a);
                if (x > 0) { UnityEngine.GUI.DrawTexture(new Rect(0, 0, x, h), Texture2D.whiteTexture); UnityEngine.GUI.DrawTexture(new Rect(x + size, 0, w - x - size, h), Texture2D.whiteTexture); }
                if (y > 0) { UnityEngine.GUI.DrawTexture(new Rect(0, 0, w, y), Texture2D.whiteTexture); UnityEngine.GUI.DrawTexture(new Rect(0, y + size, w, h - y - size), Texture2D.whiteTexture); }
                UnityEngine.GUI.color = new Color(1f, 1f, 1f, a);
                UnityEngine.GUI.DrawTexture(new Rect(x, y, size, size), _scopeTex);
                UnityEngine.GUI.color = Color.white;
            }
            else if (ShowCrosshair)
            {
                UnityEngine.GUI.color = new Color(1f, 1f, 1f, 0.9f * _aimPercent);
                float cx = w / 2f, cy = h / 2f, len = 14f, gap = 6f, th = 2f;
                UnityEngine.GUI.DrawTexture(new Rect(cx - gap - len, cy - th / 2, len, th), Texture2D.whiteTexture);
                UnityEngine.GUI.DrawTexture(new Rect(cx + gap, cy - th / 2, len, th), Texture2D.whiteTexture);
                UnityEngine.GUI.DrawTexture(new Rect(cx - th / 2, cy - gap - len, th, len), Texture2D.whiteTexture);
                UnityEngine.GUI.DrawTexture(new Rect(cx - th / 2, cy + gap, th, len), Texture2D.whiteTexture);
                UnityEngine.GUI.color = Color.white;
            }
            if (_fade > 0f)
            {
                UnityEngine.GUI.color = new Color(0f, 0f, 0f, _fade);
                UnityEngine.GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
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

        private static void HideRenderers(Component root)
        {
            if (!root) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r && r.enabled) { r.enabled = false; _hiddenRenderers.Add(r); }
        }

        /// <summary>We look through the killer's eyes, so their body, their live gun and their name tag must not be drawn.</summary>
        private static void HideKiller()
        {
            var killer = FindPlayer(_killerId);
            if (!killer || !killer.Transform) return;
            HideRenderers(killer.Transform);
            foreach (var c in killer.Transform.GetComponentsInChildren<Canvas>(true))
                if (c && c.enabled) { c.enabled = false; _hiddenCanvases.Add(c); }
            _hiddenItem = killer.Holding ? killer.Holding.HeldItem : null;
            HideRenderers(_hiddenItem);
        }

        /// <summary>The killer may swap weapons mid-replay (fresh renderers) and the victim's ragdoll may land after we start.</summary>
        private static void RehideDuringReplay(Player me)
        {
            var killer = FindPlayer(_killerId);
            if (killer && killer.Holding)
            {
                var item = killer.Holding.HeldItem;
                if (item && item != _hiddenItem) { _hiddenItem = item; HideRenderers(item); }
            }
            if (me && _victimId == me.OwnerId && me.Dying && me.Dying.DeadPlayer && me.Dying.DeadPlayer != _hiddenCorpse)
            {
                _hiddenCorpse = me.Dying.DeadPlayer;
                HideRenderers(_hiddenCorpse);
            }
        }

        /// <summary>Watching while alive (final killcam, preview): our own hands and gun would otherwise follow the replay camera.</summary>
        private static void HideLocalViewmodel()
        {
            var me = Player.LocalPlayer;
            if (!me || me.Dying.IsDead || me.OwnerId == _killerId) return;   // as the killer we are already hidden
            HideRenderers(me.Holding ? me.Holding.HeldItem : null);
            try { if (me.Hands) { me.Hands.ToggleHandMeshes(false); _handsHidden = true; } } catch (System.Exception) { }
        }

        private static void ShowKiller()
        {
            foreach (var r in _hiddenRenderers) if (r) r.enabled = true;
            _hiddenRenderers.Clear();
            foreach (var c in _hiddenCanvases) if (c) c.enabled = true;
            _hiddenCanvases.Clear();
            _hiddenItem = null; _hiddenCorpse = null;
        }

        /// <summary>A glowing stand-in for the victim at their recorded position, sized from their eye height and snapped to the floor.</summary>
        private static void UpdateGhost(bool show, float t)
        {
            Vector3 headPos = Vector3.zero; Quaternion headRot = Quaternion.identity;
            bool have = show && (_victimId >= 0 ? Recorder.TryGet(_victimId, t, out headPos, out headRot) : PreviewGhost(out headPos, out headRot));
            if (!have)
            {
                if (_ghost) _ghost.SetActive(false);
                return;
            }
            if (!_ghost)
            {
                _ghost = new GameObject("HTF1v1_KillcamGhost");
                // Same proven material path as the arena (URP Lit), bright gold with emission so it reads at any distance.
                var mat = new Material(Arena.ArenaMaterials.For(BoxKind.Yellow));
                _created.Add(mat);
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", new Color(1f, 0.75f, 0.2f) * 1.5f);
                GhostPart(PrimitiveType.Capsule, "Body", mat);
                GhostPart(PrimitiveType.Cylinder, "Beam", mat);
                GhostPart(PrimitiveType.Cube, "Face", mat);
            }
            _ghost.SetActive(true);
            float floorY = Physics.Raycast(headPos + Vector3.up * 0.1f, Vector3.down, out var hit, 3f, GameInfo.LevelLayer) ? hit.point.y : headPos.y - 1.65f;
            float height = Mathf.Clamp(headPos.y + 0.1f - floorY, 0.8f, 2.2f);
            Vector3 fwd = Vector3.ProjectOnPlane(headRot * Vector3.forward, Vector3.up);
            _ghost.transform.SetPositionAndRotation(new Vector3(headPos.x, floorY, headPos.z), fwd.sqrMagnitude > 0.001f ? Quaternion.LookRotation(fwd.normalized, Vector3.up) : Quaternion.identity);
            var body = _ghost.transform.GetChild(0); body.localPosition = new Vector3(0f, height / 2f, 0f); body.localScale = new Vector3(0.7f, height / 2f, 0.7f);
            var beam = _ghost.transform.GetChild(1); beam.localPosition = new Vector3(0f, height + 0.6f, 0f); beam.localScale = new Vector3(0.08f, 0.5f, 0.08f);
            var face = _ghost.transform.GetChild(2); face.localPosition = new Vector3(0f, height - 0.15f, 0.35f); face.localScale = new Vector3(0.25f, 0.12f, 0.35f);
        }

        private static void GhostPart(PrimitiveType type, string name, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.SetParent(_ghost.transform, false);
        }

        private static bool PreviewGhost(out Vector3 pos, out Quaternion rot)
        {
            pos = _previewGhostPos; rot = _previewGhostRot;
            return true;
        }

        /// <summary>Camera pose for this frame, or false when the killcam is not running.</summary>
        private static bool Pose(Camera cam, float baseFov, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            if (!Active) return false;
            var me = Player.LocalPlayer;
            if (!me) { Stop(); return false; }
            if (!_final && !_preview && !me.Dying.IsDead) { Stop(); return false; }   // respawned: give the camera back

            if (_mode == Mode.Replay || _mode == Mode.Hold)
            {
                // Advance the replay clock once per frame even if more than one camera hook asks for the pose.
                if (_mode == Mode.Replay && _lastAdvanceFrame != Time.frameCount)
                {
                    _lastAdvanceFrame = Time.frameCount;
                    float speed = _replayT >= _slowStart ? SlowSpeed : 1f;
                    _replayT += Time.unscaledDeltaTime * speed;
                }
                if (_mode == Mode.Replay && _replayT > _killTime + ReplayTail)
                {
                    if (_preview) { Stop(); return false; }
                    float remaining = _budget - (Time.unscaledTime - _startedAt);
                    if (_final || remaining < 1.5f)
                    {
                        _mode = Mode.Hold;   // not enough time for a live follow: freeze on the kill until the phase moves on
                    }
                    else
                    {
                        UpdateGhost(false, 0f);
                        if (_viewGun) _viewGun.SetActive(false);
                        _mode = Mode.LiveFollow;
                        _startedAt = Time.unscaledTime;
                        _budget = remaining;
                        _snapped = false;
                        _fade = 1f;
                        ShowKiller();
                    }
                }
                if (_mode == Mode.Replay || _mode == Mode.Hold)
                {
                    float t = Mathf.Min(_replayT, _killTime + ReplayTail);
                    if (Recorder.TryGet(_killerId, t, out var rp, out var rr))
                    {
                        if (cam && _clipCam != cam)
                        {
                            if (_clipCam && _savedNearClip > 0f) _clipCam.nearClipPlane = _savedNearClip;
                            _clipCam = cam; _savedNearClip = cam.nearClipPlane; cam.nearClipPlane = 0.04f;
                        }
                        RehideDuringReplay(me);
                        UpdateGhost(true, t);
                        ApplyAim(cam, baseFov);
                        PlaceViewGun(rp, rr);
                        if (_mode == Mode.Replay) ReplayShots();
                        pos = rp + rr * Vector3.forward * EyeForward;
                        rot = rr;
                        return true;
                    }
                    if (_final || _preview) { Stop(); return false; }
                    UpdateGhost(false, 0f);
                    _mode = Mode.LiveFollow;
                    _startedAt = Time.unscaledTime;
                    _fade = 1f;
                    ShowKiller();
                }
            }

            // Live follow: behind the killer's shoulder.
            if (_clipCam && _savedNearClip > 0f) { _clipCam.nearClipPlane = _savedNearClip; _clipCam = null; _savedNearClip = -1f; }
            RestoreFov();
            ReplayAds = false;
            if (Time.unscaledTime - _startedAt > LiveFollowMax) { Stop(); return false; }
            var killer = FindPlayer(_killerId);
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
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 10f);
            _pos = Vector3.Lerp(_pos, wanted, k);
            _rot = Quaternion.Slerp(_rot, wantedRot, k);
            pos = _pos; rot = _rot;
            return true;
        }

        /// <summary>From the death camera's LateUpdate (Harmony postfix) while the local player is dead.</summary>
        public static void ApplyDeathCam(PlayerDeathCam deathCam)
        {
            if (!Active || !deathCam || !deathCam.Owner.IsLocalClient) return;
            var me = Player.LocalPlayer;
            if (!me || !me.Dying.IsDead) return;   // the player camera owns the view while alive
            var cam = Traverse.Create(deathCam).Field<Camera>("_deathCam").Value;
            if (cam && Pose(cam, -1f, out var p, out var r)) cam.transform.SetPositionAndRotation(p, r);
        }

        /// <summary>From the player camera's Update (Harmony postfix) while the local player is alive (final killcam, preview).</summary>
        public static void ApplyPlayerCam(PlayerCamera playerCam)
        {
            if (!UsesPlayerCam || !playerCam) return;
            var cam = playerCam.Cam;
            float orig = -1f;
            try { orig = Traverse.Create(playerCam).Field<float>("_origFov").Value; } catch (System.Exception) { }
            if (cam && Pose(cam, orig, out var p, out var r)) cam.transform.SetPositionAndRotation(p, r);
        }
    }
}
