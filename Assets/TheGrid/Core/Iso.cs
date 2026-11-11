using System.Collections.Generic;
using UnityEngine;

namespace TheGrid.Core
{
    // Where things stand on the map. Art pixels, y up; the origin is the centre of plot 0's north corner tile.
    // A tile is 64 by 32 pixels, the size classic tycoon games draw one. Columns run down to the right, rows down to the left.
    public static class Iso
    {
        public const int TileW = 64, TileH = 32, PlotTiles = 12, RidersPerRow = 3;

        // Screen pixels per art pixel. Each level doubles the last, so pixels stay whole.
        public static readonly float[] Zoom = { .25f, .5f, 1f, 2f, 4f };

        // Three bands of 2 by 2 footprints with a two-tile road in front of each. Tall buildings stand in the front band,
        // where the only thing behind them is their own band's back wall, so they hide as few riders as possible.
        static readonly Vector2Int[] Tiles =
        {
            new Vector2Int(9, 0),   // Garage
            new Vector2Int(0, 8),   // Library
            new Vector2Int(6, 8),   // Lab
            new Vector2Int(9, 8),   // ReviewHall
            new Vector2Int(3, 8),   // Workshop
            new Vector2Int(0, 0),   // Booth
            new Vector2Int(3, 0),   // Gate
            new Vector2Int(6, 0),   // Yard
            new Vector2Int(0, 4),   // Lot
            new Vector2Int(3, 4),   // Depot
        };

        public static Vector2Int PlotOrigin(Vector2Int cell) => new Vector2Int(cell.x * PlotTiles, cell.y * PlotTiles);

        public static Vector2 TileCentre(int col, int row) => Point(col, row);

        static Vector2 Point(float col, float row) => new Vector2((col - row) * (TileW / 2), -(col + row) * (TileH / 2));

        public static Vector2Int BuildingTile(Building b) => Tiles[(int)b];

        public static bool IsRoad(int row) => row % 4 >= 2;

        // The centre of a building's footprint.
        public static Vector2 BuildingSpot(Vector2Int cell, Building b)
        {
            var o = PlotOrigin(cell) + BuildingTile(b);
            return Point(o.x + .5f, o.y + .5f);
        }

        // Riders park on the road in front of their building, three to a row, rows stepping away from it.
        // shortcut: the road holds 12 riders a building; more spill over the tiles in front. Widen the rows if crowds that size turn out to be common.
        public static Vector2 RiderSpot(Vector2Int cell, Building b, int slot)
        {
            var t = BuildingTile(b);
            var o = PlotOrigin(cell) + t;
            // Each band is nudged a quarter tile along, so a crowd that spills into the next band never lands on that band's spots.
            return Point(o.x + slot % RidersPerRow + t.y / 4 * .25f, o.y + 1.75f + slot / RidersPerRow * .5f);
        }

        // The pixel rectangle that covers these plots' ground.
        public static Rect Bounds(IEnumerable<Vector2Int> cells)
        {
            bool any = false;
            float xMin = 0, xMax = 0, yMin = 0, yMax = 0;
            foreach (var cell in cells)
            {
                var o = PlotOrigin(cell);
                int last = PlotTiles - 1;
                float l = (o.x - o.y - last) * (TileW / 2) - TileW / 2, r = (o.x - o.y + last) * (TileW / 2) + TileW / 2;
                float top = -(o.x + o.y) * (TileH / 2) + TileH / 2, bottom = -(o.x + o.y + 2 * last) * (TileH / 2) - TileH / 2;
                if (!any) { xMin = l; xMax = r; yMin = bottom; yMax = top; any = true; continue; }
                xMin = Mathf.Min(xMin, l); xMax = Mathf.Max(xMax, r); yMin = Mathf.Min(yMin, bottom); yMax = Mathf.Max(yMax, top);
            }
            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        public static int ClampLevel(int level) => Mathf.Clamp(level, 0, Zoom.Length - 1);

        // The closest level at which the content fits the view; the farthest when none does; actual size when there is nothing to fit.
        public static int FitLevel(float viewW, float viewH, Rect content)
        {
            if (content.width <= 0 || content.height <= 0) return 2;
            for (int level = Zoom.Length - 1; level > 0; level--)
                if (content.width * Zoom[level] <= viewW && content.height * Zoom[level] <= viewH) return level;
            return 0;
        }
    }
}
