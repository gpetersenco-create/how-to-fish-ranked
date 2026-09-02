using System;
using System.Collections.Generic;

namespace HowToFish1v1.Core
{
    /// <summary>
    /// Host-side 1v1 state machine. Pure C#: time is passed in as seconds.
    /// Every mutating call sets Dirty=true; the caller broadcasts the state and clears Dirty/Effects.
    /// </summary>
    public sealed class MatchMachine
    {
        public MatchState State { get; } = new MatchState();
        public MatchRules Rules { get; }
        public List<EffectKind> Effects { get; } = new List<EffectKind>();
        public bool Dirty;

        public MatchMachine(MatchRules rules)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        public void Open()
        {
            if (State.Phase != MatchPhase.Inactive) return;
            State.Phase = MatchPhase.Lobby;
            State.StatusText = "Waiting for two players";
            Dirty = true;
        }

        public void PlayerJoined(int id, string name)
        {
            if (State.Phase == MatchPhase.Inactive) return;
            if (State.Slot(id) != null) return;
            PlayerSlot slot = !State.A.IsPresent ? State.A : (!State.B.IsPresent ? State.B : null);
            if (slot == null) return;
            slot.Clear();
            slot.Id = id;
            slot.Name = name ?? "";
            Dirty = true;
        }

        public void PlayerSaidHello(int id, bool hasMod)
        {
            var slot = State.Slot(id);
            if (slot == null) return;
            slot.HasMod = hasMod;
            Dirty = true;
        }

        public void SetLoadout(int id, byte[] itemIds, bool ready)
        {
            var slot = State.Slot(id);
            if (slot == null) return;
            itemIds = itemIds ?? Array.Empty<byte>();
            int n = Math.Min(itemIds.Length, Rules.MaxLoadoutGuns);
            var copy = new byte[n];
            Array.Copy(itemIds, copy, n);
            slot.Loadout = copy;
            slot.Ready = ready;
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

        public bool CanStart(out string reason)
        {
            reason = "";
            if (State.Phase != MatchPhase.Lobby) { reason = "Not in lobby"; return false; }
            int need = Rules.SoloDebug ? 1 : 2;
            if (State.PresentCount < need) { reason = "Need two players in the game"; return false; }
            foreach (var s in new[] { State.A, State.B })
            {
                if (!s.IsPresent) continue;
                if (!s.HasMod) { reason = s.Name + " does not have the mod"; return false; }
            }
            foreach (var s in new[] { State.A, State.B })
            {
                if (!s.IsPresent) continue;
                if (!s.Ready) { reason = s.Name + " is not ready"; return false; }
            }
            return true;
        }

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
                Effects.Add(EffectKind.BuildArena);
                State.ArenaBuilt = true;
                State.BuiltMapIndex = State.MapIndex;
            }
            State.Round = 1;
            State.A.Score = 0;
            State.B.Score = 0;
            State.AIsLeft = true;
            State.MatchWinnerId = -1;
            State.LastRoundWinnerId = -1;
            BeginRound(now);
        }

        private void BeginRound(double now)
        {
            State.A.DeadThisRound = false;
            State.B.DeadThisRound = false;
            Effects.Add(EffectKind.ResetPlayers);
            State.Phase = MatchPhase.Countdown;
            State.PhaseEndsAt = now + Rules.CountdownSeconds;
            State.StatusText = "Round " + State.Round;
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
                        var winner = State.Slot(State.LastRoundWinnerId);
                        if (winner != null && winner.Score >= Rules.RoundsToWin)
                        {
                            State.Phase = MatchPhase.MatchEnd;
                            State.MatchWinnerId = winner.Id;
                            State.PhaseEndsAt = now + Rules.MatchEndSeconds;
                            State.StatusText = winner.Name + " wins the match";
                            Dirty = true;
                        }
                        else
                        {
                            State.AIsLeft = !State.AIsLeft;
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

        /// <summary>A death during Live or Countdown ends the round for the victim, so nobody is left dead when the next round starts.</summary>
        public void Kill(int victimId, double now)
        {
            if (State.Phase != MatchPhase.Live && State.Phase != MatchPhase.Countdown) return;
            var victim = State.Slot(victimId);
            if (victim == null || victim.DeadThisRound) return;
            victim.DeadThisRound = true;
            var winner = State.Other(victimId);
            if (winner != null)
            {
                winner.Score++;
                State.LastRoundWinnerId = winner.Id;
                State.StatusText = winner.Name + " wins the round";
            }
            else
            {
                State.LastRoundWinnerId = -1;
                State.StatusText = "Round over";
            }
            State.Phase = MatchPhase.RoundEnd;
            State.PhaseEndsAt = now + Rules.RoundEndSeconds;
            Dirty = true;
        }

        public void PlayerLeft(int id)
        {
            var slot = State.Slot(id);
            if (slot == null) return;
            string name = slot.Name;
            slot.Clear();
            if (State.IsRoundPhase) ReturnToLobby(name + " left the match");
            Dirty = true;
        }

        public void Quit()
        {
            if (State.Phase == MatchPhase.Inactive) return;
            if (State.ArenaBuilt) Effects.Add(EffectKind.DestroyArena);
            State.ArenaBuilt = false;
            State.BuiltMapIndex = -1;
            State.Phase = MatchPhase.Inactive;
            State.Round = 0;
            State.A.Clear();
            State.B.Clear();
            State.AIsLeft = true;
            State.LastRoundWinnerId = -1;
            State.MatchWinnerId = -1;
            State.StatusText = "";
            Dirty = true;
        }

        private void ReturnToLobby(string status)
        {
            State.Phase = MatchPhase.Lobby;
            State.Round = 0;
            State.A.Score = 0; State.B.Score = 0;
            State.A.Ready = false; State.B.Ready = false;
            State.A.DeadThisRound = false; State.B.DeadThisRound = false;
            State.LastRoundWinnerId = -1;
            State.StatusText = status;
            Dirty = true;
        }
    }
}
