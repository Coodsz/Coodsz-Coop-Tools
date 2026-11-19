#nullable disable

namespace BurningEmpiresCoopCompat
{
    // PathQueryGuard's plain rules, without game types, so Coodsz Host's tests can run them.
    internal static class PathQueryRules
    {
        public enum Unsafe
        {
            None,
            NoSettlement,
            NoComponent,
            NoFace,
        }

        // The answer for a pair the map cannot path: as far apart as the game's own answer for a
        // missing settlement, all over land.
        public const float SkippedDistance = float.MaxValue;
        public const float SkippedLandRatio = 1f;

        // Why one end of a settlement to settlement path query would reach the engine with a face
        // index of -1, which ends the dedicated server with an access violation. None when safe.
        public static Unsafe Check(bool hasSettlement, bool hasComponent, int faceIndex)
        {
            if (!hasSettlement)
                return Unsafe.NoSettlement;
            if (!hasComponent)
                return Unsafe.NoComponent;
            return faceIndex < 0 ? Unsafe.NoFace : Unsafe.None;
        }

        // The first end that is not safe decides the pair's kind.
        public static Unsafe CheckPair(Unsafe first, Unsafe second) => first != Unsafe.None ? first : second;
    }
}
