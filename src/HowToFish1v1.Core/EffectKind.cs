namespace HowToFish1v1.Core
{
    /// <summary>Side effects the host must perform after a machine transition, in list order.</summary>
    public enum EffectKind { BuildArena, DestroyArena, ResetPlayers, RespawnPlayer }

    public struct Effect
    {
        public EffectKind Kind;
        /// <summary>Player the effect targets (RespawnPlayer); -1 otherwise.</summary>
        public int PlayerId;

        public Effect(EffectKind kind, int playerId = -1) { Kind = kind; PlayerId = playerId; }
        public override string ToString() => PlayerId == -1 ? Kind.ToString() : $"{Kind}({PlayerId})";
    }
}
