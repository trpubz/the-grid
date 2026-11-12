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
        // sector stands apart, and a link crosses it to join the two lanes: a road, GapTiles long.
        public const int LaneTiles = 2, GapTiles = 3, Pitch = PlotTiles + 2 * LaneTiles + GapTiles;

        // Screen pixels per art pixel. Each level doubles the last, so pixels stay whole.
        public static readonly float[] Zoom = { .25f, .5f, 1f, 2f, 4f };

        // Where a sector stands is its origin: the tile of its plot's north corner. The sectors of one region stand on a
        // grid of their own, Pitch tiles apart; this is how far from the region's origin the sector on a cell of it stands.
        public static Vector2Int PlotOrigin(Vector2Int cell) => new Vector2Int(cell.x * Pitch, cell.y * Pitch);

        // True for a tile of the lane round the plot at this origin: the strip beyond each of its four sides, corners included.
        public static bool OnLane(Vector2Int origin, int col, int row)
        {
            var o = origin;
            int c = col - o.x, r = row - o.y;
            bool inPlot = c >= 0 && r >= 0 && c < PlotTiles && r < PlotTiles;
            return !inPlot && c >= -LaneTiles && r >= -LaneTiles && c < PlotTiles + LaneTiles && r < PlotTiles + LaneTiles;
        }

        // Between the lanes of two sectors of different regions lie at least this many tiles of open ground: wider than
        // the GapTiles between two sectors of one region, so a region is seen to stand apart.
        public const int RegionGap = 8;
        const int Square = PlotTiles + 2 * LaneTiles;   // a sector with its lane, across

        // True when two sectors, by their origins, stand far enough apart to be of different regions.
        public static bool Apart(Vector2Int a, Vector2Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y)) >= Square + RegionGap;

        // True for a tile of the sector at this origin: its plot or its lane.
        public static bool Within(Vector2Int origin, int col, int row)
        {
            int c = col - origin.x, r = row - origin.y;
            return c >= -LaneTiles && r >= -LaneTiles && c < PlotTiles + LaneTiles && r < PlotTiles + LaneTiles;
        }

        // How many tiles of the map a move of so many art pixels on the screen is, to the nearest tile.
        public static Vector2Int Tiles(Vector2 artPixels)
        {
            float a = artPixels.x / (TileW / 2), b = -artPixels.y / (TileH / 2);   // column less row, and column plus row
            return new Vector2Int(Mathf.RoundToInt((a + b) / 2), Mathf.RoundToInt((b - a) / 2));
        }

        public static Vector2 TileCentre(int col, int row) => Point(col, row);

        // Where a point of the map is in art pixels. A tile's centre is at whole numbers.
        public static Vector2 Point(float col, float row) => new Vector2((col - row) * (TileW / 2), -(col + row) * (TileH / 2));

        // The centre of a structure's footprint, in a sector arranged as `layout`.
        public static Vector2 StructureSpot(Vector2Int origin, Layout layout, Structure s)
        {
            var o = origin;
            var f = layout.Footprint(s);
            return Point(o.x + f.x + (f.width - 1) / 2f, o.y + f.y + (f.height - 1) / 2f);
        }

        // Where a bot stands, in a sector arranged as `layout`.
        public static Vector2 BotSpot(Vector2Int origin, Layout layout, Place place, int slot)
        {
            var o = origin;
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

        // The side of the sector at `a` that the road to the sector at `b` leaves by: the side b is further off on.
        static int Side(Vector2Int a, Vector2Int b)
        {
            var d = b - a;
            return Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? (d.x > 0 ? Right : Left) : (d.y > 0 ? InFront : Behind);
        }

        // True when a road between the two is told from `a`: b stands to its right or in front of it.
        public static bool RoadRunsFrom(Vector2Int a, Vector2Int b) => Side(a, b) == Right || Side(a, b) == InFront;

        // The road between two sectors, as the line a courier rides: from the inner tile of a's lane on the side b is on
        // to the inner tile of b's lane on the side facing a. Straight where the two face each other squarely, as two
        // sectors side by side in one region do. Otherwise it runs out from a, turns in the middle of the open ground
        // between the two, runs across to b's row or column, and turns again.
        // a must be the one to the left or behind (RoadRunsFrom), and the two must stand apart on that axis: sectors of one
        // region always do, and so do sectors of two regions (Apart).
        static List<Vector2> RoadLine(Vector2Int a, Vector2Int b)
        {
            bool right = Side(a, b) == Right;
            Vector2 from = a + Ring[right ? Right : InFront], to = b + Ring[right ? Left : Behind];
            var line = new List<Vector2> { from };
            if (right && from.y != to.y)
            {
                int mid = (a.x + PlotTiles + LaneTiles + b.x - LaneTiles - 1) / 2;   // between the first tile beyond a's lane and the last before b's
                line.Add(new Vector2(mid, from.y));
                line.Add(new Vector2(mid, to.y));
            }
            if (!right && from.x != to.x)
            {
                int mid = (a.y + PlotTiles + LaneTiles + b.y - LaneTiles - 1) / 2;
                line.Add(new Vector2(from.x, mid));
                line.Add(new Vector2(to.x, mid));
            }
            line.Add(to);
            return line;
        }

        // Every tile of the road between two sectors, given either way round: two tiles wide along RoadLine, from the
        // first tile beyond the one's lane to the last before the other's. A link between two sectors side by side in one
        // region is the shortest road there is: GapTiles long.
        public static List<Vector2Int> RoadTiles(Vector2Int a, Vector2Int b)
        {
            if (!RoadRunsFrom(a, b)) { var t = a; a = b; b = t; }
            var line = RoadLine(a, b);
            bool right = Side(a, b) == Right;
            var tiles = new HashSet<Vector2Int>();
            for (int i = 1; i < line.Count; i++)
            {
                Vector2Int p = Vector2Int.RoundToInt(line[i - 1]), q = Vector2Int.RoundToInt(line[i]);
                for (int c = Mathf.Min(p.x, q.x); c <= Mathf.Max(p.x, q.x) + 1; c++)
                    for (int r = Mathf.Min(p.y, q.y); r <= Mathf.Max(p.y, q.y) + 1; r++)
                        if (!Within(a, c, r) && !Within(b, c, r) && (right ? c > a.x && c < b.x : r > a.y && r < b.y)) tiles.Add(new Vector2Int(c, r));
            }
            return new List<Vector2Int>(tiles);
        }

        // The way a courier rides through the sectors at these origins, first to last: corner points, in tiles (column,
        // row). It leaves from the crossing in front of the first, goes round each sector's lane to the side the next one
        // is on and along the road to it, and ends at the last one's crossing. Empty for fewer than two.
        public static List<Vector2> Route(IReadOnlyList<Vector2Int> hops)
        {
            var route = new List<Vector2>();
            if (hops == null || hops.Count < 2) return route;
            void Put(Vector2 p)
            {
                if (route.Count == 0 || route[route.Count - 1] != p) route.Add(p);
            }
            void Add(Vector2Int o, int node) => Put(o + Ring[node]);
            // Round the lane from one node to another, the shorter way; only the corners and the last node are corner points.
            void Round(Vector2Int o, int from, int to)
            {
                int step = (to - from + Ring.Length) % Ring.Length <= Ring.Length / 2 ? 1 : -1;
                for (int n = from; n != to;)
                {
                    n = (n + step + Ring.Length) % Ring.Length;
                    if (n % 2 == 1 || n == to) Add(o, n);
                }
            }
            var at = hops[0];
            int node = InFront;
            Add(at, node);
            for (int i = 1; i < hops.Count; i++)
            {
                var next = hops[i];
                int leave = Side(at, next);
                Round(at, node, leave);
                bool forward = RoadRunsFrom(at, next);
                var line = forward ? RoadLine(at, next) : RoadLine(next, at);
                if (!forward) line.Reverse();
                foreach (var p in line) Put(p);
                at = next;
                node = (leave + Ring.Length / 2) % Ring.Length;   // a road arrives on the side facing the one it left
            }
            Round(at, node, InFront);
            return route;
        }

        // The pixel rectangle that covers these plots' ground and the lanes round them.
        public static Rect Bounds(IEnumerable<Vector2Int> origins)
        {
            bool any = false;
            float xMin = 0, xMax = 0, yMin = 0, yMax = 0;
            foreach (var o in origins)
            {
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
