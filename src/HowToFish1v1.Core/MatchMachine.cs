using System;
using System.Collections.Generic;
using System.Linq;

namespace HowToFish1v1.Core
{
    /// <summary>
    /// Host-side state machine for every mode. Pure C#: time is passed in as seconds.
    /// Every mutating call sets Dirty=true; the caller broadcasts the state and clears Dirty/Effects.
    /// </summary>
    public sealed class MatchMachine
    {
        public MatchState State { get; } = new MatchState();
        public MatchRules Rules { get; }
        public List<Effect> Effects { get; } = new List<Effect>();
        public bool Dirty;

        public MatchMachine(MatchRules rules)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        // ------------------------------------------------------------------ lobby

        public void Open()
        {
            if (State.Phase != MatchPhase.Inactive) return;
            State.Phase = MatchPhase.Lobby;
            State.StatusText = "Waiting for players";
            Dirty = true;
        }

        public void SetMode(MatchMode mode)
        {
            if (State.Phase != MatchPhase.Lobby) return;
            bool wasSolo = MatchModes.IsSolo(State.Mode);
            State.Mode = mode;
            RebalanceTeams();
            // Trickshot has its own map; leaving it goes back to the first arena.
            if (MatchModes.IsSolo(mode)) State.MapIndex = ArenaLayout.TrickshotIndex;
            else if (wasSolo && State.MapIndex == ArenaLayout.TrickshotIndex) State.MapIndex = 0;
            Dirty = true;
        }

        /// <summary>Host picks the map; only allowed in the lobby. Wraps into the valid range.</summary>
        public void SetMap(int mapIndex)
        {
            if (State.Phase != MatchPhase.Lobby) return;
            int n = ArenaLayout.MapCount;
            State.MapIndex = ((mapIndex % n) + n) % n;
            Dirty = true;
        }

        public void PlayerJoined(int id, string name)
        {
            if (State.Phase == MatchPhase.Inactive) return;
            if (State.Slot(id) != null) return;
            if (State.Players.Count >= MatchState.MaxPlayers) return;
            var slot = new PlayerSlot { Id = id, Name = name ?? "" };
            slot.Team = State.TeamCount(0) <= State.TeamCount(1) ? 0 : 1;
            State.Players.Add(slot);
            Dirty = true;
        }

        public void PlayerSaidHello(int id, bool hasMod)
        {
            var slot = State.Slot(id);
            if (slot == null) return;
            slot.HasMod = hasMod;
            Dirty = true;
        }

        public void SetLoadout(int id, byte[] itemIds, bool ready, int rankPoints = -1, int charm = -1)
        {
            var slot = State.Slot(id);
            if (slot == null) return;
            slot.Loadout = LoadoutCodec.Truncate(itemIds ?? Array.Empty<byte>(), Rules.MaxLoadoutGuns);
            slot.Ready = ready;
            if (rankPoints >= 0) slot.RankPoints = rankPoints;
            if (charm >= 0) slot.Charm = (byte)charm;
            Dirty = true;
        }

        /// <summary>Host moves a player to the other team. Lobby only, team modes only.</summary>
        public void MoveTeam(int id)
        {
            if (State.Phase != MatchPhase.Lobby || State.IsFfa) return;
            var slot = State.Slot(id);
            if (slot == null) return;
            slot.Team = 1 - slot.Team;
            Dirty = true;
        }

        private void RebalanceTeams()
        {
            for (int i = 0; i < State.Players.Count; i++) State.Players[i].Team = i % 2;
        }

        public bool CanStart(out string reason)
        {
            reason = "";
            if (State.Phase != MatchPhase.Lobby) { reason = "Not in lobby"; return false; }
            int count = State.PresentCount;
            int min = Rules.SoloDebug ? 1 : MatchModes.MinPlayers(State.Mode);
            int max = MatchModes.MaxPlayers(State.Mode);
            if (count < min) { reason = $"{MatchModes.Name(State.Mode)} needs {min} players ({count} here)"; return false; }
            if (count > max) { reason = $"{MatchModes.Name(State.Mode)} allows at most {max} players ({count} here)"; return false; }
            if (!State.IsFfa && !Rules.SoloDebug && !MatchModes.IsSolo(State.Mode))
            {
                int cap = MatchModes.TeamSize(State.Mode);
                if (State.TeamCount(0) == 0 || State.TeamCount(1) == 0) { reason = "Both teams need a player"; return false; }
                if (State.TeamCount(0) > cap || State.TeamCount(1) > cap) { reason = $"Teams hold at most {cap} in {MatchModes.Name(State.Mode)}"; return false; }
            }
            foreach (var s in State.Players)
                if (!s.HasMod) { reason = s.Name + " does not have the mod"; return false; }
            foreach (var s in State.Players)
                if (!s.Ready) { reason = s.Name + " is not ready"; return false; }
            return true;
        }

        // ------------------------------------------------------------------ match flow

        public void Start(double now)
        {
            if (!CanStart(out string reason))
            {
                State.StatusText = reason;
                Dirty = true;
                return;
            }
            if (!State.ArenaBuilt || State.BuiltMapIndex != State.MapIndex)
            {
                Effects.Add(new Effect(EffectKind.BuildArena));
                State.ArenaBuilt = true;
                State.BuiltMapIndex = State.MapIndex;
            }
            State.MatchNumber++;
            State.Round = 1;
            State.TeamScore[0] = 0;
            State.TeamScore[1] = 0;
            foreach (var p in State.Players) { p.Kills = 0; p.Deaths = 0; }
            State.TeamAIsLeft = true;
            State.MatchWinnerTeam = -1;
            State.MatchWinnerId = -1;
            State.LastRoundWinnerTeam = -1;
            BeginRound(now);
        }

        private void BeginRound(double now)
        {
            foreach (var p in State.Players) p.DeadThisRound = false;
            Effects.Add(new Effect(EffectKind.ResetPlayers));
            State.Phase = MatchPhase.Countdown;
            State.PhaseEndsAt = now + Rules.CountdownSeconds;
            State.StatusText = MatchModes.IsSolo(State.Mode) ? "Jump off and hit a bot mid-air" : State.IsFfa ? $"First to {Rules.KillsToWin} kills" : "Round " + State.Round;
            Dirty = true;
        }

        public void Tick(double now)
        {
            switch (State.Phase)
            {
                case MatchPhase.Countdown:
                    if (now >= State.PhaseEndsAt)
                    {
                        State.Phase = MatchPhase.Live;
                        Dirty = true;
                    }
                    break;
                case MatchPhase.RoundEnd:
                    if (now >= State.PhaseEndsAt)
                    {
                        int w = State.LastRoundWinnerTeam;
                        if (w >= 0 && State.TeamScore[w] >= Rules.RoundsToWin)
                        {
                            State.Phase = MatchPhase.MatchEnd;
                            State.MatchWinnerTeam = w;
                            State.PhaseEndsAt = now + Rules.MatchEndSeconds;
                            State.StatusText = TeamLabel(w) + " wins the match";
                            Dirty = true;
                        }
                        else
                        {
                            State.TeamAIsLeft = !State.TeamAIsLeft;
                            State.Round++;
                            BeginRound(now);
                        }
                    }
                    break;
                case MatchPhase.MatchEnd:
                    if (now >= State.PhaseEndsAt) ReturnToLobby("Rematch? Ready up");
                    break;
            }
        }

        /// <summary>
        /// A death during Live or Countdown. Team modes: when a whole team is dead the other team takes the round.
        /// Free-for-all: the killer gains a kill (suicides count nothing), the victim respawns after a delay.
        /// </summary>
        /// <summary>Returns true when the death was accepted (so the host can announce it).</summary>
        public bool Kill(int victimId, int killerId, double now)
        {
            if (State.Phase != MatchPhase.Live && State.Phase != MatchPhase.Countdown) return false;
            var victim = State.Slot(victimId);
            if (victim == null || victim.DeadThisRound) return false;
            victim.DeadThisRound = true;
            victim.Deaths++;
            Dirty = true;

            var killer = State.Slot(killerId);
            bool credited = killer != null && killerId != victimId && (State.IsFfa || killer.Team != victim.Team);
            if (credited) killer.Kills++;

            if (MatchModes.RespawnsInPlace(State.Mode))
            {
                if (credited && State.IsFfa)
                {
                    State.StatusText = killer.Name + " killed " + victim.Name;
                    if (killer.Kills >= Rules.KillsToWin)
                    {
                        State.Phase = MatchPhase.MatchEnd;
                        State.MatchWinnerId = killer.Id;
                        State.PhaseEndsAt = now + Rules.MatchEndSeconds;
                        State.StatusText = killer.Name + " wins the match";
                        return true;
                    }
                }
                else
                {
                    State.StatusText = victim.Name + " died";
                }
                Effects.Add(new Effect(EffectKind.RespawnPlayer, victimId));
                return true;
            }

            int team = victim.Team;
            bool teamWiped = State.TeamMembers(team).All(p => p.DeadThisRound);
            if (!teamWiped) return true;
            int winner = State.TeamCount(1 - team) > 0 ? 1 - team : -1;
            if (winner >= 0)
            {
                State.TeamScore[winner]++;
                State.LastRoundWinnerTeam = winner;
                State.StatusText = TeamLabel(winner) + " wins the round";
                if (State.TeamScore[winner] >= Rules.RoundsToWin)
                {
                    // The deciding kill ends the match right away so the final killcam plays while it is still fresh.
                    State.Phase = MatchPhase.MatchEnd;
                    State.MatchWinnerTeam = winner;
                    State.PhaseEndsAt = now + Rules.MatchEndSeconds;
                    State.StatusText = TeamLabel(winner) + " wins the match";
                    return true;
                }
            }
            else
            {
                State.LastRoundWinnerTeam = -1;
                State.StatusText = "Round over";
            }
            State.Phase = MatchPhase.RoundEnd;
            State.PhaseEndsAt = now + Rules.RoundEndSeconds;
            return true;
        }

        /// <summary>Trickshot: the player landed a mid-air hit; the match ends and the final killcam replays it.</summary>
        public void EndTrickshot(int playerId, double now, int attempts)
        {
            if (State.Phase != MatchPhase.Live || !MatchModes.IsSolo(State.Mode)) return;
            var slot = State.Slot(playerId);
            if (slot != null) slot.Kills++;
            State.Phase = MatchPhase.MatchEnd;
            State.MatchWinnerId = playerId;
            State.PhaseEndsAt = now + Rules.MatchEndSeconds;
            State.StatusText = attempts <= 1 ? "TRICKSHOT HIT   first try!" : $"TRICKSHOT HIT   attempt {attempts}";
            Dirty = true;
        }

        /// <summary>Free-for-all: the host reports that the respawn effect was carried out.</summary>
        public void PlayerRespawned(int id)
        {
            var slot = State.Slot(id);
            if (slot == null) return;
            slot.DeadThisRound = false;
            Dirty = true;
        }

        public void PlayerLeft(int id)
        {
            var slot = State.Slot(id);
            if (slot == null) return;
            State.Players.Remove(slot);
            Dirty = true;
            if (!State.IsRoundPhase) return;
            if (State.IsFfa && State.PresentCount >= 2)
            {
                State.StatusText = slot.Name + " left";
                return;
            }
            ReturnToLobby(slot.Name + " left the match");
        }

        public void Quit()
        {
            if (State.Phase == MatchPhase.Inactive) return;
            if (State.ArenaBuilt) Effects.Add(new Effect(EffectKind.DestroyArena));
            State.ArenaBuilt = false;
            State.BuiltMapIndex = -1;
            State.Phase = MatchPhase.Inactive;
            State.Round = 0;
            State.Players.Clear();
            State.TeamScore[0] = 0; State.TeamScore[1] = 0;
            State.TeamAIsLeft = true;
            State.LastRoundWinnerTeam = -1;
            State.MatchWinnerTeam = -1;
            State.MatchWinnerId = -1;
            State.StatusText = "";
            Dirty = true;
        }

        private void ReturnToLobby(string status)
        {
            State.Phase = MatchPhase.Lobby;
            State.Round = 0;
            State.TeamScore[0] = 0; State.TeamScore[1] = 0;
            foreach (var p in State.Players) { p.Ready = false; p.DeadThisRound = false; p.Kills = 0; p.Deaths = 0; }
            State.LastRoundWinnerTeam = -1;
            State.StatusText = status;
            Dirty = true;
        }

        /// <summary>"Gavin" in 1v1, "Team A" / "Team B" otherwise.</summary>
        public string TeamLabel(int team)
        {
            if (team < 0) return "Nobody";
            var members = State.TeamMembers(team).ToList();
            if (members.Count == 1) return members[0].Name;
            return team == 0 ? "Team A" : "Team B";
        }
    }
}
