using System.Collections.Generic;
using System.Linq;
using HowToFish1v1.Core;
using HowToFish1v1.Match;
using HowToFish1v1.Net;
using HowToFish1v1.Net.Proto2;
using Steamworks;
using UnityEngine;
using S = HowToFish1v1.UI.RankedStyles;

namespace HowToFish1v1.UI
{
    /// <summary>
    /// Full-screen lobby: player cards per team (or a free-for-all grid), invite button, loadout picker with attachments,
    /// Ready, and for the host the mode/map pickers and Start. Opens by itself in ranked sessions and closes when the match starts.
    /// </summary>
    public static class LobbyPanel
    {
        public static bool IsOpen => ModState.PanelOpen;

        private static readonly List<LoadoutGun> _guns = new List<LoadoutGun>();
        private static bool _ready;
        private static string _hint = "";
        private static Vector2 _loadoutScroll;
        private static int _previewIndex;
        private const float PreviewH = 330f;
        private const float AttachH = 308f;

        public static void Toggle() { if (IsOpen) Close(); else Open(); }

        public static void Open()
        {
            if (!Player.LocalPlayer) { Plugin.Log.LogInfo("Join or host a game before opening the lobby"); return; }
            ModState.PanelOpen = true;
            S.MarkOpen("lobby");
            PlayerCamera.ToggleMouse(true);
            if (ModNet.IsHost && !Plugin.Host.IsOpen) Plugin.Host.Open();
        }

        public static void Close()
        {
            ModState.PanelOpen = false;
            PlayerCamera.ToggleMouse(false);
        }

        public static void Draw()
        {
            if (!IsOpen) return;
            float open = S.Ease("lobby", 0.35f);
            var saved = S.BeginCanvas((1f - open) * 24f);
            S.DrawBackground(open);
            GUI.color = new Color(1f, 1f, 1f, open);

            bool host = ModNet.IsHost;
            var s = ClientMatchView.Latest;
            bool has = ClientMatchView.HasState && ModState.IsActive;

            _cardIndex = 0;
            DrawHeader(host, has, s);
            if (!has)
            {
                GUI.Label(new Rect(0, 460, S.DesignW, 60), host ? "OPENING LOBBY..." : "WAITING FOR THE HOST TO OPEN THE LOBBY", S.H1Center);
                GUI.Label(new Rect(0, 520, S.DesignW, 40), "Press F5 to hide this screen.", S.SmallCenter);
            }
            else
            {
                var mode = (MatchMode)s.Mode;
                bool inLobby = (MatchPhase)s.Phase == MatchPhase.Lobby;
                if (MatchModes.IsFfa(mode)) DrawFfaGrid(s, host && inLobby);
                else DrawTeams(s, host && inLobby);
                DrawLoadout(inLobby);
                DrawFooter(host, inLobby, s);
            }
            GUI.color = Color.white;
            GUI.matrix = saved;
        }

        // ------------------------------------------------------------ pieces

        private static void DrawHeader(bool host, bool has, MatchStateBroadcast s)
        {
            S.Box(new Rect(-20, -30, S.DesignW + 40, 122), S.PanelColor, 22f);
            S.Rule(0, 91, S.DesignW);
            S.Shadowed(new Rect(40, 20, 500, 52), "RANKED LOBBY", S.Title);
            if (has)
            {
                var mode = (MatchMode)s.Mode;
                string map = ArenaLayout.MapNames[((s.MapIndex % ArenaLayout.MapCount) + ArenaLayout.MapCount) % ArenaLayout.MapCount];
                GUI.Label(new Rect(420, 26, 700, 40), $"{MatchModes.Name(mode).ToUpperInvariant()}   |   {map.ToUpperInvariant()}   |   {ClientMatchView.Players.Length}/{MatchModes.MaxPlayers(mode)} PLAYERS", S.H2);
            }
            bool steam = ConnectionManager.IsUsingSteam;
            GUI.enabled = steam;
            if (S.Btn(new Rect(S.DesignW - 560, 18, 260, 56), steam ? "INVITE FRIENDS" : "OFFLINE SESSION", S.Button)) Invite();
            GUI.enabled = true;
            if (S.Btn(new Rect(S.DesignW - 280, 18, 240, 56), host ? "END RANKED" : "HIDE (F5)", S.Button))
            {
                if (host) { Plugin.Host.Quit(); _ready = false; }
                else Close();
            }
        }

        private static void DrawTeams(MatchStateBroadcast s, bool canMove)
        {
            var mode = (MatchMode)s.Mode;
            int size = MatchModes.TeamSize(mode);
            var players = ClientMatchView.Players;
            float colW = 560, top = 120;
            float[] xs = { 60, 60 + colW + 40 };
            for (int team = 0; team < 2; team++)
            {
                float x = xs[team];
                int score = team == 0 ? s.TeamScoreA : s.TeamScoreB;
                S.Box(new Rect(x, top, colW, 56), S.Panel);
                GUI.DrawTexture(new Rect(x, top, 6, 56), team == 0 ? S.Gold : S.PanelHover);
                GUI.Label(new Rect(x + 20, top + 10, 300, 36), team == 0 ? "TEAM A" : "TEAM B", S.H1);
                GUI.Label(new Rect(x + colW - 120, top + 10, 100, 36), score.ToString(), S.H1Center);
                var members = players.Where(p => p.Team == team).ToList();
                float y = top + 70;
                for (int i = 0; i < size; i++)
                {
                    if (i < members.Count) DrawCard(x, y, colW, members[i], canMove, false);
                    else DrawEmptyCard(x, y, colW);
                    y += 130;
                }
            }
        }

        private static void DrawFfaGrid(MatchStateBroadcast s, bool canMove)
        {
            var players = ClientMatchView.Players.OrderByDescending(p => p.Kills).ToList();
            float top = 120, colW = 560;
            S.Box(new Rect(60, top, colW * 2 + 40, 56), S.Panel);
            GUI.DrawTexture(new Rect(60, top, 6, 56), S.Gold);
            GUI.Label(new Rect(80, top + 10, 600, 36), $"FREE-FOR-ALL   first to {s.KillsToWin} kills", S.H1);
            int max = MatchModes.MaxPlayers(MatchMode.FreeForAll);
            for (int i = 0; i < max; i++)
            {
                float x = 60 + (i % 2) * (colW + 40);
                float y = top + 70 + (i / 2) * 130;
                if (i < players.Count) DrawCard(x, y, colW, players[i], false, true);
                else DrawEmptyCard(x, y, colW);
            }
        }

        private static int _cardIndex;

        private static void DrawCard(float x, float y, float w, PlayerEntry p, bool canMove, bool showKills)
        {
            // Cards slide in one after another when the lobby opens.
            float ce = S.Ease("lobby", 0.3f, 0.05f * (_cardIndex++ % 8));
            x += (1f - ce) * 40f;
            bool me = p.Id == ModState.LocalOwnerId;
            S.Box(new Rect(x, y, w, 118), me ? S.PanelLightColor : S.PanelColor, 14f);
            if (me) S.Outline(new Rect(x, y, w, 118), new Color(1f, 0.85f, 0.4f, 0.35f), 1.5f, 14f);
            if (p.Ready) { S.Glow(new Rect(x, y, 12, 118), new Color(0.3f, 1f, 0.5f, 0.5f + 0.3f * Mathf.Sin(Time.unscaledTime * 3f)), 2f); S.Box(new Rect(x, y + 10, 6, 98), S.GreenColor, 3f); }
            int tier = RankService.Ladder.TierIndex(p.RankPoints);
            S.Emblem(x + 62, y + 12, 94, tier);
            GUI.Label(new Rect(x + 120, y + 12, w - 260, 36), p.Name + (me ? "  (you)" : ""), S.H1);
            GUI.Label(new Rect(x + 120, y + 48, w - 260, 28), RankService.Ladder.TierName(p.RankPoints).ToUpperInvariant() + $"   {p.RankPoints} RP", S.GoldText);
            GUI.Label(new Rect(x + 120, y + 78, w - 260, 28), (showKills ? $"{p.Kills} kills   " : "") + LoadoutService.Summary(p.Loadout), S.Small);
            string badge = !p.HasMod
                ? (string.IsNullOrEmpty(p.ModVersion) ? "NO MOD" : $"v{p.ModVersion}\nneeds {Plugin.Version}")
                : (p.Ready ? "READY" : "NOT READY");
            S.Box(new Rect(x + w - 150, y + 14, 136, 40), !p.HasMod ? S.RedColor : (p.Ready ? S.GreenColor : new Color(0.22f, 0.26f, 0.32f)), 8f);
            GUI.Label(new Rect(x + w - 150, y + 14, 136, 40), badge, S.SmallCenter);
            if (canMove && S.Btn(new Rect(x + w - 130, y + 62, 116, 40), "MOVE", S.ToggleButton)) Plugin.Host.MoveTeam(p.Id);
        }

        private static void DrawEmptyCard(float x, float y, float w)
        {
            S.Box(new Rect(x, y, w, 118), S.Panel);
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            GUI.Label(new Rect(x, y, w, 118), "WAITING FOR PLAYER...", S.BodyCenter);
            GUI.color = Color.white;
        }

        private static void DrawLoadout(bool inLobby)
        {
            float x = 1300, y = 120, w = 580;
            S.Box(new Rect(x, y, w, 56), S.Panel);
            GUI.DrawTexture(new Rect(x, y, 6, 56), S.Gold);
            int max = Mathf.Max(0, Plugin.Cfg.MaxLoadoutGuns.Value);
            GUI.Label(new Rect(x + 20, y + 10, 540, 36), $"YOUR LOADOUT   (pick up to {max})", S.H1);
            if (inLobby && _ready && ClientMatchView.Me is PlayerEntry me && !me.Ready) _ready = false;

            DrawPreview(x, y + 66, w);
            GUI.enabled = inLobby;
            // Scrollable area: gun toggles, then an attachment block per chosen gun.
            float areaTop = y + 66 + PreviewH + 10, areaH = S.DesignH - 130 - areaTop - 90;
            float contentH = LoadoutService.Weapons().Count * 46 + _guns.Count * (AttachH + 12) + 40 + 52 + 52;
            _loadoutScroll = GUI.BeginScrollView(new Rect(x, areaTop, w + 20, areaH), _loadoutScroll, new Rect(0, 0, w, contentH));
            float gy = 0;
            // The knife: always carried, one key, its own skin (local only: nobody else sees your knife).
            {
                S.Box(new Rect(0, gy, w, 44), S.Panel, 8f);
                byte ks = (byte)Mathf.Clamp(Plugin.Cfg.KnifeSkin.Value, 0, WeaponSkins.Count - 1);
                bool was = GUI.enabled; GUI.enabled = true;
                if (Cycle(8, gy + 3, w - 16, $"Knife ({Plugin.Cfg.KnifeKey.Value})", _skinNames, ref ks))
                {
                    if (!WeaponSkins.CanPick(ks)) ks = 0;
                    Plugin.Cfg.KnifeSkin.Value = ks;
                }
                GUI.enabled = was;
                gy += 52;
            }
            // The gun charm: hangs off the left side of every gun you hold; everyone sees it.
            {
                S.Box(new Rect(0, gy, w, 44), S.Panel, 8f);
                byte ch = (byte)Mathf.Clamp(Plugin.Cfg.Charm.Value, 0, 2);
                if (ch == 2 && !RankCharms.CanUseDev) ch = 1;
                bool was = GUI.enabled; GUI.enabled = true;
                if (Cycle(8, gy + 3, w - 16, "Charm", _charmNames, ref ch))
                {
                    if (ch == 2 && !RankCharms.CanUseDev) ch = 0;
                    Plugin.Cfg.Charm.Value = ch;
                    SendLoadout(_ready);
                }
                GUI.enabled = was;
                gy += 52;
            }
            foreach (var item in LoadoutService.Weapons())
            {
                int idx = _guns.FindIndex(g => g.ItemId == item.ID);
                bool on = idx >= 0;
                if (S.Btn(new Rect(0, gy, w, 40), (on ? "  [x]  " : "  [ ]  ") + LoadoutService.DisplayName(item).ToUpperInvariant(), on ? S.ToggleButtonOn : S.ToggleButton))
                {
                    if (on) { _guns.RemoveAt(idx); SendLoadout(false); }
                    else if (_guns.Count < max) { _guns.Add(new LoadoutGun(item.ID)); SendLoadout(false); }
                    else _hint = $"Only {max} guns per loadout";
                }
                gy += 46;
            }
            gy += 10;
            for (int i = 0; i < _guns.Count; i++)
            {
                gy = DrawAttachments(0, gy, w, i);
                gy += 12;
            }
            if (!string.IsNullOrEmpty(_hint)) GUI.Label(new Rect(0, gy, w, 30), _hint, S.Small);
            GUI.EndScrollView();

            if (S.Btn(new Rect(x, S.DesignH - 130 - 80, w, 70), _ready ? "READY  (click to unready)" : "READY UP", _ready ? S.BigButton : S.Button))
            {
                _ready = !_ready;
                _hint = "";
                SendLoadout(_ready);
            }
            GUI.enabled = true;
        }

        /// <summary>Create-a-class view: the chosen gun with its attachments and skin, turning in front of a private camera.</summary>
        private static void DrawPreview(float x, float y, float w)
        {
            S.Box(new Rect(x, y, w, PreviewH), S.Panel);
            GUI.DrawTexture(new Rect(x, y, 6, PreviewH), S.GoldDim);
            if (_guns.Count == 0)
            {
                ClassPreview.Hide();
                GUI.color = new Color(1f, 1f, 1f, 0.6f);
                GUI.Label(new Rect(x, y, w, PreviewH), "PICK A GUN BELOW TO SEE IT HERE", S.BodyCenter);
                GUI.color = Color.white;
                return;
            }
            _previewIndex = Mathf.Clamp(_previewIndex, 0, _guns.Count - 1);
            var g = _guns[_previewIndex];
            var o = LoadoutService.Options(g.ItemId);
            ClassPreview.Show(g);
            var tex = ClassPreview.Texture;
            var view = new Rect(x + 12, y + 44, w - 24, PreviewH - 84);
            if (tex) GUI.DrawTexture(view, tex, ScaleMode.ScaleToFit);
            else GUI.Label(view, ClassPreview.Error, S.BodyCenter);
            GUI.Label(new Rect(x + 16, y + 8, w - 32, 30), $"{o.Name.ToUpperInvariant()}   |   {WeaponSkins.Names[Mathf.Clamp(g.Skin, 0, WeaponSkins.Count - 1)].ToUpperInvariant()}", S.H2);
            if (_guns.Count > 1)
            {
                bool was = GUI.enabled; GUI.enabled = true;
                if (S.Btn(new Rect(x + w - 108, y + 6, 42, 34), "<", S.ToggleButton)) _previewIndex = (_previewIndex + _guns.Count - 1) % _guns.Count;
                if (S.Btn(new Rect(x + w - 58, y + 6, 42, 34), ">", S.ToggleButton)) _previewIndex = (_previewIndex + 1) % _guns.Count;
                GUI.enabled = was;
            }
            string mods = string.Join("  |  ", new[] {
                g.Sight > 0 && g.Sight < o.Sights.Count ? o.Sights[g.Sight] : null,
                g.Barrel > 0 && g.Barrel < o.Barrels.Count ? o.Barrels[g.Barrel] : null,
                g.ExtendedMag ? "Extended mag" : null, g.Drum ? "Drum mag" : null, g.Switch ? "Switch (full auto)" : null, g.Laser ? "Laser" : null
            }.Where(m => m != null));
            int mag = o.AmmoPerMag > 0 ? Mathf.RoundToInt(o.AmmoPerMag * (g.Drum ? ModAttachments.DrumMultiplier : 1f)) : 0;
            GUI.Label(new Rect(x + 16, y + PreviewH - 36, w - 32, 28), (mag > 0 ? $"{mag} rounds   " : "") + (mods.Length > 0 ? mods : "stock"), S.Small);
        }

        /// <summary>Attachment rows for one chosen gun; returns the y after the block.</summary>
        private static float DrawAttachments(float x, float y, float w, int gunIndex)
        {
            var g = _guns[gunIndex];
            var o = LoadoutService.Options(g.ItemId);
            S.Box(new Rect(x, y, w, AttachH), S.Panel);
            GUI.DrawTexture(new Rect(x, y, 6, AttachH), gunIndex == _previewIndex ? S.Gold : S.GoldDim);
            GUI.Label(new Rect(x + 16, y + 8, w - 32, 30), $"{o.Name.ToUpperInvariant()}  ATTACHMENTS & SKIN", S.H2);
            if (Event.current.type == EventType.MouseDown && new Rect(x, y, w, AttachH).Contains(Event.current.mousePosition)) _previewIndex = gunIndex;
            bool changed = false;
            float ry = y + 44;
            changed |= Cycle(x + 16, ry, w - 32, "Sight", o.Sights, ref g.Sight); ry += 42;
            changed |= Cycle(x + 16, ry, w - 32, "Barrel", o.Barrels, ref g.Barrel); ry += 42;
            changed |= Cycle(x + 16, ry, w - 32, "Bullets", o.Bullets, ref g.Bullets); ry += 42;
            changed |= Cycle(x + 16, ry, w - 32, "Skin", _skinNames, ref g.Skin); ry += 42;
            float half = (w - 40) / 2f;
            GUI.enabled = GUI.enabled && o.HasExtendedMag;
            if (S.Btn(new Rect(x + 16, ry, half, 38), o.HasExtendedMag ? (g.ExtendedMag ? "[x] Extended mag" : "[ ] Extended mag") : "No extended mag", g.ExtendedMag ? S.ToggleButtonOn : S.ToggleButton))
            { g.ExtendedMag = !g.ExtendedMag; changed = true; }
            GUI.enabled = ModState.Phase == MatchPhase.Lobby && o.HasLaser;
            if (S.Btn(new Rect(x + 24 + half, ry, half, 38), o.HasLaser ? (g.Laser ? "[x] Laser sight" : "[ ] Laser sight") : "No laser", g.Laser ? S.ToggleButtonOn : S.ToggleButton))
            { g.Laser = !g.Laser; changed = true; }
            ry += 42;
            GUI.enabled = ModState.Phase == MatchPhase.Lobby && o.HasDrum;
            if (S.Btn(new Rect(x + 16, ry, half, 38), o.HasDrum ? (g.Drum ? "[x] Drum mag" : "[ ] Drum mag") : "No drum mag", g.Drum ? S.ToggleButtonOn : S.ToggleButton))
            { g.Drum = !g.Drum; if (g.Drum) g.ExtendedMag = false; changed = true; }
            GUI.enabled = ModState.Phase == MatchPhase.Lobby && o.HasSwitch;
            if (S.Btn(new Rect(x + 24 + half, ry, half, 38), o.HasSwitch ? (g.Switch ? "[x] The Switch (full auto)" : "[ ] The Switch (full auto)") : "No switch", g.Switch ? S.ToggleButtonOn : S.ToggleButton))
            { g.Switch = !g.Switch; changed = true; }
            GUI.enabled = ModState.Phase == MatchPhase.Lobby;
            if (!WeaponSkins.CanPick(g.Skin)) { g.Skin = 0; changed = true; }   // locked skins are skipped
            if (changed) { _guns[gunIndex] = g; _previewIndex = gunIndex; SendLoadout(false); }
            return y + AttachH;
        }

        private static readonly List<string> _charmNames = new List<string> { "None", "Rank emblem", RankCharms.CanUseDev ? "DEV tag" : "DEV tag (locked)" };
        private static readonly List<string> _skinNames = WeaponSkins.Names.Select((n, i) => i == WeaponSkins.Dragon && !WeaponSkins.CanPick((byte)i) ? n + " (locked)" : n).ToList();

        private static bool Cycle(float x, float y, float w, string label, List<string> options, ref byte index)
        {
            bool changed = false;
            int n = Mathf.Max(1, options.Count);
            index = (byte)Mathf.Clamp(index, 0, n - 1);
            GUI.Label(new Rect(x, y, 90, 38), label, S.Small);
            GUI.enabled = GUI.enabled && n > 1;
            if (S.Btn(new Rect(x + 96, y, 42, 38), "<", S.ToggleButton)) { index = (byte)((index + n - 1) % n); changed = true; }
            GUI.Label(new Rect(x + 144, y, w - 240, 38), options[index], S.BodyCenter);
            if (S.Btn(new Rect(x + w - 42, y, 42, 38), ">", S.ToggleButton)) { index = (byte)((index + 1) % n); changed = true; }
            GUI.enabled = ModState.Phase == MatchPhase.Lobby;
            return changed;
        }

        private static void DrawFooter(bool host, bool inLobby, MatchStateBroadcast s)
        {
            S.Box(new Rect(0, S.DesignH - 130, S.DesignW, 130), S.Panel);
            var mode = (MatchMode)s.Mode;
            if (host)
            {
                GUI.enabled = inLobby;
                float x = 40;
                GUI.Label(new Rect(x, S.DesignH - 118, 100, 40), "MODE", S.Small);
                foreach (var m in MatchModes.All)
                {
                    bool on = m == mode;
                    if (S.Btn(new Rect(x, S.DesignH - 84, 150, 54), MatchModes.Name(m), on ? S.ToggleButtonOn : S.ToggleButton) && !on) Plugin.Host.SetMode(m);
                    x += 158;
                }
                x += 30;
                string map = ArenaLayout.MapNames[((s.MapIndex % ArenaLayout.MapCount) + ArenaLayout.MapCount) % ArenaLayout.MapCount];
                GUI.Label(new Rect(x, S.DesignH - 118, 300, 40), "MAP", S.Small);
                if (S.Btn(new Rect(x, S.DesignH - 84, 54, 54), "<", S.ToggleButton)) Plugin.Host.SetMap(s.MapIndex - 1);
                GUI.Label(new Rect(x + 60, S.DesignH - 84, 200, 54), map.ToUpperInvariant(), S.H1Center);
                if (S.Btn(new Rect(x + 266, S.DesignH - 84, 54, 54), ">", S.ToggleButton)) Plugin.Host.SetMap(s.MapIndex + 1);
                GUI.enabled = true;

                string why = "";
                bool canStart = false;
                if (!inLobby) why = "Match in progress";
                else if (Plugin.Host.Machine != null) canStart = Plugin.Host.Machine.CanStart(out why);
                GUI.Label(new Rect(S.DesignW - 760, S.DesignH - 118, 720, 30), canStart ? "Everyone is ready." : why, S.SmallRight);
                GUI.enabled = canStart;
                if (S.Btn(new Rect(S.DesignW - 420, S.DesignH - 84, 380, 60), "START MATCH", S.BigButton)) Plugin.Host.Start();
                GUI.enabled = true;
                // Alone in the lobby: allow a solo run to try maps, guns and the killcam (F8 replays your own last seconds).
                var mach = Plugin.Host.Machine;
                if (inLobby && mach != null && mach.State.PresentCount == 1 && !mach.Rules.SoloDebug
                    && S.Btn(new Rect(S.DesignW - 760, S.DesignH - 84, 320, 60), "SOLO TEST (no friends)", S.Button))
                {
                    mach.Rules.SoloDebug = true;
                    mach.Dirty = true;
                }
            }
            else
            {
                GUI.Label(new Rect(40, S.DesignH - 100, 1200, 40), inLobby ? (s.StatusText ?? "Waiting for the host to start the match.") : "Match in progress.", S.Body);
            }
        }

        private static void Invite()
        {
            try
            {
                if (SteamManager.CurrentLobbyID != CSteamID.Nil) SteamFriends.ActivateGameOverlayInviteDialog(SteamManager.CurrentLobbyID);
                else _hint = "No Steam lobby to invite to";
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Invite failed: " + e.Message);
            }
        }

        private static void SendLoadout(bool ready)
        {
            _ready = ready;
            var bytes = LoadoutCodec.Encode(_guns);
            byte charm = (byte)Mathf.Clamp(Plugin.Cfg.Charm.Value, 0, 2);
            if (charm == 2 && !RankCharms.CanUseDev) charm = 1;
            if (ModNet.IsHost) Plugin.Host.SetLocalLoadout(bytes, ready, RankService.Points, charm);
            else ModNet.SendLoadout(bytes, ready, RankService.Points, charm);
        }

        /// <summary>Re-sends the current loadout so the host learns updated rank points.</summary>
        public static void ResendLoadout() => SendLoadout(_ready);
    }
}
