using System.Collections.Generic;
using UnityEngine;

namespace TheGrid.Core
{
    // Where things stand on the map. Art pixels, y up; the origin is the centre of plot 0's north corner tile.
    // A tile is 64 by 32 pixels, the size classic tycoon games draw one. Columns run down to the right, rows down to the left.
    public static class Iso
    {
        public const int TileW = 64, TileH = 32, PlotTiles = 12, RidersPerRow = 2;

        // Screen pixels per art pixel. Each level doubles the last, so pixels stay whole.
        public static readonly float[] Zoom = { .25f, .5f, 1f, 2f, 4f };

        // Two streets of five 2 by 2 footprints, each with a two-tile road in front. A tower hides whatever stands within four tiles
        // behind it, so the tall work buildings line the back street, where behind them is only the next plot's empty front margin;
        // the low buildings line the front street, across an open plaza, and hide nobody.
        static readonly Vector2Int[] Tiles =
        {
            new Vector2Int(9, 0),   // Garage
            new Vector2Int(1, 0),   // Library
            new Vector2Int(5, 0),   // Lab
            new Vector2Int(3, 0),   // ReviewHall
            new Vector2Int(7, 0),   // Workshop
            new Vector2Int(1, 6),   // Booth
            new Vector2Int(3, 6),   // Gate
            new Vector2Int(5, 6),   // Yard
            new Vector2Int(7, 6),   // Lot
            new Vector2Int(9, 6),   // Depot
        };

        public static Vector2Int PlotOrigin(Vector2Int cell) => new Vector2Int(cell.x * PlotTiles, cell.y * PlotTiles);

        public static Vector2 TileCentre(int col, int row) => Point(col, row);

        static Vector2 Point(float col, float row) => new Vector2((col - row) * (TileW / 2), -(col + row) * (TileH / 2));

        public static Vector2Int BuildingTile(Building b) => Tiles[(int)b];

        // The centre of a building's footprint.
        public static Vector2 BuildingSpot(Vector2Int cell, Building b)
        {
            var o = PlotOrigin(cell) + BuildingTile(b);
            return Point(o.x + .5f, o.y + .5f);
        }

        // Riders park on the road in front of their building, two to a row, rows stepping away from it.
        // shortcut: the road holds 8 riders a building; more spill over the tiles in front. Widen the rows if crowds that size turn out to be common.
        public static Vector2 RiderSpot(Vector2Int cell, Building b, int slot)
        {
            var t = BuildingTile(b);
            var o = PlotOrigin(cell) + t;
            // The front street is nudged a quarter tile along, so a crowd that spills across the plaza never lands on its spots.
            return Point(o.x + slot % RidersPerRow + t.y / 6 * .25f, o.y + 1.75f + slot / RidersPerRow * .5f);
        }

        // The four sides of a plot, each as the step to the cell beyond it. On screen: lower left, lower right, upper left, upper right.
        public static readonly Vector2Int[] Sides = { new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, -1) };

        // A city always has company on two sides: another city, or a thoroughfare. These are the roads that takes, each as the
        // empty cell it runs through and the step from that cell to the city whose side it runs along.
        // A road goes first where the empty cell already lies between cities, so one stretch of land serves both; after that
        // on a front side, where no tower hides it. It follows only from where the cities are.
        public static List<(Vector2Int cell, Vector2Int toCity)> Thoroughfares(IEnumerable<Vector2Int> cities)
        {
            var all = new HashSet<Vector2Int>(cities);
            var roads = new List<(Vector2Int, Vector2Int)>();
            foreach (var city in all)
            {
                var open = new List<Vector2Int>();
                foreach (var side in Sides)
                    if (!all.Contains(city + side)) open.Add(side);
                for (int need = 2 - (Sides.Length - open.Count); need > 0; need--)
                {
                    int best = 0;
                    for (int i = 1; i < open.Count; i++)
                        if (CitiesAround(all, city + open[i]) > CitiesAround(all, city + open[best])) best = i;
                    roads.Add((city + open[best], -open[best]));
                    open.RemoveAt(best);
                }
            }
            return roads;
        }

        static int CitiesAround(HashSet<Vector2Int> all, Vector2Int cell)
        {
            int n = 0;
            foreach (var side in Sides)
                if (all.Contains(cell + side)) n++;
            return n;
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
