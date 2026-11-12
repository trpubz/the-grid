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

        // A lane runs round every plot: the sector's own. Between one sector's lane and the next's lies open ground, so each
        // sector stands apart, and a link crosses it on each side to join the two lanes.
        public const int LaneTiles = 2, GapTiles = 3, Pitch = PlotTiles + 2 * LaneTiles + GapTiles;

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

        // True for a tile of a link: the two-tile road across the open ground between one sector's lane and the next's. One
        // runs out from the crossing in front of every sector, and one from the same count of tiles down its side.
        public static bool OnLink(int col, int row)
        {
            int c = Local(col), r = Local(row);
            bool gapC = c >= PlotTiles + LaneTiles, gapR = r >= PlotTiles + LaneTiles;
            return (gapR && !gapC && (c == Crossing || c == Crossing + 1)) || (gapC && !gapR && (r == Crossing || r == Crossing + 1));
        }

        // A tile's column or row counted within its own sector's square of the map: -2 and -1 are the lane behind the plot,
        // 0 to 11 the plot, 12 and 13 the lane in front, 14 to 16 the open ground.
        static int Local(int tile)
        {
            int m = ((tile + LaneTiles) % Pitch + Pitch) % Pitch;
            return m - LaneTiles;
        }

        public static Vector2 TileCentre(int col, int row) => Point(col, row);

        // Where a point of the map is in art pixels. A tile's centre is at whole numbers.
        public static Vector2 Point(float col, float row) => new Vector2((col - row) * (TileW / 2), -(col + row) * (TileH / 2));

        // The centre of a structure's footprint, in a sector arranged as `layout`.
        public static Vector2 StructureSpot(Vector2Int cell, Layout layout, Structure s)
        {
            var o = PlotOrigin(cell);
            var f = layout.Footprint(s);
            return Point(o.x + f.x + (f.width - 1) / 2f, o.y + f.y + (f.height - 1) / 2f);
        }

        // Where a bot stands, in a sector arranged as `layout`.
        public static Vector2 BotSpot(Vector2Int cell, Layout layout, Place place, int slot)
        {
            var o = PlotOrigin(cell);
            var at = layout.Spot(place, slot);
            return Point(o.x + at.x, o.y + at.y);
        }

        // The column, within a sector, of the crossing painted on the lane along its front: where a courier stops, and
        // where the link to the sector in front leaves.
        public const int Crossing = 5;

        // Round a sector's lane, on its inner tiles: the four places a link meets it, and the four corners between them.
        // In order round the plot: behind, the right, in front, the left.
        static readonly Vector2Int[] Ring =
        {
            new Vector2Int(Crossing, -1), new Vector2Int(PlotTiles, -1), new Vector2Int(PlotTiles, Crossing), new Vector2Int(PlotTiles, PlotTiles),
            new Vector2Int(Crossing, PlotTiles), new Vector2Int(-1, PlotTiles), new Vector2Int(-1, Crossing), new Vector2Int(-1, -1),
        };
        const int Behind = 0, Right = 2, InFront = 4, Left = 6;

        // The way a courier rides from one sector to another: corner points, in tiles (column, row), every leg along a lane
        // or a link. It leaves from the crossing in front of the sender, goes round sectors' lanes and across the links
        // between them, a column of sectors at a time and then a row, and ends at the recipient's crossing. Empty within
        // one sector.
        // shortcut: lanes and links are drawn only for sectors that exist, so across a hole in the map a courier rides over
        // open ground. Route round holes if maps with them turn out to be common.
        public static List<Vector2> Route(Vector2Int fromCell, Vector2Int toCell)
        {
            var route = new List<Vector2>();
            if (fromCell == toCell) return route;
            void Add(Vector2Int cell, int node)
            {
                var o = PlotOrigin(cell);
                var p = new Vector2(o.x + Ring[node].x, o.y + Ring[node].y);
                if (route.Count == 0 || route[route.Count - 1] != p) route.Add(p);
            }
            // Round the lane from one node to another, the shorter way; only the corners and the last node are corner points.
            void Round(Vector2Int cell, int from, int to)
            {
                int step = (to - from + Ring.Length) % Ring.Length <= Ring.Length / 2 ? 1 : -1;
                for (int n = from; n != to;)
                {
                    n = (n + step + Ring.Length) % Ring.Length;
                    if (n % 2 == 1 || n == to) Add(cell, n);
                }
            }
            var cell0 = fromCell;
            int at = InFront;
            Add(cell0, at);
            while (cell0 != toCell)
            {
                int leave = cell0.x < toCell.x ? Right : cell0.x > toCell.x ? Left : cell0.y < toCell.y ? InFront : Behind;
                var next = cell0 + (leave == Right ? Vector2Int.right : leave == Left ? Vector2Int.left : leave == InFront ? Vector2Int.up : Vector2Int.down);
                Round(cell0, at, leave);
                cell0 = next;
                at = (leave + Ring.Length / 2) % Ring.Length;   // a link arrives on the side facing the one it left
                Add(cell0, at);
            }
            Round(cell0, at, InFront);
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
