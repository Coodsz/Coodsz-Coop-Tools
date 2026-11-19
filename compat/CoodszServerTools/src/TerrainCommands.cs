// Coodsz Server Tools: the real shape of the campaign map (land, sea, rivers, forests, mountains)
// for the background of Coodsz Host's live map page. Read only: it asks the map scene's own
// navigation mesh what terrain is under each point of a grid and writes it to a file.
// It sits in the module that follows CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep);
// this command is new.
//
// Copyright (c) 2026 aderimo
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using static CoodszServerTools.Text;

namespace CoodszServerTools;

internal static class TerrainCommands
{
    /// <summary>
    /// <c>coodsz.terrain [columns] [from row] [rows]</c>: the terrain type of every cell of a grid
    /// over the whole map (the scene's own map borders), written to <c>out\terrain.txt</c>.
    /// <code>
    /// TERRAIN|min x|min y|max x|max y|columns|rows|from row|row count|milliseconds
    /// TROW|row|run length cells
    /// </code>
    /// Row 0 is the north edge (max y). A cell is <c>a</c> plus the game's TerrainType number
    /// (b plain, e forest, h mountain, i lake, k water, l river, s coastal sea, t open sea, ...)
    /// or <c>.</c> where there is no navigation mesh (impassable). A cell is followed by a count
    /// when the same cell repeats (<c>k12b3</c> is twelve water cells, then three plain).
    /// The map does not change during a campaign, so Coodsz Host reads it once per server start,
    /// a few rows at a time so the game does not stall.
    /// </summary>
    public static string Terrain(List<string> args)
    {
        if (Campaign.Current is null) return NoCampaign;
        var scene = Campaign.Current.MapSceneWrapper;
        if (scene is null) return "COODSZ_ERROR the map scene is not loaded.";

        var columns = TryInt(args, 0, 16, 2000, out var c) ? c : 600;
        scene.GetMapBorders(out var min, out var max, out _);
        if (!(max.x > min.x && max.y > min.y))
        {
            var size = scene.GetTerrainSize();
            min = new Vec2(0, 0);
            max = size;
        }

        if (!(max.x > min.x && max.y > min.y)) return "COODSZ_ERROR the map has no size.";
        var step = (max.x - min.x) / columns;
        var rows = Math.Max(1, (int)Math.Round((max.y - min.y) / step));
        var from = TryInt(args, 1, 0, rows - 1, out var f) ? f : 0;
        var count = TryInt(args, 2, 1, rows, out var n) ? n : rows;
        count = Math.Min(count, rows - from);

        var clock = Stopwatch.StartNew();
        var lines = new List<string>(count + 1) { "" };
        var row = new StringBuilder(columns * 2);
        for (var r = from; r < from + count; r++)
        {
            row.Clear();
            var y = max.y - (r + 0.5f) * step;
            var last = '\0';
            var run = 0;
            for (var col = 0; col < columns; col++)
            {
                var cell = Cell(scene, new Vec2(min.x + (col + 0.5f) * step, y));
                if (cell == last)
                {
                    run++;
                    continue;
                }

                Flush(row, last, run);
                last = cell;
                run = 1;
            }

            Flush(row, last, run);
            lines.Add(Line("TROW", I(r), row.ToString()));
        }

        lines[0] = Line("TERRAIN", F(min.x), F(min.y), F(max.x), F(max.y), I(columns), I(rows), I(from), I(count), I((int)clock.ElapsedMilliseconds));
        return ToFile("terrain", lines);
    }

    /// <summary>The land mesh first, then the sea mesh (War Sails maps have one); '.' when neither has a face here.</summary>
    private static char Cell(TaleWorlds.CampaignSystem.Map.IMapScene scene, Vec2 at)
    {
        var face = scene.GetFaceIndex(new CampaignVec2(at, true));
        if (!face.IsValid())
            face = scene.GetFaceIndex(new CampaignVec2(at, false));
        if (!face.IsValid()) return '.';
        var type = face.FaceGroupIndex;
        return type >= 0 && type <= 25 ? (char)('a' + type) : '.';
    }

    private static void Flush(StringBuilder row, char cell, int run)
    {
        if (run == 0) return;
        row.Append(cell);
        if (run > 1) row.Append(I(run));
    }
}
