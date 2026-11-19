using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace BurningEmpiresCoopCompat
{
    // Map borders on the dedicated server.
    // The server's own map scene never reads the map's border entities: it sets Calradia's
    // borders, (62,30)-(790,640), and a terrain size of 848. Burning Empires' settlements reach
    // far past both, so the campaign's map diagonal and path finding cost limit come out too
    // small, NavigationHelper clamps searches around a far settlement to an empty box, and the
    // weather grid indexes past its array for a position beyond the terrain size.
    // Campaign.LoadMapScene reads the borders right after the map scene loads, by which time
    // every settlement is known (new campaign and loaded save alike). On the server only, after
    // it, the borders are widened to hold every settlement plus a margin (MapBoundsRules), on
    // the map scene and in the campaign's copies, the costs are worked out again the same way,
    // and the terrain size is raised to reach the new borders. Calradia's settlements all sit
    // inside its borders, so a Calradia campaign keeps them.
    internal static class MapBounds
    {
        private static readonly string[] SceneFields = { "_minimumPositionCache", "_maximumPositionCache", "_terrainSize" };
        private static readonly string[] CampaignProperties =
        {
            nameof(Campaign.MapMinimumPosition), nameof(Campaign.MapMaximumPosition), nameof(Campaign.MapDiagonal), nameof(Campaign.MapDiagonalSquared),
        };

        public static void Install(Harmony harmony)
        {
            if (!FreshCampaign.IsDedicatedServerProcess())
                return;
            try
            {
                var target = AccessTools.Method(typeof(Campaign), "LoadMapScene", Type.EmptyTypes);
                if (target == null)
                {
                    SubModule.Log("Map borders: Campaign.LoadMapScene not found; the server keeps its map scene's borders.");
                    return;
                }
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(MapBounds), nameof(LoadMapScenePostfix)));
            }
            catch (Exception ex)
            {
                SubModule.Log("Map borders could not hook Campaign.LoadMapScene: " + ex.Message);
            }
        }

        private static void LoadMapScenePostfix(Campaign __instance)
        {
            if (!FreshCampaign.FreshStartActive && !Coop.OnServer)
                return;
            try
            {
                var scene = __instance?.MapSceneWrapper;
                if (scene == null)
                    return;
                var minField = AccessTools.Field(scene.GetType(), SceneFields[0]);
                var maxField = AccessTools.Field(scene.GetType(), SceneFields[1]);
                var sizeField = AccessTools.Field(scene.GetType(), SceneFields[2]);
                if (minField == null || maxField == null || sizeField == null)
                {
                    SubModule.Log("Map borders: map scene " + scene.GetType().FullName + " has no border fields; borders kept.");
                    return;
                }
                foreach (var property in CampaignProperties)
                {
                    if (AccessTools.PropertySetter(typeof(Campaign), property) == null)
                    {
                        SubModule.Log("Map borders: Campaign." + property + " cannot be set; borders kept.");
                        return;
                    }
                }

                var min = Campaign.MapMinimumPosition;
                var max = Campaign.MapMaximumPosition;
                var old = new MapBoundsRules.Bounds(min.x, min.y, max.x, max.y);
                if (!MapBoundsRules.TryWiden(old, SettlementPositions(), MapBoundsRules.Margin, out var widened))
                {
                    SubModule.Log("Map borders: every settlement is inside " + old + "; borders kept.");
                    return;
                }

                var newMin = new Vec2(widened.MinX, widened.MinY);
                var newMax = new Vec2(widened.MaxX, widened.MaxY);
                minField.SetValue(scene, newMin);
                maxField.SetValue(scene, newMax);
                var oldSize = (Vec2)sizeField.GetValue(scene);
                var newSize = new Vec2(MapBoundsRules.CoverTerrain(oldSize.x, widened.MaxX), MapBoundsRules.CoverTerrain(oldSize.y, widened.MaxY));
                sizeField.SetValue(scene, newSize);

                var oldLimit = Campaign.PathFindingMaxCostLimit;
                var diagonal = widened.Diagonal;
                SetCampaign(nameof(Campaign.MapMinimumPosition), newMin);
                SetCampaign(nameof(Campaign.MapMaximumPosition), newMax);
                SetCampaign(nameof(Campaign.MapDiagonal), diagonal);
                SetCampaign(nameof(Campaign.MapDiagonalSquared), diagonal * diagonal);
                MapBoundsRules.PathCosts(diagonal, __instance.Models.MapDistanceModel.RegionSwitchCostFromLandToSea, out var switchCost, out var limit);
                Campaign.PlayerRegionSwitchCostFromLandToSea = switchCost;
                Campaign.PathFindingMaxCostLimit = limit;

                SubModule.Log("Map borders: settlements reach past the server map scene's borders " + old + "; borders now " + widened
                    + ", terrain size " + MapBoundsRules.Text(oldSize.x) + "x" + MapBoundsRules.Text(oldSize.y)
                    + " -> " + MapBoundsRules.Text(newSize.x) + "x" + MapBoundsRules.Text(newSize.y)
                    + ", map diagonal " + MapBoundsRules.Text(old.Diagonal) + " -> " + MapBoundsRules.Text(diagonal)
                    + ", path cost limit " + oldLimit + " -> " + limit + ".");
            }
            catch (Exception ex)
            {
                SubModule.Log("Map borders: could not widen the server's map borders: " + ex);
            }
        }

        private static IEnumerable<KeyValuePair<float, float>> SettlementPositions()
        {
            foreach (var settlement in Settlement.All)
            {
                if (settlement?.SettlementComponent == null)
                    continue;
                var position = settlement.Position;
                yield return new KeyValuePair<float, float>(position.X, position.Y);
            }
        }

        // The campaign's border copies have private setters, checked before anything changes.
        private static void SetCampaign(string property, object value) =>
            AccessTools.PropertySetter(typeof(Campaign), property).Invoke(null, new[] { value });
    }
}
