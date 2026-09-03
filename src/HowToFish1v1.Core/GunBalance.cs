namespace HowToFish1v1.Core
{
    /// <summary>
    /// Fixed per-gun damage for ranked play. The host applies these to every hit, so client settings, bullet upgrades and
    /// edited configs cannot change them. Players have 100 health: the sniper is a one-shot, the pistol a four-shot, the
    /// assault rifle a five-shot, the SMG a seven-shot (with the fastest fire), the shotgun four pellets.
    /// </summary>
    public static class GunBalance
    {
        public const int Health = 100;
        public const int KnifeDamage = 150;
        public const float KnifeReach = 3.2f;
        public const float RicochetScale = 0.75f;

        public static int DamageFor(string gunName)
        {
            string n = (gunName ?? "").ToLowerInvariant();
            if (n.Contains("snip")) return 100;
            if (n.Contains("assault") || n.Contains("rifle")) return 24;
            if (n.Contains("smg")) return 16;
            if (n.Contains("pistol")) return 30;
            if (n.Contains("shotgun")) return 25;
            return 25;
        }

        public static int RicochetDamageFor(string gunName) => System.Math.Max(1, (int)System.Math.Round(DamageFor(gunName) * RicochetScale));

        /// <summary>
        /// What the host lets through for a reported hit: a knife within reach keeps its damage, a ricochet (three quarters
        /// of the gun's damage) keeps its damage, everything else becomes the gun's fixed damage.
        /// </summary>
        public static int Authoritative(string gunName, int reported, float distance)
        {
            int target = DamageFor(gunName);
            if (reported >= KnifeDamage && distance <= KnifeReach) return KnifeDamage;
            int ric = RicochetDamageFor(gunName);
            if (System.Math.Abs(reported - ric) <= 1 && ric != target) return ric;
            return target;
        }
    }
}
