using System.Collections.Generic;
using UnityEngine;

namespace TheGrid.Core
{
    // Where things stand on the map. Art pixels, y up; the origin is the centre of plot 0's north corner tile.
    // A tile is 64 by 32 pixels, the size classic tycoon games draw one. Columns run down to the right, rows down to the left.
    // What stands in a sector: six buildings and the fountain.
    public enum Structure { ServerTower, ReviewHall, Relay, Works, Depot, Fountain, Workshop }

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
            // A bot on an upper floor is drawn a storey up the screen for each floor.
            return Point(o.x + at.x, o.y + at.y) + new Vector2(0, layout.Lift(place, slot));
        }

        // True for a point of the map, in art pixels, that is on a sector's depot or on one of the crates shown in front
        // of it: where a click is for the depot. `crates` is how many open pull requests the repo has.
        public static bool OnDepot(Vector2Int origin, Layout layout, int crates, Vector2 point)
        {
            // The building: four tiles by three on the ground, its roof some sixty pixels up.
            var d = point - StructureSpot(origin, layout, Structure.Depot);
            if (Mathf.Abs(d.x) < 104 && d.y > -44 && d.y < 96) return true;
            // A crate: a box about a tile across and half as high, standing on its spot.
            for (int slot = 0; slot < Mathf.Min(crates, Layout.Crates); slot++)
            {
                var c = point - CrateSpot(origin, layout, slot);
                if (Mathf.Abs(c.x) < 20 && c.y > -10 && c.y < 26) return true;
            }
            return false;
        }

        // The way a bot walks to its place (Layout.Way), in art pixels: each floor up is a storey up the screen.
        public static List<Vector2> Way(Vector2Int origin, Layout layout, Place place, int slot)
        {
            var points = new List<Vector2>();
            var way = layout.Way(place, slot);
            // The door, where a way of more than one point starts, is on the paving; the rest is on the building's floors.
            float room = layout.Lift(place, slot) - layout.Floor(place, slot) * Layout.StoreyPixels;
            for (int i = 0; i < way.Count; i++)
                points.Add(Point(origin.x + way[i].x, origin.y + way[i].y) + new Vector2(0, i == 0 && way.Count > 1 ? 0 : room + way[i].z * Layout.StoreyPixels));
            return points;
        }

        // The walk from one place to another (Layout.Walk), in art pixels, and with each point how much further down the
        // screen a bot there is drawn as if it stood: a bot in a room, on any floor or on a stair, is drawn over the room.
        public static List<Vector3> Walk(Vector2Int origin, Layout layout, Place from, int fromSlot, Place to, int toSlot)
        {
            var points = new List<Vector3>();
            foreach (var p in layout.Walk(from, fromSlot, to, toSlot))
            {
                var at = Point(origin.x + p.x, origin.y + p.y);
                var room = layout.Room(new Vector2(p.x, p.y));
                if (room is Structure tall && tall != Structure.Workshop) at.y += Layout.FloorPixels + p.z * Layout.StoreyPixels;
                points.Add(new Vector3(at.x, at.y, at.y - Depth(origin, layout, room, at.y)));
            }
            return points;
        }

        // A bot at a job (Jobs), a step from its own place. stands: its own place in art pixels, with how much further down
        // the screen it is drawn there as if it stood. step: the job's step, in tiles. The bot is drawn a step away, and as
        // far down the order as at its own place: a step toward the back of its room does not put it behind its building.
        public static Vector3 AtWork(Vector3 stands, Vector2 step)
        {
            var by = Point(step.x, step.y);
            return new Vector3(stands.x + by.x, stands.y + by.y, stands.z + by.y);
        }

        // How far down the screen a bot is drawn as if it stood, in art pixels: what is lower is drawn later, over what is
        // higher. In the open that is where it stands. Inside a building it is never higher than just below the point the
        // building is drawn from, or a bot at the back of a room would be drawn first and the room over it. Two bots in one
        // room keep their order.
        public static float BotDepth(Vector2Int origin, Layout layout, Place place, int slot) =>
            Depth(origin, layout, layout.IsInside(place, slot) ? World.StructureOf(place) : null, BotSpot(origin, layout, place, slot).y);

        static float Depth(Vector2Int origin, Layout layout, Structure? room, float stands)
        {
            if (!(room is Structure building)) return stands;
            float front = StructureSpot(origin, layout, building).y - 1;
            return stands <= front ? stands : front + (stands - front) * .001f;
        }

        // Where a crate of the depot stands, in a sector arranged as `layout`.
        public static Vector2 CrateSpot(Vector2Int origin, Layout layout, int slot)
        {
            var at = layout.Crate(slot);
            return Point(origin.x + at.x, origin.y + at.y);
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

        // The whole of a courier's ride out, in tiles (RideTiles) or in art pixels (Ride): from the front of the sender's
        // building over its plot (Layout.ToAisle, then down the aisle) to its crossing, by the lanes and roads of the
        // sectors on its way (Route), and over the recipient's plot to the front of its building. Within one sector, from
        // the one front to the other by the aisle. Every leg runs along columns or along rows, through nothing that
        // stands. It rides back the same way.
        public static List<Vector2> RideTiles(Vector2Int fromOrigin, Layout fromLayout, Place fromPlace, int fromSlot, Vector2Int toOrigin, Layout toLayout, Place toPlace, int toSlot, IReadOnlyList<Vector2Int> way)
        {
            var ride = new List<Vector2>();
            void Put(Vector2Int o, Vector2 p)
            {
                var at = new Vector2(o.x + p.x, o.y + p.y);
                if (ride.Count == 0 || ride[ride.Count - 1] != at) ride.Add(at);
            }
            foreach (var p in fromLayout.ToAisle(fromPlace, fromSlot)) Put(fromOrigin, p);
            var lanes = Route(way);
            if (lanes.Count > 0)
            {
                // Down the aisle to the lane in front, along it to the crossing, and at the other end the same turned round.
                Put(fromOrigin, new Vector2(Layout.Aisle, PlotTiles + .5f));
                foreach (var p in lanes) Put(Vector2Int.zero, p);
                Put(toOrigin, new Vector2(Layout.Aisle, PlotTiles + .5f));
            }
            var last = toLayout.ToAisle(toPlace, toSlot);
            for (int i = last.Count - 1; i >= 0; i--) Put(toOrigin, last[i]);
            return ride;
        }

        public static List<Vector2> Ride(Vector2Int fromOrigin, Layout fromLayout, Place fromPlace, int fromSlot, Vector2Int toOrigin, Layout toLayout, Place toPlace, int toSlot, IReadOnlyList<Vector2Int> way)
        {
            var ride = new List<Vector2>();
            foreach (var p in RideTiles(fromOrigin, fromLayout, fromPlace, fromSlot, toOrigin, toLayout, toPlace, toSlot, way)) ride.Add(Point(p.x, p.y));
            return ride;
        }

        // One upright slice of the ribbon of light a courier leaves behind it. x: the middle of the slice. y: the ground at
        // the lower end of the piece of the way it stands on. wide: how wide it is. rise: how much higher the ground is at
        // the other end of that piece, which the slice is taller by. at: how far along the ride it starts.
        public struct RibbonSlice { public float x, y, wide, rise, at; }

        // A ride (Ride), cut into upright slices so many art pixels wide, side by side: a wall along the way, seen as the
        // map is seen. Each is a thing of its own, so that each is drawn in its own place among the buildings. Where the
        // way runs straight down the screen the wall is seen edge on: one slice, as much taller as that piece is long.
        public static List<RibbonSlice> Ribbon(IReadOnlyList<Vector2> ride, float step)
        {
            var slices = new List<RibbonSlice>();
            float gone = 0;
            for (int i = 1; i < ride.Count; i++)
            {
                Vector2 a = ride[i - 1], b = ride[i];
                float dx = b.x - a.x, dy = b.y - a.y, leg = (b - a).magnitude, across = Mathf.Abs(dx);
                if (across < .001f)
                {
                    if (leg > 0) slices.Add(new RibbonSlice { x = a.x, y = Mathf.Min(a.y, b.y), wide = step, rise = Mathf.Abs(dy), at = gone });
                }
                else
                    for (float from = 0; from < across - .001f; from += step)
                    {
                        float to = Mathf.Min(from + step, across), y0 = a.y + dy * from / across, y1 = a.y + dy * to / across;
                        slices.Add(new RibbonSlice { x = a.x + Mathf.Sign(dx) * (from + to) / 2, y = Mathf.Min(y0, y1), wide = to - from, rise = Mathf.Abs(y1 - y0), at = gone + leg * from / across });
                    }
                gone += leg;
            }
            return slices;
        }

        // How long a courier takes over so many art pixels: four tiles a second, and never under three seconds, or a short
        // ride is over before anyone has looked.
        public static float RideSeconds(float pixels) => Mathf.Max(3f, pixels / (4f * TileStep));

        // A place on a sector's lane (Ring), moved from the middle of the lane's inner tile to the middle of the lane, which
        // is two tiles wide: half a tile further out. Along the lane it moves half a tile on, to the middle of the road
        // that meets the lane there, which is two tiles wide as well.
        static Vector2 Middle(Vector2Int ring) => new Vector2(ring.x == -1 ? -1.5f : ring.x + .5f, ring.y == -1 ? -1.5f : ring.y + .5f);

        // The way a courier rides through the sectors at these origins, first to last: corner points, in tiles (column,
        // row). It leaves from the crossing in front of the first, goes round each sector's lane to the side the next one
        // is on and along the road to it, and ends at the last one's crossing: all of it down the middle of the lane or
        // the road. Empty for fewer than two.
        public static List<Vector2> Route(IReadOnlyList<Vector2Int> hops)
        {
            var route = new List<Vector2>();
            if (hops == null || hops.Count < 2) return route;
            void Put(Vector2 p)
            {
                if (route.Count == 0 || route[route.Count - 1] != p) route.Add(p);
            }
            void Add(Vector2Int o, int node) => Put(o + Middle(Ring[node]));
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
                int arrive = (leave + Ring.Length / 2) % Ring.Length;   // a road arrives on the side facing the one it left
                // RoadLine runs along the first of the road's two rows of tiles, from lane to lane: the middle of the road
                // is half a tile over, and its ends are the middles of the two lanes.
                for (int k = 0; k < line.Count; k++)
                    Put(k == 0 ? at + Middle(Ring[leave]) : k == line.Count - 1 ? next + Middle(Ring[arrive]) : line[k] + new Vector2(.5f, .5f));
                at = next;
                node = arrive;
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

        // Which way the map moves for a pointer resting within `edge` of an edge of the map: -1, 0 or 1 across, and the
        // same up, where up is toward the top of the window. Nothing while the pointer is off the map. view and pointer
        // are in the window's points, y running down.
        public static Vector2 EdgePan(Rect view, Vector2 pointer, float edge)
        {
            if (!view.Contains(pointer)) return Vector2.zero;
            return new Vector2(pointer.x < view.xMin + edge ? -1 : pointer.x > view.xMax - edge ? 1 : 0, pointer.y < view.yMin + edge ? 1 : pointer.y > view.yMax - edge ? -1 : 0);
        }

        // After a level of zoom the wheel rests for so many seconds.
        public const float WheelRest = .15f;

        // How many levels to zoom for a turn of the wheel: 1 in, -1 out, or 0. A mouse's wheel sends a notch at a time and
        // two fingers on a trackpad many small turns, which are added up in `pending` until they make a notch. However
        // hard it is turned it is one level, and then the wheel rests (`since`: seconds since the last level), or one
        // swipe would run through every level there is.
        public static int WheelStep(ref float pending, float delta, float since)
        {
            if (since < WheelRest)
            {
                pending = 0;
                return 0;
            }
            if (pending * delta < 0) pending = 0;   // turned back: start afresh
            pending += delta;
            if (Mathf.Abs(pending) < 1f) return 0;
            int step = pending < 0 ? 1 : -1;
            pending = 0;
            return step;
        }

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
