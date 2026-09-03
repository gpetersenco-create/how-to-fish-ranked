using HowToFish1v1.Core;
using HowToFish1v1.Match;
using HowToFish1v1.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using S = HowToFish1v1.UI.RankedStyles;

namespace HowToFish1v1.UI
{
    /// <summary>
    /// "Ranked" button on the main menu (cloned from the game's Host button so it looks native) and the full-screen page it
    /// opens, laid out like a shooter's ranked screen: tab bar, stats column, previous / current / next rank emblems with a
    /// rank-points bar, match history column, leaderboard, and a Matchmake button. Drawn on a 1920x1080 design canvas.
    /// </summary>
    public static class RankedMenu
    {
        private const string MenuButtonsPath = "CanvasHolder/MainMenuCanvas/MainMenuButtons (To toggle)";
        private const string HostButtonPath = MenuButtonsPath + "/MainMenuLayout/HostButton";
        private const string CharacterButtonPath = MenuButtonsPath + "/MainMenuLayout/CharacterButton";
        private const string PageKey = "ranked-page";
        private const string TabKey = "ranked-tab";

        private enum Tab { Overview, MyRank, Leaderboard, RankRewards, Gameplay, MatchFormat, Maps }
        private static readonly string[] TabNames = { "OVERVIEW", "MY RANK", "LEADERBOARD", "RANK REWARDS", "GAMEPLAY", "MATCH FORMAT", "MAPS" };
        private const float TabW = 190f;

        private static GameObject _button;
        private static GameObject _menuButtons;
        private static bool _pageOpen;
        private static bool _drawLogged;
        private static Tab _tab = Tab.MyRank;
        private static float _underlineX = -1f;
        private static MatchMode _mode = MatchMode.OneVOne;
        private static int _map;
        private static string _status = "";

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
                foreach (var comp in tmp.GetComponents<Component>())
                    if (comp && comp.GetType().Name.IndexOf("Locali", System.StringComparison.OrdinalIgnoreCase) >= 0) Object.Destroy(comp);
                tmp.text = "Ranked";
            }
            var btn = _button.GetComponent<Button>();
            if (btn)
            {
                btn.onClick = new Button.ButtonClickedEvent();
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
            _underlineX = -1f;
            S.MarkOpen(PageKey);
            S.MarkOpen(TabKey);
            if (_menuButtons) _menuButtons.SetActive(false);
        }

        private static void ClosePage()
        {
            _pageOpen = false;
            if (_menuButtons && MainMenuManager.IsInMenu) _menuButtons.SetActive(true);
        }

        private static void SetTab(Tab t)
        {
            if (_tab == t) return;
            _tab = t;
            S.MarkOpen(TabKey);
        }

        // ------------------------------------------------------------------ drawing

        public static void Draw()
        {
            if (!IsOpen) return;
            float open = S.Ease(PageKey, 0.35f);
            var saved = S.BeginCanvas();
            S.DrawBackground(open);
            GUI.color = new Color(1f, 1f, 1f, open);

            DrawTabBar();
            // Tab content slides up and fades in on every tab change.
            float tabEase = S.Ease(TabKey, 0.28f);
            GUI.color = new Color(1f, 1f, 1f, open * tabEase);
            GUI.matrix = saved;
            S.BeginCanvas((1f - tabEase) * 24f);
            switch (_tab)
            {
                case Tab.Overview: DrawOverview(); break;
                case Tab.MyRank: DrawMyRank(); break;
                case Tab.Leaderboard: DrawLeaderboard(); break;
                case Tab.RankRewards: DrawRankRewards(); break;
                case Tab.Gameplay: DrawGameplay(); break;
                case Tab.MatchFormat: DrawMatchFormat(); break;
                case Tab.Maps: DrawMaps(); break;
            }
            GUI.color = new Color(1f, 1f, 1f, open);
            GUI.matrix = saved;
            S.BeginCanvas();
            DrawFooter();
            GUI.color = Color.white;
            GUI.matrix = saved;

            if (!_drawLogged && Event.current.type == EventType.Repaint)
            {
                _drawLogged = true;
                Plugin.Log.LogInfo($"Ranked page drawn at {Screen.width}x{Screen.height}, menu buttons hidden={(_menuButtons && !_menuButtons.activeSelf)}");
            }
        }

        private static void DrawTabBar()
        {
            S.Box(new Rect(-20, -30, S.DesignW + 40, 122), S.PanelColor, 22f);
            S.Rule(0, 91, S.DesignW);
            if (S.Btn(new Rect(24, 22, 60, 48), "<", S.Button)) ClosePage();
            float x = 120;
            float targetX = -1f;
            // The active tab sits in a gold-dim pill that glides between tabs; the others brighten on hover.
            if (_underlineX < 0f) _underlineX = 120 + (int)_tab * TabW;
            if (Event.current.type == EventType.Repaint) _underlineX = Mathf.Lerp(_underlineX, 120 + (int)_tab * TabW, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 14f));
            S.Box(new Rect(_underlineX + 10, 22, TabW - 20, 48), new Color(0.52f, 0.42f, 0.16f, 0.55f), 24f);
            S.Outline(new Rect(_underlineX + 10, 22, TabW - 20, 48), new Color(1f, 0.85f, 0.4f, 0.5f), 1.5f, 24f);
            for (int i = 0; i < TabNames.Length; i++)
            {
                bool on = (int)_tab == i;
                var r = new Rect(x, 0, TabW, 92);
                if (GUI.Button(r, TabNames[i], on ? S.TabOn : S.Tab)) SetTab((Tab)i);
                if (on) targetX = x;
                x += TabW;
            }
            // Player chip: emblem, name, rank and RP.
            var ladder = RankService.Ladder;
            int tier = ladder.TierIndex(RankService.Points);
            string me = Player.LocalPlayer ? Player.LocalPlayer.SteamName : (SteamManagerName());
            float cx = S.DesignW - 440;
            S.Box(new Rect(cx, 14, 420, 64), S.PanelLightColor, 32f);
            S.Emblem(cx + 34, 18, 56, tier);
            GUI.Label(new Rect(cx + 72, 18, 330, 30), me, S.H2);
            GUI.Label(new Rect(cx + 72, 46, 330, 26), $"{ladder.TierName(RankService.Points).ToUpperInvariant()}   {RankService.Points} RP", S.GoldText);
        }

        private static string SteamManagerName()
        {
            try { return Steamworks.SteamFriends.GetPersonaName(); } catch (System.Exception) { return "You"; }
        }

        private static void DrawFooter()
        {
            GUI.Label(new Rect(40, S.DesignH - 60, 900, 30), "Matchmake opens an invite-only Steam lobby. Invite friends from the lobby screen; they need this mod too.", S.Small);
            if (!string.IsNullOrEmpty(_status)) GUI.Label(new Rect(40, S.DesignH - 95, 900, 30), _status, S.Body);
            if (S.Btn(new Rect(S.DesignW - 420, S.DesignH - 110, 380, 70), "MATCHMAKE", S.BigButton, 14f)) Host(steam: true);
            if (S.Btn(new Rect(S.DesignW - 700, S.DesignH - 110, 260, 70), "SOLO PRACTICE", S.Button, 14f)) Host(steam: false);
            GUI.Label(new Rect(S.DesignW - 700, S.DesignH - 150, 660, 30), $"{MatchModes.Name(_mode)}  on  {ArenaLayout.MapNames[_map]}", S.Small);
        }

        // ---------------------------------------------------------------- MY RANK

        private static void DrawMyRank()
        {
            var ladder = RankService.Ladder;
            int pts = RankService.Points;
            int tier = ladder.TierIndex(pts);
            float e = S.Ease(TabKey, 0.9f);

            // Left: season + stats in a rounded card
            float lx = 60, ly = 130;
            S.Box(new Rect(lx - 20, ly - 20, 520, 640), S.PanelColor, 18f);
            GUI.Label(new Rect(lx, ly, 480, 44), "SEASON 1: MASTER BAIT", S.H1);
            GUI.Label(new Rect(lx, ly + 48, 480, 60), "Play ranked matches to improve your rank and unlock the next tier. Ranks are stored on this PC per Steam account.", S.Small);
            float sy = ly + 130;
            Stat(lx, sy, "Wins", Mathf.RoundToInt(RankService.Wins * e).ToString());
            Stat(lx + 220, sy, "K/D Ratio", (RankService.KdRatio * e).ToString("0.0"));
            Stat(lx, sy + 110, "Losses", Mathf.RoundToInt(RankService.Losses * e).ToString());
            Stat(lx + 220, sy + 110, "Kills", Mathf.RoundToInt(RankService.Kills * e).ToString());
            Stat(lx, sy + 220, "Win Rate", (RankService.WinRate * 100f * e).ToString("0") + "%");
            Stat(lx + 220, sy + 220, "Matches Played", Mathf.RoundToInt(RankService.MatchesPlayed * e).ToString());
            GUI.Label(new Rect(lx, sy + 330, 400, 34), "Peak Rank", S.StatLabel);
            GUI.Label(new Rect(lx, sy + 360, 440, 44), ladder.TierName(RankService.Peak).ToUpperInvariant(), S.H1);

            // Center: previous / current / next emblems
            float cx = 960;
            int prevTier = Mathf.Max(0, tier - 1), nextTier = Mathf.Min(ladder.Names.Length - 1, tier + 1);
            S.Emblem(cx - 330, 300, 150, prevTier, tier > 0 ? ladder.Names[prevTier] : "-", "Previous Rank", dim: true);
            S.Emblem(cx, 250, 260, tier, ladder.Names[tier], "Current Rank", dim: false, glow: true);
            S.Emblem(cx + 330, 300, 150, nextTier, tier < ladder.Names.Length - 1 ? ladder.Names[nextTier] : "-", "Next Rank", dim: true);

            // Rank points bar fills up on open
            float bx = cx - 420, by = 640, bw = 840;
            int inTier = pts - tier * ladder.PointsPerTier;
            float frac = tier >= ladder.Names.Length - 1 ? 1f : Mathf.Clamp01(inTier / (float)ladder.PointsPerTier);
            frac *= e;
            GUI.Label(new Rect(bx, by - 34, 200, 30), "0", S.Small);
            GUI.Label(new Rect(bx + bw - 200, by - 34, 200, 30), ladder.PointsPerTier.ToString(), S.SmallRight);
            S.Bar(new Rect(bx, by, bw, 16), frac, S.GoldColor);
            GUI.Label(new Rect(bx, by + 22, bw, 30), $"{Mathf.RoundToInt(inTier * e)} RP", S.Body);
            GUI.Label(new Rect(bx - 100, by + 60, bw + 200, 60),
                $"RANK POINTS: earn Rank Points (RP) by winning ranked matches (+{ladder.WinPoints}) and rank up every {ladder.PointsPerTier} RP. Losses cost {ladder.LossPoints} RP.", S.GoldText);

            // Right: match history cards slide in one after another
            float rx = 1520, ry = 130;
            GUI.Label(new Rect(rx, ry, 360, 44), "MATCH HISTORY", S.H1);
            float hy = ry + 60;
            if (RankService.History.Count == 0)
            {
                S.Box(new Rect(rx, hy, 360, 90), S.PanelLightColor, 14f);
                GUI.Label(new Rect(rx + 20, hy + 28, 320, 34), "NO MATCHES YET", S.Body);
            }
            int idx = 0;
            foreach (var h in RankService.History)
            {
                if (hy > S.DesignH - 260) break;
                float ce = S.Ease(TabKey, 0.35f, 0.06f * idx++);
                float ox = (1f - ce) * 60f;
                var saved = GUI.color; GUI.color = new Color(1f, 1f, 1f, saved.a * ce);
                S.Box(new Rect(rx + ox, hy, 360, 96), S.PanelLightColor, 14f);
                S.Box(new Rect(rx + ox, hy, 6, 96), h.Won ? S.GreenColor : S.RedColor, 3f);
                GUI.Label(new Rect(rx + ox + 16, hy + 8, 330, 28), $"{h.When}   {h.Mode} on {h.Map}", S.Small);
                GUI.Label(new Rect(rx + ox + 16, hy + 40, 330, 40), $"{(h.Won ? "WIN" : "LOSS")}   {(h.Delta >= 0 ? "+" : "")}{h.Delta} RP   {h.Kills}K / {h.Deaths}D", h.Won ? S.GoldText : S.Body);
                GUI.color = saved;
                hy += 106;
            }
        }

        private static readonly Color _texBar = new Color(0.22f, 0.26f, 0.32f);

        private static void Stat(float x, float y, string label, string value)
        {
            S.Box(new Rect(x - 12, y - 6, 200, 96), new Color(1f, 1f, 1f, 0.05f), 12f);
            S.Box(new Rect(x - 12, y - 6, 4, 96), S.GoldDimColor, 2f);
            GUI.Label(new Rect(x, y, 190, 34), label.ToUpperInvariant(), S.StatLabel);
            GUI.Label(new Rect(x, y + 30, 190, 60), value, S.Stat);
        }

        // ---------------------------------------------------------------- LEADERBOARD

        private static void DrawLeaderboard()
        {
            var ladder = RankService.Ladder;
            var top = Leaderboard.Top(25);
            S.Shadowed(new Rect(60, 130, 900, 50), "LEADERBOARD  <size=55%>top 25</size>", S.Title); S.Rule(60, 184, 240);
            if (Leaderboard.IsGlobal)
                GUI.Label(new Rect(60, 180, 1200, 40), "Global standings of everyone running the mod. Your own rank is reported after each match." + (string.IsNullOrEmpty(CloudRanks.Status) ? "" : "   " + CloudRanks.Status), S.Small);
            else
                GUI.Label(new Rect(60, 180, 1300, 40), (CloudRanks.Enabled ? (string.IsNullOrEmpty(CloudRanks.Status) ? "Global leaderboard loading..." : CloudRanks.Status) + "   Showing players you have met meanwhile." : "Global leaderboard is off in the config. Showing everyone you have played ranked with."), S.Small);
            if (CloudRanks.Enabled && Event.current.type == EventType.Repaint) Plugin.Instance.StartCoroutine(CloudRanks.Refresh());
            float w = 1400, x = (S.DesignW - w) / 2f, y = 240;
            S.Box(new Rect(x, y, w, 40), S.PanelColor, 10f);
            GUI.Label(new Rect(x + 20, y + 5, 60, 30), "#", S.Small);
            GUI.Label(new Rect(x + 90, y + 5, 90, 30), "RANK", S.Small);
            GUI.Label(new Rect(x + 200, y + 5, 600, 30), "PLAYER", S.Small);
            GUI.Label(new Rect(x + 900, y + 5, 240, 30), "TIER", S.Small);
            GUI.Label(new Rect(x + 1150, y + 5, 120, 30), "RP", S.SmallRight);
            GUI.Label(new Rect(x + 1280, y + 5, 100, 30), "SEEN", S.SmallRight);
            float ry = y + 48;
            for (int i = 0; i < top.Count; i++)
            {
                var p = top[i];
                if (ry > S.DesignH - 220) { GUI.Label(new Rect(x, ry, w, 30), $"... and {top.Count - i} more", S.SmallCenter); break; }
                bool me = p.SteamId == RankService.LocalId;
                float ce = S.Ease(TabKey, 0.3f, 0.03f * i);
                var saved = GUI.color; GUI.color = new Color(1f, 1f, 1f, saved.a * ce);
                float ox = (1f - ce) * 40f;
                S.Box(new Rect(x + ox, ry, w, 42), me ? S.PanelLightColor : S.PanelColor, 10f);
                if (i < 3) S.Box(new Rect(x + ox, ry, 6, 42), i == 0 ? S.GoldColor : (i == 1 ? new Color(0.75f, 0.75f, 0.78f) : new Color(0.8f, 0.5f, 0.25f)), 3f);
                GUI.Label(new Rect(x + ox + 20, ry + 4, 60, 34), (i + 1).ToString(), S.Body);
                S.Emblem(x + ox + 130, ry + 3, 36, ladder.TierIndex(p.Points));
                GUI.Label(new Rect(x + ox + 200, ry + 4, 680, 34), p.Name + (me ? "  (you)" : ""), me ? S.GoldText : S.Body);
                GUI.Label(new Rect(x + ox + 900, ry + 4, 240, 34), ladder.TierName(p.Points), S.Small);
                GUI.Label(new Rect(x + ox + 1150, ry + 4, 120, 34), p.Points.ToString(), S.Body);
                GUI.Label(new Rect(x + ox + 1280, ry + 4, 100, 34), p.LastSeen ?? "", S.SmallRight);
                GUI.color = saved;
                ry += 48;
            }
        }

        // ---------------------------------------------------------------- other tabs

        private static void DrawOverview()
        {
            var ladder = RankService.Ladder;
            S.Shadowed(new Rect(60, 130, 900, 50), "RANKED PLAY", S.Title); S.Rule(60, 184, 240);
            GUI.Label(new Rect(60, 200, 820, 200),
                "Fight friends in round-based 1v1, 2v2 and 3v3 or a free-for-all on small arenas built for duels. " +
                "Every match moves your Rank Points; climb from Master Baiter to Poseidon. Pick a mode in GAMEPLAY, a map in MAPS, then hit MATCHMAKE.", S.Body);
            S.Emblem(1350, 180, 300, ladder.TierIndex(RankService.Points), RankService.RankName, "Your Rank", dim: false, glow: true);
            Stat(60, 460, "Wins", RankService.Wins.ToString());
            Stat(280, 460, "Losses", RankService.Losses.ToString());
            Stat(500, 460, "K/D Ratio", RankService.KdRatio.ToString("0.0"));
            Stat(720, 460, "Rank Points", RankService.Points.ToString());
        }

        private static void DrawRankRewards()
        {
            var ladder = RankService.Ladder;
            int cur = ladder.TierIndex(RankService.Points);
            S.Shadowed(new Rect(60, 130, 900, 50), "RANK LADDER", S.Title); S.Rule(60, 184, 240);
            int n = ladder.Names.Length;
            float slot = Mathf.Min(180f, (S.DesignW - 120f) / n);
            for (int i = 0; i < n; i++)
            {
                float ce = S.Ease(TabKey, 0.3f, 0.04f * i);
                float cx = 60 + slot * i + slot / 2;
                var saved = GUI.color; GUI.color = new Color(1f, 1f, 1f, saved.a * ce);
                S.Emblem(cx, (i == cur ? 300 : 330) + (1f - ce) * 30f, i == cur ? 170 : 130, i, ladder.Names[i], i == cur ? "CURRENT" : $"{i * ladder.PointsPerTier} RP", dim: i != cur, glow: i == cur);
                GUI.color = saved;
            }
            GUI.Label(new Rect(60, 620, S.DesignW - 120, 40), $"Win +{ladder.WinPoints} RP, loss -{ladder.LossPoints} RP (free-for-all loss -{ladder.FfaLossPoints}). Rank Points never drop below 0.", S.Body);
        }

        private static void DrawGameplay()
        {
            S.Shadowed(new Rect(60, 130, 900, 50), "GAMEPLAY", S.Title); S.Rule(60, 184, 240);
            var blurbs = new System.Collections.Generic.Dictionary<MatchMode, string>
            {
                { MatchMode.OneVOne, "One kill wins the round.\nFirst to 6 rounds." },
                { MatchMode.TwoVTwo, "A round ends when a whole team is down.\nFirst to 6 rounds. 2 to 4 players." },
                { MatchMode.ThreeVThree, "A round ends when a whole team is down.\nFirst to 6 rounds. 2 to 6 players." },
                { MatchMode.SearchAndDestroy, "One life per round. Attackers plant at the\ncentre site, defenders stop them. First to 6." },
                { MatchMode.FreeForAll, "2 to 8 players. First to 10 kills.\nRespawn after a short killcam." },
                { MatchMode.OneInTheChamber, "Pistols, one bullet each, every hit kills.\nA kill earns a bullet. Knife when dry." },
                { MatchMode.SniperOnly, "Everyone gets the sniper and nothing else.\nFree-for-all rules." },
                { MatchMode.KnifeOnly, "No guns. Knife on V, one hit kills.\nFree-for-all rules." },
                { MatchMode.Trickshot, "Solo practice. Jump off the tower and\nhit a bot mid-air. Miss and you go back up." },
            };
            const int cols = 4;
            float cardW = 430f, cardH = 230f, gapX = 20f, gapY = 14f;
            for (int i = 0; i < MatchModes.All.Length; i++)
            {
                var m = MatchModes.All[i];
                float ce = S.Ease(TabKey, 0.3f, 0.05f * i);
                int col = i % cols, row = i / cols;
                var r = new Rect(60 + col * (cardW + gapX), 205 + row * (cardH + gapY) + (1f - ce) * 40f, cardW, cardH);
                bool on = m == _mode;
                var saved = GUI.color; GUI.color = new Color(1f, 1f, 1f, saved.a * ce);
                S.Box(r, on ? S.PanelLightColor : S.PanelColor, 16f);
                if (on) S.Outline(r, S.GoldColor, 2f, 16f);
                GUI.Label(new Rect(r.x + 20, r.y + 14, r.width - 40, 40), MatchModes.Name(m).ToUpperInvariant(), S.H2);
                GUI.Label(new Rect(r.x + 20, r.y + 56, r.width - 40, 90), blurbs.TryGetValue(m, out var text) ? text : "", S.Small);
                if (S.Btn(new Rect(r.x + 20, r.y + cardH - 72, r.width - 40, 52), on ? "SELECTED" : "SELECT", on ? S.BigButton : S.Button)) _mode = m;
                GUI.color = saved;
            }
        }

        private static void DrawMatchFormat()
        {
            var c = Plugin.Cfg;
            S.Shadowed(new Rect(60, 130, 900, 50), "MATCH FORMAT", S.Title); S.Rule(60, 184, 240);
            string[][] rows =
            {
                new[] { "Rounds to win (1v1, 2v2, 3v3)", c.RoundsToWin.Value.ToString() },
                new[] { "Kills to win (free-for-all)", c.KillsToWin.Value.ToString() },
                new[] { "Countdown before each round", c.CountdownSeconds.Value + " s" },
                new[] { "Round end / killcam", c.RoundEndSeconds.Value + " s" },
                new[] { "Free-for-all respawn / killcam", c.FfaRespawnSeconds.Value + " s" },
                new[] { "Damage multiplier", c.DamageMultiplier.Value.ToString("0.0") + "x" },
                new[] { "Guns per loadout", c.MaxLoadoutGuns.Value.ToString() },
                new[] { "Sides", "swap every round" },
                new[] { "Saving", "disabled during ranked; your save is never touched" },
            };
            float y = 220;
            int i = 0;
            foreach (var row in rows)
            {
                float ce = S.Ease(TabKey, 0.3f, 0.03f * i++);
                float ox = (1f - ce) * 40f;
                S.Box(new Rect(60 + ox, y, 1000, 56), S.PanelLightColor, 10f);
                GUI.Label(new Rect(80 + ox, y + 12, 600, 34), row[0], S.Body);
                GUI.Label(new Rect(680 + ox, y + 12, 360, 34), row[1], S.GoldText);
                y += 66;
            }
            GUI.Label(new Rect(60, y + 20, 1000, 40), "Change these in BepInEx\\config\\com.gavin.howtofish1v1.cfg", S.Small);
        }

        private static void DrawMaps()
        {
            S.Shadowed(new Rect(60, 130, 900, 50), "MAPS", S.Title); S.Rule(60, 184, 240);
            // Four cards per row; two rows fit above the footer.
            const int cols = 4;
            float cardW = 430f, cardH = 330f, gapX = 20f, gapY = 16f;
            for (int i = 0; i < ArenaLayout.MapCount; i++)
            {
                float ce = S.Ease(TabKey, 0.3f, 0.05f * i);
                int col = i % cols, row = i / cols;
                var r = new Rect(60 + col * (cardW + gapX), 205 + row * (cardH + gapY) + (1f - ce) * 40f, cardW, cardH);
                bool on = i == _map;
                var saved = GUI.color; GUI.color = new Color(1f, 1f, 1f, saved.a * ce);
                S.Box(r, on ? S.PanelLightColor : S.PanelColor, 16f);
                if (on) S.Outline(r, S.GoldColor, 2f, 16f);
                GUI.DrawTexture(new Rect(r.x + 15, r.y + 14, cardW - 30, 210), MapPreview.Get(i), ScaleMode.StretchToFill, true, 0f, GUI.color, 0f, 10f);
                GUI.Label(new Rect(r.x + 20, r.y + 230, r.width - 200, 40), ArenaLayout.MapNames[i].ToUpperInvariant(), S.H2);
                GUI.Label(new Rect(r.x + 20, r.y + 262, r.width - 200, 30), MapSize(i), S.Small);
                if (S.Btn(new Rect(r.x + cardW - 180, r.y + 240, 160, 44), on ? "SELECTED" : "SELECT", on ? S.BigButton : S.Button)) _map = i;
                GUI.color = saved;
            }
            GUI.Label(new Rect(60, 900, S.DesignW - 120, 30), "Blue and orange squares are the team pads; green dots are free-for-all spawns.", S.Small);
        }

        private static string MapSize(int i)
        {
            var l = ArenaLayout.Create(i);
            float w = l.HalfWidth * 2f, d = l.HalfDepth * 2f;
            string size = w * d >= 2500f ? "Large" : w * d >= 1200f ? "Medium" : "Small";
            return ArenaLayout.IsSoloMap(i) ? "Solo practice" : $"{size}   {w:0} x {d:0} m";
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
    }
}
