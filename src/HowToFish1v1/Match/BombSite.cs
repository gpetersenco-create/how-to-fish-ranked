using System.Collections.Generic;
using HowToFish1v1.Arena;
using HowToFish1v1.Core;
using HowToFish1v1.Net;
using HowToFish1v1.UI;
using UnityEngine;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// Search and Destroy on every client: the site marker on the map, the planted bomb with its blinking light and
    /// accelerating beep, the explosion, the plant/defuse prompt and progress, and the hold-to-plant input (the host
    /// does the timing). Everything reads the host's match state, so all clients agree.
    /// </summary>
    public static class BombSite
    {
        public const float Reach = 3.2f;

        private static GameObject _marker, _bomb, _light;
        private static Material _ringMat, _bombMat, _lightMat;
        private static int _builtMap = -1;
        private static bool _wasPlanted;
        private static float _nextBeep = -1f, _lastSend = -1f;
        private static bool _holding;
        private static float _explodeFlashUntil = -1f;
        private static readonly List<Object> _created = new List<Object>();

        public static bool IsMode => ModState.IsActive && ClientMatchView.HasState && MatchModes.IsBomb((MatchMode)ClientMatchView.Latest.Mode);
        public static Vector3 SitePos => ArenaBuilder.Origin + new Vector3(ArenaBuilder.Layout.SiteX, ArenaBuilder.Layout.SiteY, ArenaBuilder.Layout.SiteZ);
        public static bool Holding => _holding;

        /// <summary>The local player's role this round.</summary>
        public static bool IsAttacker => ClientMatchView.HasState && ClientMatchView.MyTeam == ClientMatchView.Latest.AttackersTeam;

        public static void Update()
        {
            if (!IsMode || !ArenaBuilder.IsBuilt)
            {
                if (_marker) Clear();
                if (_holding) SetHolding(false);
                return;
            }
            if (_builtMap != ArenaBuilder.MapIndex) Build();
            var s = ClientMatchView.Latest;
            bool planted = s.BombPlanted;
            if (planted != _wasPlanted)
            {
                _wasPlanted = planted;
                if (_bomb) _bomb.SetActive(planted);
                if (planted) { Announcer.Play("planted"); Hud.Popup(IsAttacker ? "BOMB PLANTED" : "DEFUSE THE BOMB"); _nextBeep = Time.unscaledTime; }
                else if (ModState.Phase == MatchPhase.RoundEnd && (s.StatusText ?? "").Contains("defused")) Announcer.Play("defused");
            }
            if (ModState.Phase == MatchPhase.RoundEnd && (s.StatusText ?? "").Contains("exploded") && _explodeFlashUntil < Time.unscaledTime - 5f && _wasPlanted)
            {
                Explode();
            }
            if (_bomb && _bomb.activeSelf)
            {
                // Faster blink and beep as the timer runs down.
                double left = ClientMatchView.BombSecondsLeft;
                float rate = Mathf.Lerp(0.18f, 1.1f, Mathf.Clamp01((float)left / 40f));
                bool on = (Time.unscaledTime % rate) < rate * 0.35f;
                if (_light) _light.SetActive(on);
                if (Time.unscaledTime >= _nextBeep) { _nextBeep = Time.unscaledTime + rate; HitSounds.PlayBeep(SitePos, Mathf.Clamp01(1f - (float)left / 40f)); }
            }
            UpdateInput();
        }

        private static void UpdateInput()
        {
            var me = Player.LocalPlayer;
            if (!me || me.Dying.IsDead || ModState.Phase != MatchPhase.Live || ModState.PanelOpen || KillCam.Active)
            {
                if (_holding) SetHolding(false);
                return;
            }
            bool near = Vector3.Distance(me.Transform.position, SitePos) <= Reach;
            bool allowed = CanWork();
            bool want = near && allowed && Input.GetKey(Plugin.Cfg.PlantKey.Value);
            if (want != _holding) SetHolding(want);
            else if (_holding && Time.unscaledTime - _lastSend > 0.5f) { ModNet.SendBomb(true); _lastSend = Time.unscaledTime; }
        }

        private static void SetHolding(bool on)
        {
            _holding = on;
            ModNet.SendBomb(on);
            _lastSend = Time.unscaledTime;
        }

        /// <summary>May the local player plant (attacker, not planted) or defuse (defender, planted) right now?</summary>
        public static bool CanWork()
        {
            if (!IsMode || ModState.Phase != MatchPhase.Live) return false;
            var s = ClientMatchView.Latest;
            return s.BombPlanted ? !IsAttacker : IsAttacker;
        }

        public static string Prompt()
        {
            var me = Player.LocalPlayer;
            if (!IsMode || !me || me.Dying.IsDead || ModState.Phase != MatchPhase.Live) return "";
            var s = ClientMatchView.Latest;
            bool near = Vector3.Distance(me.Transform.position, SitePos) <= Reach;
            if (!CanWork()) return "";
            string action = s.BombPlanted ? "DEFUSE" : "PLANT";
            return near ? $"HOLD {Plugin.Cfg.PlantKey.Value} TO {action}" : (s.BombPlanted ? "GET TO THE BOMB" : "GET TO THE SITE");
        }

        private static void Build()
        {
            Clear();
            _builtMap = ArenaBuilder.MapIndex;
            var pos = SitePos;
            var shader = ArenaMaterials.LitShader;
            _ringMat = new Material(ArenaMaterials.For(BoxKind.Yellow)) { name = "HTF1v1_SiteRing" };
            if (_ringMat.HasProperty("_BaseMap")) _ringMat.SetTexture("_BaseMap", null);
            if (_ringMat.HasProperty("_BumpMap")) _ringMat.SetTexture("_BumpMap", null);
            _ringMat.DisableKeyword("_NORMALMAP");
            _ringMat.EnableKeyword("_EMISSION");
            if (_ringMat.HasProperty("_EmissionColor")) _ringMat.SetColor("_EmissionColor", new Color(1f, 0.6f, 0.1f) * 2f);
            _created.Add(_ringMat);
            _marker = new GameObject("HTF1v1_BombSite");
            _marker.transform.position = pos;
            // A ring of short posts and a flat pad so the site is obvious from anywhere.
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f;
                var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(post.GetComponent<Collider>());
                post.name = "HTF1v1_SitePost";
                post.transform.SetParent(_marker.transform, false);
                post.transform.localPosition = new Vector3(Mathf.Cos(a) * 2.6f, 0.25f, Mathf.Sin(a) * 2.6f);
                post.transform.localScale = new Vector3(0.12f, 0.25f, 0.12f);
                post.GetComponent<MeshRenderer>().sharedMaterial = _ringMat;
            }
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(pad.GetComponent<Collider>());
            pad.name = "HTF1v1_SitePad";
            pad.transform.SetParent(_marker.transform, false);
            pad.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            pad.transform.localScale = new Vector3(2.4f, 0.02f, 2.4f);
            pad.GetComponent<MeshRenderer>().sharedMaterial = _ringMat;
            // The bomb: a dark case with a red light on top, hidden until planted.
            _bombMat = new Material(ArenaMaterials.For(BoxKind.Steel)) { name = "HTF1v1_Bomb" };
            if (_bombMat.HasProperty("_BaseColor")) _bombMat.SetColor("_BaseColor", new Color(0.15f, 0.15f, 0.17f));
            _created.Add(_bombMat);
            _lightMat = new Material(shader ? shader : Shader.Find("Sprites/Default")) { name = "HTF1v1_BombLight" };
            if (_lightMat.HasProperty("_BaseColor")) _lightMat.SetColor("_BaseColor", Color.red);
            _lightMat.EnableKeyword("_EMISSION");
            if (_lightMat.HasProperty("_EmissionColor")) _lightMat.SetColor("_EmissionColor", new Color(1f, 0.1f, 0.05f) * 5f);
            _created.Add(_lightMat);
            _bomb = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(_bomb.GetComponent<Collider>());
            _bomb.name = "HTF1v1_BombCase";
            _bomb.transform.SetParent(_marker.transform, false);
            _bomb.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            _bomb.transform.localScale = new Vector3(0.6f, 0.35f, 0.4f);
            _bomb.GetComponent<MeshRenderer>().sharedMaterial = _bombMat;
            _light = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(_light.GetComponent<Collider>());
            _light.name = "HTF1v1_BombLight";
            _light.transform.SetParent(_bomb.transform, false);
            _light.transform.localPosition = new Vector3(0.3f, 0.6f, 0f);
            _light.transform.localScale = new Vector3(0.2f, 0.35f, 0.3f);
            _light.GetComponent<MeshRenderer>().sharedMaterial = _lightMat;
            _bomb.SetActive(false);
            _wasPlanted = false;
        }

        private static void Explode()
        {
            _explodeFlashUntil = Time.unscaledTime + 0.6f;
            _wasPlanted = false;
            if (_bomb) _bomb.SetActive(false);
            Announcer.Play("exploded");
            HitSounds.PlayExplosion(SitePos);
            var me = Player.LocalPlayer;
            if (me && me.Transform) HitReactions.Hit(SitePos, 60);
        }

        /// <summary>White flash right after the explosion.</summary>
        public static float ExplosionFlash => Mathf.Clamp01((_explodeFlashUntil - Time.unscaledTime) / 0.6f);

        private static void Clear()
        {
            if (_marker) Object.Destroy(_marker);
            _marker = null; _bomb = null; _light = null;
            foreach (var o in _created) if (o) Object.Destroy(o);
            _created.Clear();
            _builtMap = -1;
            _wasPlanted = false;
        }
    }
}
