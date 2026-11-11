using System.Collections.Generic;
using UnityEngine;

namespace TheGrid.Core
{
    // Where things stand on the map. Art pixels, y up; the origin is the centre of plot 0's north corner tile.
    // A tile is 64 by 32 pixels, the size classic tycoon games draw one. Columns run down to the right, rows down to the left.
    // What stands in a sector: six buildings and the fountain.
    public enum Structure { ServerTower, ReviewHall, Relay, Works, AssemblyBay, Fountain, Workshop }

    public static class Iso
    {
        public const int TileW = 64, TileH = 32, PlotTiles = 12, RidersPerRow = 2;

        // A path runs round every plot, so between two neighbouring plots there is one, and the paths of the whole map join up.
        public const int PathTiles = 2, Pitch = PlotTiles + PathTiles;

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

        public static Vector2Int PlotOrigin(Vector2Int cell) => new Vector2Int(cell.x * Pitch, cell.y * Pitch);

        // True for a tile of the path round the plot at this cell: the strip beyond each of its four sides, corners included.
        public static bool OnPath(Vector2Int cell, int col, int row)
        {
            var o = PlotOrigin(cell);
            int c = col - o.x, r = row - o.y;
            bool inPlot = c >= 0 && r >= 0 && c < PlotTiles && r < PlotTiles;
            return !inPlot && c >= -PathTiles && r >= -PathTiles && c < PlotTiles + PathTiles && r < PlotTiles + PathTiles;
        }

        public static Vector2 TileCentre(int col, int row) => Point(col, row);

        // Where a point of the map is in art pixels. A tile's centre is at whole numbers.
        public static Vector2 Point(float col, float row) => new Vector2((col - row) * (TileW / 2), -(col + row) * (TileH / 2));

        // A sector, in tiles from its plot's first tile. Tall things stand along the back, so they hide no bot; the low hall and
        // the cut-away workshop stand in the middle, either side of the fountain; the front is open ground.
        static readonly RectInt[] Footprints =
        {
            new RectInt(0, 0, 3, 3),    // ServerTower
            new RectInt(4, 0, 3, 3),    // ReviewHall
            new RectInt(7, 0, 2, 2),    // Relay
            new RectInt(9, 0, 3, 3),    // Works
            new RectInt(0, 5, 4, 3),    // AssemblyBay
            new RectInt(5, 5, 2, 2),    // Fountain
            new RectInt(8, 5, 4, 3),    // Workshop
        };

        // Where the bots of each place stand: the first spot, how many to a row, the step along the row and the step to the next
        // row. The numbers are chosen so that fifty bots at every place are four hundred different points.
        static readonly (float col, float row, int perRow, float along, float away)[] Spots =
        {
            (0.25f, 3.25f, 3, 1f, .5f),     // ServerTower: in front of its door
            (4.25f, 3.25f, 3, 1f, .5f),     // ReviewHall
            (9.25f, 3.25f, 3, 1f, .5f),     // Works
            (0.4f, 8.25f, 4, 1f, .5f),      // AssemblyBay
            (8.7f, 8.25f, 4, 1f, .5f),      // Workshop, from its third bot
            (4.4f, 7.4f, 4, 1f, .5f),       // Plaza: done, in front of the fountain
            (3.5f, 9.75f, 6, 1f, .75f),     // Forecourt: every bot that needs the owner, in the open at the front
            (9.6f, 9.75f, 3, .8f, .75f),    // Dock: offline
        };

        public static RectInt Footprint(Structure s) => Footprints[(int)s];

        // The centre of a structure's footprint.
        public static Vector2 StructureSpot(Vector2Int cell, Structure s)
        {
            var o = PlotOrigin(cell);
            var f = Footprint(s);
            return Point(o.x + f.x + (f.width - 1) / 2f, o.y + f.y + (f.height - 1) / 2f);
        }

        public static Vector2 BotSpot(Vector2Int cell, Place place, int slot)
        {
            var o = PlotOrigin(cell);
            if (place == Place.Workshop)
            {
                // The workshop's walls are cut away so its first two bots can be seen inside: one at the terminal, one at the bench.
                if (slot == 0) return Point(o.x + 9.5f, o.y + 6.0f);
                if (slot == 1) return Point(o.x + 10.5f, o.y + 6.6f);
                slot -= 2;
            }
            var s = Spots[(int)place];
            return Point(o.x + s.col + slot % s.perRow * s.along, o.y + s.row + slot / s.perRow * s.away);
        }

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

        // The pixel rectangle that covers these plots' ground and the paths round them.
        public static Rect Bounds(IEnumerable<Vector2Int> cells)
        {
            bool any = false;
            float xMin = 0, xMax = 0, yMin = 0, yMax = 0;
            foreach (var cell in cells)
            {
                var o = PlotOrigin(cell);
                int first = -PathTiles, last = PlotTiles + PathTiles - 1;
                float l = (o.x - o.y + first - last) * (TileW / 2) - TileW / 2, r = (o.x - o.y + last - first) * (TileW / 2) + TileW / 2;
                float top = -(o.x + o.y + 2 * first) * (TileH / 2) + TileH / 2, bottom = -(o.x + o.y + 2 * last) * (TileH / 2) - TileH / 2;
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
