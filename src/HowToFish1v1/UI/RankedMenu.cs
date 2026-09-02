using HowToFish1v1.Core;
using HowToFish1v1.Match;
using HowToFish1v1.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HowToFish1v1.UI
{
    /// <summary>
    /// "Ranked" button on the main menu (cloned from the game's Host button so it looks native) and the full-screen page it
    /// opens, laid out like a shooter's ranked screen: tab bar, stats column, previous / current / next rank emblems with a
    /// rank-points bar, match history column, and a Matchmake button. Drawn on a 1920x1080 design canvas scaled to the screen.
    /// </summary>
    public static class RankedMenu
    {
        private const string MenuButtonsPath = "CanvasHolder/MainMenuCanvas/MainMenuButtons (To toggle)";
        private const string HostButtonPath = MenuButtonsPath + "/MainMenuLayout/HostButton";
        private const string CharacterButtonPath = MenuButtonsPath + "/MainMenuLayout/CharacterButton";
        private const float DesignW = 1920f, DesignH = 1080f;

        private enum Tab { Overview, MyRank, RankRewards, Gameplay, MatchFormat, Maps }
        private static readonly string[] TabNames = { "OVERVIEW", "MY RANK", "RANK REWARDS", "GAMEPLAY", "MATCH FORMAT", "MAPS" };

        private static GameObject _button;
        private static GameObject _menuButtons;
        private static bool _pageOpen;
        private static bool _drawLogged;
        private static Tab _tab = Tab.MyRank;
        private static MatchMode _mode = MatchMode.OneVOne;
        private static int _map;
        private static string _status = "";

        private static Texture2D _bg, _panel, _panelLight, _gold, _bar, _barBg, _white;
        private static GUIStyle _tab_, _tabOn, _title, _h1, _h2, _body, _small, _stat, _statLabel, _bigButton, _button_, _gold_;

        // Set when hosting from this menu; consumed by Plugin.Update once the local player exists.
        public static bool PendingHostSetup;
        public static MatchMode PendingMode;
        public static int PendingMap;

        public static bool IsOpen => _pageOpen && MainMenuManager.IsInMenu;

        public static void Update()
        {
            if (!MainMenuManager.IsInMenu) { if (_pageOpen) ClosePage(); return; }
            if (_pageOpen && Input.GetKeyDown(KeyCode.Escape)) ClosePage();
            if (_button)
            {
                // The game re-enables its menu buttons and re-localizes labels on its own schedule; keep our state on top.
                if (_pageOpen && _menuButtons && _menuButtons.activeSelf) _menuButtons.SetActive(false);
                var label = _button.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label && label.text != "Ranked") label.text = "Ranked";
                return;
            }
            var host = GameObject.Find(HostButtonPath);
            var character = GameObject.Find(CharacterButtonPath);
            _menuButtons = GameObject.Find(MenuButtonsPath);
            if (!host) return;
            _button = Object.Instantiate(host, host.transform.parent);
            _button.name = "RankedButton";
            _button.transform.SetSiblingIndex((character ? character.transform.GetSiblingIndex() : host.transform.GetSiblingIndex()) + 1);
            var tmp = _button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp)
            {
                // Strip any localization component that would overwrite the label with "Host Game".
                foreach (var comp in tmp.GetComponents<Component>())
                    if (comp && comp.GetType().Name.IndexOf("Locali", System.StringComparison.OrdinalIgnoreCase) >= 0) Object.Destroy(comp);
                tmp.text = "Ranked";
            }
            var btn = _button.GetComponent<Button>();
            if (btn)
            {
                btn.onClick = new Button.ButtonClickedEvent(); // drop the inspector-wired Host handler
                btn.onClick.AddListener(OpenPage);
            }
            Plugin.Log.LogInfo("Ranked button added to the main menu");
            if (Plugin.Cfg.DumpMenu.Value) { OpenPage(); Plugin.Log.LogInfo("Ranked page auto-opened (DumpMenu)"); }
        }

        private static void OpenPage()
        {
            _pageOpen = true;
            _status = "";
            _tab = Tab.MyRank;
            if (_menuButtons) _menuButtons.SetActive(false);
        }

        private static void ClosePage()
        {
            _pageOpen = false;
            if (_menuButtons && MainMenuManager.IsInMenu) _menuButtons.SetActive(true);
        }

        // ------------------------------------------------------------------ drawing

        public static void Draw()
        {
            if (!IsOpen) return;
            EnsureStyles();
            var saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / DesignW, Screen.height / DesignH, 1f));
            GUI.DrawTexture(new Rect(0, 0, DesignW, DesignH), _bg);
            // Subtle diagonal light band like the reference art
            GUI.color = new Color(1f, 1f, 1f, 0.05f);
            GUI.DrawTexture(new Rect(1100, -200, 500, 1600), _white);
            GUI.color = Color.white;

            DrawTabBar();
            switch (_tab)
            {
                case Tab.Overview: DrawOverview(); break;
                case Tab.MyRank: DrawMyRank(); break;
                case Tab.RankRewards: DrawRankRewards(); break;
                case Tab.Gameplay: DrawGameplay(); break;
                case Tab.MatchFormat: DrawMatchFormat(); break;
                case Tab.Maps: DrawMaps(); break;
            }
            DrawFooter();
            GUI.matrix = saved;

            if (!_drawLogged && Event.current.type == EventType.Repaint)
            {
                _drawLogged = true;
                Plugin.Log.LogInfo($"Ranked page drawn at {Screen.width}x{Screen.height}, menu buttons hidden={(_menuButtons && !_menuButtons.activeSelf)}");
            }
        }

        private static void DrawTabBar()
        {
            GUI.DrawTexture(new Rect(0, 0, DesignW, 92), _panel);
            if (GUI.Button(new Rect(24, 22, 60, 48), "<", _button_)) ClosePage();
            float x = 120;
            for (int i = 0; i < TabNames.Length; i++)
            {
                bool on = (int)_tab == i;
                var r = new Rect(x, 0, 200, 92);
                if (GUI.Button(r, TabNames[i], on ? _tabOn : _tab_)) _tab = (Tab)i;
                if (on) GUI.DrawTexture(new Rect(x + 30, 86, 140, 4), _gold);
                x += 200;
            }
            GUI.Label(new Rect(DesignW - 420, 26, 400, 40), "HOW TO FISH  |  RANKED", _small);
        }

        private static void DrawFooter()
        {
            GUI.Label(new Rect(40, DesignH - 60, 900, 30), "Host Ranked opens an invite-only Steam lobby. Invite friends from the Steam overlay; they need this mod too.", _small);
            if (!string.IsNullOrEmpty(_status)) GUI.Label(new Rect(40, DesignH - 95, 900, 30), _status, _body);
            if (GUI.Button(new Rect(DesignW - 420, DesignH - 110, 380, 70), "MATCHMAKE", _bigButton)) Host(steam: true);
            if (GUI.Button(new Rect(DesignW - 700, DesignH - 110, 260, 70), "SOLO PRACTICE", _button_)) Host(steam: false);
            GUI.Label(new Rect(DesignW - 700, DesignH - 150, 660, 30), $"{MatchModes.Name(_mode)}  on  {ArenaLayout.MapNames[_map]}", _small);
        }

        // ---------------------------------------------------------------- MY RANK

        private static void DrawMyRank()
        {
            var ladder = RankService.Ladder;
            int pts = RankService.Points;
            int tier = ladder.TierIndex(pts);

            // Left: season + stats
            float lx = 60, ly = 130;
            GUI.Label(new Rect(lx, ly, 520, 44), "SEASON 1: MASTER BAIT", _h1);
            GUI.Label(new Rect(lx, ly + 48, 520, 60), "Play ranked matches to improve your rank and unlock the next tier. Ranks are stored on this PC per Steam account.", _small);
            float sy = ly + 130;
            Stat(lx, sy, "Wins", RankService.Wins.ToString());
            Stat(lx + 200, sy, "K/D Ratio", RankService.KdRatio.ToString("0.0"));
            Stat(lx, sy + 110, "Losses", RankService.Losses.ToString());
            Stat(lx + 200, sy + 110, "Kills", RankService.Kills.ToString());
            Stat(lx, sy + 220, "Win Rate", (RankService.WinRate * 100f).ToString("0") + "%");
            Stat(lx + 200, sy + 220, "Matches Played", RankService.MatchesPlayed.ToString());
            GUI.Label(new Rect(lx, sy + 330, 400, 34), "Peak Rank", _statLabel);
            GUI.Label(new Rect(lx, sy + 360, 400, 44), ladder.TierName(RankService.Peak).ToUpperInvariant(), _h1);

            // Center: previous / current / next emblems
            float cx = 960;
            int prevTier = Mathf.Max(0, tier - 1), nextTier = Mathf.Min(ladder.Names.Length - 1, tier + 1);
            Emblem(cx - 330, 300, 150, prevTier, tier > 0 ? ladder.Names[prevTier] : "-", "Previous Rank", dim: true);
            Emblem(cx, 250, 260, tier, ladder.Names[tier], "Current Rank", dim: false, glow: true);
            Emblem(cx + 330, 300, 150, nextTier, tier < ladder.Names.Length - 1 ? ladder.Names[nextTier] : "-", "Next Rank", dim: true);

            // Rank points bar
            float bx = cx - 420, by = 640, bw = 840;
            int inTier = pts - tier * ladder.PointsPerTier;
            float frac = tier >= ladder.Names.Length - 1 ? 1f : Mathf.Clamp01(inTier / (float)ladder.PointsPerTier);
            GUI.Label(new Rect(bx, by - 34, 200, 30), "0", _small);
            GUI.Label(new Rect(bx + bw - 200, by - 34, 200, 30), ladder.PointsPerTier.ToString(), _smallRight);
            GUI.DrawTexture(new Rect(bx, by, bw, 14), _barBg);
            GUI.DrawTexture(new Rect(bx, by, bw * frac, 14), _bar);
            GUI.DrawTexture(new Rect(bx + bw * frac - 3, by - 6, 6, 26), _gold);
            GUI.Label(new Rect(bx, by + 22, bw, 30), $"{inTier} RP", _body);
            GUI.Label(new Rect(bx - 100, by + 60, bw + 200, 60),
                $"RANK POINTS: earn Rank Points (RP) by winning ranked matches (+{ladder.WinPoints}) and rank up every {ladder.PointsPerTier} RP. Losses cost {ladder.LossPoints} RP.", _gold_);

            // Right: match history
            float rx = 1520, ry = 130;
            GUI.Label(new Rect(rx, ry, 360, 44), "MATCH HISTORY", _h1);
            float hy = ry + 60;
            if (RankService.History.Count == 0)
            {
                GUI.DrawTexture(new Rect(rx, hy, 360, 90), _panelLight);
                GUI.Label(new Rect(rx + 20, hy + 28, 320, 34), "NO MATCHES YET", _body);
            }
            foreach (var h in RankService.History)
            {
                if (hy > DesignH - 260) break;
                GUI.DrawTexture(new Rect(rx, hy, 360, 96), _panelLight);
                GUI.Label(new Rect(rx + 16, hy + 8, 330, 28), $"{h.When}   {h.Mode} on {h.Map}", _small);
                GUI.Label(new Rect(rx + 16, hy + 40, 330, 40), $"{(h.Won ? "WIN" : "LOSS")}   {(h.Delta >= 0 ? "+" : "")}{h.Delta} RP   {h.Kills}K / {h.Deaths}D", h.Won ? _gold_ : _body);
                hy += 106;
            }
        }

        private static void Stat(float x, float y, string label, string value)
        {
            GUI.Label(new Rect(x, y, 190, 34), label, _statLabel);
            GUI.Label(new Rect(x, y + 30, 190, 60), value, _stat);
        }

        private static void Emblem(float centerX, float top, float size, int tier, string name, string caption, bool dim, bool glow = false)
        {
            var tex = RankEmblems.Get(tier);
            var r = new Rect(centerX - size / 2, top, size, size);
            if (glow)
            {
                GUI.color = new Color(1f, 0.9f, 0.5f, 0.18f);
                GUI.DrawTexture(new Rect(centerX - size * 0.62f, top - size * 0.12f, size * 1.24f, size * 1.24f), tex);
            }
            GUI.color = dim ? new Color(1f, 1f, 1f, 0.55f) : Color.white;
            GUI.DrawTexture(r, tex);
            var numeral = new GUIStyle(_h1) { fontSize = Mathf.RoundToInt(size * 0.28f), alignment = TextAnchor.MiddleCenter };
            numeral.normal.textColor = new Color(1f, 0.95f, 0.8f);
            GUI.Label(new Rect(r.x, r.y + size * 0.30f, size, size * 0.3f), RankEmblems.Numeral(tier), numeral);
            GUI.color = Color.white;
            GUI.Label(new Rect(centerX - 220, top + size + 8, 440, 30), caption, _smallCenter);
            GUI.Label(new Rect(centerX - 220, top + size + 36, 440, 40), name.ToUpperInvariant(), dim ? _bodyCenter : _h1Center);
        }

        // ---------------------------------------------------------------- other tabs

        private static void DrawOverview()
        {
            var ladder = RankService.Ladder;
            GUI.Label(new Rect(60, 130, 900, 50), "RANKED PLAY", _title);
            GUI.Label(new Rect(60, 200, 820, 200),
                "Fight friends in round-based 1v1, 2v2 and 3v3 or a free-for-all on small arenas built for duels. " +
                "Every match moves your Rank Points; climb from Master Baiter to Poseidon. Pick a mode in GAMEPLAY, a map in MAPS, then hit MATCHMAKE.", _body);
            Emblem(1350, 180, 300, ladder.TierIndex(RankService.Points), RankService.RankName, "Your Rank", dim: false, glow: true);
            Stat(60, 460, "Wins", RankService.Wins.ToString());
            Stat(260, 460, "Losses", RankService.Losses.ToString());
            Stat(460, 460, "K/D Ratio", RankService.KdRatio.ToString("0.0"));
            Stat(660, 460, "Rank Points", RankService.Points.ToString());
        }

        private static void DrawRankRewards()
        {
            var ladder = RankService.Ladder;
            int cur = ladder.TierIndex(RankService.Points);
            GUI.Label(new Rect(60, 130, 900, 50), "RANK LADDER", _title);
            int n = ladder.Names.Length;
            float slot = Mathf.Min(180f, (DesignW - 120f) / n);
            for (int i = 0; i < n; i++)
            {
                float cx = 60 + slot * i + slot / 2;
                Emblem(cx, i == cur ? 300 : 330, i == cur ? 170 : 130, i, ladder.Names[i], i == cur ? "CURRENT" : $"{i * ladder.PointsPerTier} RP", dim: i != cur, glow: i == cur);
            }
            GUI.Label(new Rect(60, 620, DesignW - 120, 40), $"Win +{ladder.WinPoints} RP, loss -{ladder.LossPoints} RP (free-for-all loss -{ladder.FfaLossPoints}). Rank Points never drop below 0.", _body);
        }

        private static void DrawGameplay()
        {
            GUI.Label(new Rect(60, 130, 900, 50), "GAMEPLAY", _title);
            string[] blurbs =
            {
                "One kill wins the round.\nFirst to 6 rounds.",
                "A round ends when a whole team is down.\nFirst to 6 rounds.",
                "A round ends when a whole team is down.\nFirst to 6 rounds.",
                "2 to 8 players. First to 10 kills.\nRespawn after 3 seconds."
            };
            for (int i = 0; i < MatchModes.All.Length; i++)
            {
                var m = MatchModes.All[i];
                var r = new Rect(60 + i * 450, 220, 420, 300);
                bool on = m == _mode;
                GUI.DrawTexture(r, on ? _panelLight : _panel);
                if (on) GUI.DrawTexture(new Rect(r.x, r.y, r.width, 6), _gold);
                GUI.Label(new Rect(r.x + 20, r.y + 30, r.width - 40, 60), MatchModes.Name(m).ToUpperInvariant(), _h1);
                GUI.Label(new Rect(r.x + 20, r.y + 110, r.width - 40, 100), blurbs[i], _body);
                if (GUI.Button(new Rect(r.x + 20, r.y + 220, r.width - 40, 56), on ? "SELECTED" : "SELECT", on ? _bigButton : _button_)) _mode = m;
            }
        }

        private static void DrawMatchFormat()
        {
            var c = Plugin.Cfg;
            GUI.Label(new Rect(60, 130, 900, 50), "MATCH FORMAT", _title);
            string[][] rows =
            {
                new[] { "Rounds to win (1v1, 2v2, 3v3)", c.RoundsToWin.Value.ToString() },
                new[] { "Kills to win (free-for-all)", c.KillsToWin.Value.ToString() },
                new[] { "Countdown before each round", c.CountdownSeconds.Value + " s" },
                new[] { "Free-for-all respawn delay", c.FfaRespawnSeconds.Value + " s" },
                new[] { "Damage multiplier", c.DamageMultiplier.Value.ToString("0.0") + "x" },
                new[] { "Guns per loadout", c.MaxLoadoutGuns.Value.ToString() },
                new[] { "Sides", "swap every round" },
                new[] { "Saving", "disabled during ranked; your save is never touched" },
            };
            float y = 220;
            foreach (var row in rows)
            {
                GUI.DrawTexture(new Rect(60, y, 1000, 56), _panelLight);
                GUI.Label(new Rect(80, y + 12, 600, 34), row[0], _body);
                GUI.Label(new Rect(680, y + 12, 360, 34), row[1], _gold_);
                y += 66;
            }
            GUI.Label(new Rect(60, y + 20, 1000, 40), "Change these in BepInEx\\config\\com.gavin.howtofish1v1.cfg", _small);
        }

        private static void DrawMaps()
        {
            GUI.Label(new Rect(60, 130, 900, 50), "MAPS", _title);
            for (int i = 0; i < ArenaLayout.MapCount; i++)
            {
                var r = new Rect(60 + i * 450, 220, 420, 380);
                bool on = i == _map;
                GUI.DrawTexture(r, on ? _panelLight : _panel);
                if (on) GUI.DrawTexture(new Rect(r.x, r.y, r.width, 6), _gold);
                GUI.DrawTexture(new Rect(r.x + 18, r.y + 20, 384, 256), MapPreview.Get(i));
                GUI.Label(new Rect(r.x + 20, r.y + 284, r.width - 40, 44), ArenaLayout.MapNames[i].ToUpperInvariant(), _h1);
                if (GUI.Button(new Rect(r.x + 20, r.y + 326, r.width - 40, 44), on ? "SELECTED" : "SELECT", on ? _bigButton : _button_)) _map = i;
            }
            GUI.Label(new Rect(60, 620, DesignW - 120, 40), "Blue and orange squares are the team pads; green dots are free-for-all spawns.", _small);
        }

        // ---------------------------------------------------------------- hosting

        private static void Host(bool steam)
        {
            if (!ConnectionManager.Instance) { _status = "Connection manager not ready"; return; }
            ModState.RankedSession = true;
            PendingHostSetup = true;
            PendingMode = _mode;
            PendingMap = _map;
            _status = "";
            ClosePage();
            Plugin.Log.LogInfo($"Ranked host: mode {MatchModes.Name(_mode)} map {ArenaLayout.MapNames[_map]} steam={steam}");
            if (steam) SteamManager.CreateLobby();
            else ConnectionManager.Instance.CreateOfflineLobby();
        }

        /// <summary>Called every frame; applies the pending host setup once the host is in the world.</summary>
        public static void ApplyPendingSetup()
        {
            if (!PendingHostSetup || !ModNet.IsHost || !Player.LocalPlayer || MainMenuManager.IsInMenu) return;
            PendingHostSetup = false;
            Plugin.Host.Open();
            Plugin.Host.SetMode(PendingMode);
            Plugin.Host.SetMap(PendingMap);
            LobbyPanel.Open();
        }

        // ---------------------------------------------------------------- styles

        private static GUIStyle _smallRight, _smallCenter, _bodyCenter, _h1Center;

        private static void EnsureStyles()
        {
            if (_title != null) return;
            _bg = Solid(new Color(0.06f, 0.09f, 0.13f, 1f));
            _panel = Solid(new Color(0.09f, 0.13f, 0.19f, 0.95f));
            _panelLight = Solid(new Color(0.14f, 0.20f, 0.28f, 0.95f));
            _gold = Solid(new Color(0.95f, 0.78f, 0.25f));
            _bar = Solid(new Color(0.95f, 0.78f, 0.25f));
            _barBg = Solid(new Color(0.22f, 0.26f, 0.32f));
            _white = Solid(Color.white);
            Color white = Color.white, goldc = new Color(0.95f, 0.78f, 0.25f), muted = new Color(0.72f, 0.77f, 0.84f);

            _tab_ = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _tab_.normal.textColor = muted;
            _tab_.hover.textColor = white;
            _tabOn = new GUIStyle(_tab_);
            _tabOn.normal.textColor = white;
            _tabOn.hover.textColor = white;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _title.normal.textColor = white;
            _h1 = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _h1.normal.textColor = white;
            _h2 = new GUIStyle(_h1) { fontSize = 22 };
            _body = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleLeft, wordWrap = true };
            _body.normal.textColor = white;
            _small = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleLeft, wordWrap = true };
            _small.normal.textColor = muted;
            _smallRight = new GUIStyle(_small) { alignment = TextAnchor.MiddleRight };
            _smallCenter = new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter };
            _bodyCenter = new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter };
            _h1Center = new GUIStyle(_h1) { alignment = TextAnchor.MiddleCenter };
            _stat = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _stat.normal.textColor = white;
            _statLabel = new GUIStyle(_small);
            _gold_ = new GUIStyle(_body);
            _gold_.normal.textColor = goldc;
            _bigButton = new GUIStyle(GUI.skin.button) { fontSize = 26, fontStyle = FontStyle.Bold };
            _bigButton.normal.background = _gold; _bigButton.hover.background = Solid(new Color(1f, 0.86f, 0.4f)); _bigButton.active.background = _gold;
            _bigButton.normal.textColor = new Color(0.08f, 0.08f, 0.1f); _bigButton.hover.textColor = _bigButton.normal.textColor; _bigButton.active.textColor = _bigButton.normal.textColor;
            _button_ = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
            _button_.normal.background = _panelLight; _button_.hover.background = Solid(new Color(0.22f, 0.30f, 0.40f)); _button_.active.background = _panelLight;
            _button_.normal.textColor = white; _button_.hover.textColor = white; _button_.active.textColor = white;
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
