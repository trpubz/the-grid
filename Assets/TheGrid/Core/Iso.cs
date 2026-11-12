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
        public const int TileW = 64, TileH = 32, PlotTiles = 12;

        // How far apart on screen the centres of two neighbouring tiles are, in pixels.
        public static readonly float TileStep = Mathf.Sqrt(TileW * TileW / 4f + TileH * TileH / 4f);

        // A lane runs round every plot, so between two neighbouring plots there is one, and the lanes of the whole map join up.
        public const int LaneTiles = 2, Pitch = PlotTiles + LaneTiles;

        // Screen pixels per art pixel. Each level doubles the last, so pixels stay whole.
        public static readonly float[] Zoom = { .25f, .5f, 1f, 2f, 4f };

        public static Vector2Int PlotOrigin(Vector2Int cell) => new Vector2Int(cell.x * Pitch, cell.y * Pitch);

        // True for a tile of the lane round the plot at this cell: the strip beyond each of its four sides, corners included.
        public static bool OnLane(Vector2Int cell, int col, int row)
        {
            var o = PlotOrigin(cell);
            int c = col - o.x, r = row - o.y;
            bool inPlot = c >= 0 && r >= 0 && c < PlotTiles && r < PlotTiles;
            return !inPlot && c >= -LaneTiles && r >= -LaneTiles && c < PlotTiles + LaneTiles && r < PlotTiles + LaneTiles;
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
        // row. The numbers are chosen so that fifty bots at every place are four hundred different points, and so that the
        // first six of a place are never behind the server tower of the sector in front: that tower hides the strip from the
        // fountain to this sector's near corner.
        static readonly (float col, float row, int perRow, float along, float away)[] Spots =
        {
            (0.25f, 3.25f, 3, 1f, .5f),     // ServerTower: in front of its door
            (4.25f, 3.25f, 3, 1f, .5f),     // ReviewHall
            (9.25f, 2.95f, 3, 1f, .5f),     // Works: close to its door, clear of the workshop's back wall
            (0.4f, 8.25f, 4, 1f, .5f),      // AssemblyBay
            (8.7f, 8.25f, 4, 1f, .5f),      // Workshop, from its fifth bot
            (4.3f, 7.0f, 3, .6f, .45f),     // Plaza: done, at the fountain's near left
            (2.0f, 10.25f, 6, 1f, .6f),     // Forecourt: every bot that needs the owner, in the open at the front left, far enough
                                            // forward that its badge is not on the head of a bot at the assembly bay
            (6.9f, 2.3f, 3, .6f, .5f),      // Dock: offline, parked in front of the relay
        };

        // The workshop's walls are cut away so its first four bots can be seen inside: at the terminal, at the bench, and
        // two on the open floor.
        static readonly Vector2[] InTheWorkshop = { new Vector2(9.5f, 6.0f), new Vector2(10.5f, 6.6f), new Vector2(10.0f, 7.0f), new Vector2(10.8f, 7.3f) };

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
                if (slot < InTheWorkshop.Length) return Point(o.x + InTheWorkshop[slot].x, o.y + InTheWorkshop[slot].y);
                // shortcut: a fifth bot waits outside in front, where the next sector's tower can hide it. Give the workshop
                // a bigger floor if five sessions testing at once in one repo turns out to be common.
                slot -= InTheWorkshop.Length;
            }
            var s = Spots[(int)place];
            return Point(o.x + s.col + slot % s.perRow * s.along, o.y + s.row + slot / s.perRow * s.away);
        }

        // The column, within a sector, of the crossing painted on the lane along its front: where a courier stops.
        public const int Crossing = 5;

        // The way a courier rides from one sector to another: corner points, in tiles (column, row), every leg along a lane.
        // It leaves from the crossing on the lane along the sender's front, keeps to that lane as far as the recipient's
        // column of sectors, turns down the lane beside the recipient, and ends at the recipient's crossing. The front lane,
        // not the back: a sector's own buildings never stand between that lane and us. Empty within one sector.
        // shortcut: a lane is drawn only round a sector that exists, so across a gap in the map a courier rides over open
        // ground; and a sector in front can still hide a stretch. Route round both if either turns out to matter.
        public static List<Vector2> Route(Vector2Int fromCell, Vector2Int toCell)
        {
            var route = new List<Vector2>();
            if (fromCell == toCell) return route;
            Vector2Int a = PlotOrigin(fromCell), b = PlotOrigin(toCell);
            var start = new Vector2(a.x + Crossing, a.y + PlotTiles);
            var end = new Vector2(b.x + Crossing, b.y + PlotTiles);
            route.Add(start);
            if (start.y != end.y)
            {
                float turn = b.x - 1;                                    // the lane down the recipient's left side
                if (turn != start.x) route.Add(new Vector2(turn, start.y));
                route.Add(new Vector2(turn, end.y));
            }
            route.Add(end);
            return route;
        }

        // The pixel rectangle that covers these plots' ground and the lanes round them.
        public static Rect Bounds(IEnumerable<Vector2Int> cells)
        {
            bool any = false;
            float xMin = 0, xMax = 0, yMin = 0, yMax = 0;
            foreach (var cell in cells)
            {
                var o = PlotOrigin(cell);
                int first = -LaneTiles, last = PlotTiles + LaneTiles - 1;
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
