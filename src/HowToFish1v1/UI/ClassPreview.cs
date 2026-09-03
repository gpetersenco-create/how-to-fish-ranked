using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using HowToFish1v1.Core;
using HowToFish1v1.Match;
using UnityEngine;

namespace HowToFish1v1.UI
{
    /// <summary>
    /// The create-a-class preview: a render-only copy of the chosen gun prefab with the chosen sight, barrel, laser and
    /// skin, turning slowly in front of a private camera far above the world. The camera draws into a texture the lobby
    /// panel shows. Nothing here is networked: the copy is plain meshes, so it never touches FishNet.
    /// </summary>
    public static class ClassPreview
    {
        private static readonly Vector3 Origin = new Vector3(0f, 900f, 0f);
        private static Camera _cam;
        private static RenderTexture _rt;
        private static GameObject _model;
        private static Light _light;
        private static int _layer = -1;
        private static LoadoutGun _shown;
        private static bool _hasModel;
        private static float _spin;
        private static readonly List<Object> _created = new List<Object>();
        private static string _error = "";

        public static Texture Texture => _rt;
        public static string Error => _error;

        /// <summary>Show this gun (rebuilds only when something changed).</summary>
        public static void Show(LoadoutGun g)
        {
            if (_hasModel && Same(_shown, g) && _model) { Enable(true); return; }
            Build(g);
        }

        public static void Hide()
        {
            Enable(false);
        }

        private static bool Same(LoadoutGun a, LoadoutGun b) =>
            a.ItemId == b.ItemId && a.Sight == b.Sight && a.Barrel == b.Barrel && a.Laser == b.Laser && a.Skin == b.Skin && a.Drum == b.Drum && a.Switch == b.Switch;

        /// <summary>Call every frame: turns the model and switches the camera off when the lobby is closed.</summary>
        public static void Update()
        {
            if (!LobbyPanel.IsOpen) { Enable(false); return; }
            if (_model && _model.activeSelf)
            {
                _spin += Time.unscaledDeltaTime * 28f;
                _model.transform.rotation = Quaternion.Euler(0f, 90f + _spin, 0f) * Quaternion.Euler(8f, 0f, 0f);
            }
        }

        private static void Enable(bool on)
        {
            if (_cam && _cam.enabled != on) _cam.enabled = on;
            if (_model && _model.activeSelf != on) _model.SetActive(on);
            if (_light && _light.enabled != on) _light.enabled = on;
        }

        private static int PickLayer()
        {
            for (int i = 31; i >= 8; i--) if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            return 0;
        }

        private static void EnsureCamera()
        {
            if (_cam) return;
            _layer = PickLayer();
            _rt = new RenderTexture(720, 440, 24) { name = "HTF1v1_ClassPreview" };
            var go = new GameObject("HTF1v1_ClassPreviewCam");
            Object.DontDestroyOnLoad(go);
            _cam = go.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.07f, 0.10f, 0.15f, 1f);
            _cam.cullingMask = 1 << _layer;
            _cam.fieldOfView = 26f;
            _cam.nearClipPlane = 0.02f;
            _cam.farClipPlane = 30f;
            _cam.targetTexture = _rt;
            _cam.depth = -50;
            _cam.allowHDR = false;
            _cam.useOcclusionCulling = false;
            var lgo = new GameObject("HTF1v1_ClassPreviewLight");
            lgo.transform.SetParent(go.transform, false);
            lgo.transform.localPosition = new Vector3(-0.6f, 0.8f, -0.4f);
            _light = lgo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.range = 12f;
            _light.intensity = 2.2f;
            _light.color = new Color(1f, 0.97f, 0.9f);
        }

        private static void Clear()
        {
            if (_model) Object.Destroy(_model);
            _model = null;
            foreach (var o in _created) if (o) Object.Destroy(o);
            _created.Clear();
            _hasModel = false;
        }

        private static void Build(LoadoutGun g)
        {
            EnsureCamera();
            Clear();
            _shown = g;
            _error = "";
            var prefab = GameInfo.IDToItem(g.ItemId);
            if (!prefab) { _error = "No such gun"; return; }
            var att = prefab.GetComponentInChildren<Attachments>(true);
            List<Sight> sights = null; List<BarrelAttachment> barrels = null; LaserSight laser = null; Transform firePoint = null;
            if (att)
            {
                var t = Traverse.Create(att);
                try { sights = t.Field<List<Sight>>("_sights").Value; } catch (System.Exception) { }
                try { barrels = t.Field<List<BarrelAttachment>>("_barrelAttachments").Value; } catch (System.Exception) { }
                try { laser = t.Field<LaserSight>("_laserSight").Value; } catch (System.Exception) { }
                try { var bl = t.Field<List<BarrelAttachment>>("_barrelAttachments").Value; int bi = Mathf.Clamp(g.Barrel, 0, (bl?.Count ?? 1) - 1); firePoint = bl != null && bl.Count > 0 ? bl[bi].FirePoint : null; } catch (System.Exception) { }
            }
            Renderer hands = null;
            try { hands = prefab is Tool tool ? tool.HandsMesh : null; } catch (System.Exception) { }
            var root = prefab.transform;
            int sightIdx = sights != null ? Mathf.Clamp(g.Sight, 0, sights.Count - 1) : 0;
            int barrelIdx = barrels != null ? Mathf.Clamp(g.Barrel, 0, barrels.Count - 1) : 0;

            _model = new GameObject("HTF1v1_ClassPreviewModel");
            _model.layer = _layer;
            string name = LoadoutService.DisplayName(prefab);
            var me = Player.LocalPlayer;
            bool skinOn = g.Skin > 0 && (g.Skin != WeaponSkins.Dragon ? true : (WeaponSkins.CanPick(g.Skin) && WeaponSkins.IsSniper(name)));

            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!r || r == hands) continue;
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                if (!Included(r.transform, root, sights, sightIdx, barrels, barrelIdx, laser, g.Laser)) continue;
                var go = new GameObject(r.name);
                go.layer = _layer;
                go.transform.SetParent(_model.transform, false);
                go.transform.localPosition = root.InverseTransformPoint(r.transform.position);
                go.transform.localRotation = Quaternion.Inverse(root.rotation) * r.transform.rotation;
                Mesh mesh = null;
                if (r is SkinnedMeshRenderer smr)
                {
                    var baked = new Mesh();
                    try { smr.BakeMesh(baked); mesh = baked; _created.Add(baked); }
                    catch (System.Exception) { Object.Destroy(baked); mesh = smr.sharedMesh; }
                }
                else
                {
                    var mf = r.GetComponent<MeshFilter>();
                    mesh = mf ? mf.sharedMesh : null;
                    go.transform.localScale = r.transform.lossyScale;
                }
                if (!mesh) { Object.Destroy(go); continue; }
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = skinOn ? WeaponSkins.MaterialsFor(g.Skin, r.sharedMaterials, _created) : r.sharedMaterials;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (skinOn && g.Skin == WeaponSkins.Dragon && firePoint)
            {
                var fp = new GameObject("FirePoint");
                fp.layer = _layer;
                fp.transform.SetParent(_model.transform, false);
                fp.transform.localPosition = root.InverseTransformPoint(firePoint.position);
                fp.transform.localRotation = Quaternion.Inverse(root.rotation) * firePoint.rotation;
                var head = WeaponSkins.BuildDragonHead(fp.transform, true, _created);
                foreach (var tr in head.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = _layer;
            }

            // Centre the model on the origin and back the camera off so it fills the frame.
            var rends = _model.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray();
            if (rends.Length == 0) { _error = "Nothing to show"; Object.Destroy(_model); _model = null; return; }
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            _model.transform.position = Origin - (b.center - _model.transform.position);
            float radius = Mathf.Max(0.15f, b.extents.magnitude);
            float dist = radius / Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
            _cam.transform.position = Origin + new Vector3(0f, radius * 0.25f, -dist);
            _cam.transform.LookAt(Origin);
            _hasModel = true;
            Enable(true);
        }

        /// <summary>Whether a prefab renderer belongs in the preview given the chosen attachments.</summary>
        private static bool Included(Transform t, Transform root, List<Sight> sights, int sightIdx, List<BarrelAttachment> barrels, int barrelIdx, LaserSight laser, bool laserOn)
        {
            for (var cur = t; cur && cur != root; cur = cur.parent)
            {
                if (sights != null)
                    for (int i = 0; i < sights.Count; i++)
                        if (sights[i] && cur == sights[i].transform) { if (i != sightIdx) return false; return ActiveBelow(t, cur); }
                if (barrels != null)
                    for (int i = 0; i < barrels.Count; i++)
                        if (barrels[i] && cur == barrels[i].transform) { if (i != barrelIdx) return false; return ActiveBelow(t, cur); }
                if (laser && cur == laser.transform) { if (!laserOn) return false; return ActiveBelow(t, cur); }
            }
            return ActiveBelow(t, root);
        }

        /// <summary>Every object from t up to (not including) stop must be active in the prefab.</summary>
        private static bool ActiveBelow(Transform t, Transform stop)
        {
            for (var cur = t; cur && cur != stop; cur = cur.parent) if (!cur.gameObject.activeSelf) return false;
            return true;
        }
    }
}
