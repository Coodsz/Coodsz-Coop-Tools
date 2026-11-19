#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace BurningEmpiresCoopCompat
{
    // MapBounds' plain rules, without game types, so Coodsz Host's tests can run them.
    internal static class MapBoundsRules
    {
        // Room left past the outermost settlements, in map units.
        public const float Margin = 50f;

        public struct Bounds
        {
            public float MinX, MinY, MaxX, MaxY;

            public Bounds(float minX, float minY, float maxX, float maxY)
            {
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
            }

            public float Diagonal
            {
                get
                {
                    var x = MaxX - MinX;
                    var y = MaxY - MinY;
                    return (float)Math.Sqrt(x * x + y * y);
                }
            }

            public override string ToString() =>
                "(" + Text(MinX) + "," + Text(MinY) + ")-(" + Text(MaxX) + "," + Text(MaxY) + ")";
        }

        public static string Text(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

        // The borders widened to hold every settlement plus the margin, never past 0. False and
        // the old borders when every settlement already sits inside them (Calradia on the server).
        public static bool TryWiden(Bounds old, IEnumerable<KeyValuePair<float, float>> positions, float margin, out Bounds widened)
        {
            widened = old;
            if (positions == null)
                return false;
            var outside = false;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var position in positions)
            {
                var x = position.Key;
                var y = position.Value;
                if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y))
                    continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                if (x < old.MinX || y < old.MinY || x > old.MaxX || y > old.MaxY)
                    outside = true;
            }
            if (!outside)
                return false;
            widened = new Bounds(
                Math.Max(0f, Math.Min(old.MinX, minX - margin)),
                Math.Max(0f, Math.Min(old.MinY, minY - margin)),
                Math.Max(old.MaxX, maxX + margin),
                Math.Max(old.MaxY, maxY + margin));
            return true;
        }

        // Campaign.LoadMapScene's path finding costs for a map diagonal.
        public static void PathCosts(float diagonal, int regionSwitchCostFromLandToSea, out int playerRegionSwitchCost, out int pathFindingMaxCostLimit)
        {
            playerRegionSwitchCost = (int)(diagonal * regionSwitchCostFromLandToSea * 0.2f);
            pathFindingMaxCostLimit = Math.Max(playerRegionSwitchCost * 100, (int)(diagonal * 500f));
        }

        // The weather grid splits the terrain size into cells and indexes a fixed size array with
        // a position's cell, so the size must reach past every settlement or the index overflows.
        public static float CoverTerrain(float size, float maxBorder) => Math.Max(size, maxBorder);
    }
}
