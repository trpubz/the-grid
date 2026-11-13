using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGrid.Core
{
    // A straight line of light on the ground, one tile long, along an edge between what is paved (lane, road) and what
    // is not. a: it lies along the far edge of `cell`'s row, between (column, row - 1) and (column, row); else along
    // the near edge of its column, between (column, row) and (column + 1, row). of: the paved tile it is the edge of.
    public struct GroundEdge { public Vector2Int cell, of; public bool a; }

    // A rounded corner, all within one tile. tile: the tile. toward: which of its four corners, as steps along columns
    // and rows (-1 or 1 each). filled: the tile is open ground and the corner is filled in with paving, as where a road
    // meets a lane or on the inside of a bend; else the tile is paved and the corner is cut away, as at the outer corner
    // of a sector's lane or the outside of a bend. beside: the paved tile it goes with: itself, or a neighbour.
    public struct RoundCorner { public Vector2Int tile, toward, beside; public bool filled; }

    // Where the lines of light on the ground run. Lanes and roads are lit along every edge, and their corners are
    // rounded by one tile: the two lines that met in a point are joined by a quarter of a circle instead. The corner of
    // a sector's plot stays square: buildings stand right up to it.
    public static class Ground
    {
        static (Vector2Int cell, bool a) Edge(Vector2Int tile, int columns, int rows) =>
            rows != 0 ? (new Vector2Int(tile.x, rows < 0 ? tile.y : tile.y + 1), true) : (new Vector2Int(columns > 0 ? tile.x : tile.x - 1, tile.y), false);

        static readonly Vector2Int[] Sides = { new Vector2Int(0, -1), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(1, 0) };
        static readonly Vector2Int[] Corners = { new Vector2Int(-1, -1), new Vector2Int(1, -1), new Vector2Int(1, 1), new Vector2Int(-1, 1) };

        // paved: every tile of lane and road. closed: true for a tile of a sector's plot, which is never rounded into.
        public static void Outline(HashSet<Vector2Int> paved, Func<Vector2Int, bool> closed, out List<GroundEdge> edges, out List<RoundCorner> corners)
        {
            // Every edge between a paved tile and one that is not, and the open tiles that lie along them.
            var boundary = new Dictionary<(Vector2Int, bool), Vector2Int>();
            var open = new HashSet<Vector2Int>();
            foreach (var t in paved)
                foreach (var d in Sides)
                {
                    if (paved.Contains(t + d)) continue;
                    boundary[Edge(t, d.x, d.y)] = t;
                    if (!closed(t + d)) open.Add(t + d);
                }

            // A paved tile whose two neighbours at a corner are both open, and the tile beyond the corner too: an outer
            // corner, cut away. An open tile whose two neighbours at a corner are both paved: an inner one, filled in.
            var found = new List<RoundCorner>();
            foreach (var t in paved)
                foreach (var c in Corners)
                    if (!paved.Contains(t + new Vector2Int(c.x, 0)) && !paved.Contains(t + new Vector2Int(0, c.y)) && !paved.Contains(t + c))
                        found.Add(new RoundCorner { tile = t, toward = c, beside = t });
            foreach (var t in open)
                foreach (var c in Corners)
                    if (paved.Contains(t + new Vector2Int(c.x, 0)) && paved.Contains(t + new Vector2Int(0, c.y)))
                        found.Add(new RoundCorner { tile = t, toward = c, beside = t + new Vector2Int(c.x, 0), filled = true });

            // A corner takes the two edges that meet in it. Where two corners would take the same edge (two bends a tile
            // apart), their arcs would not meet: both stay square.
            var wanted = new Dictionary<(Vector2Int, bool), int>();
            foreach (var c in found)
                foreach (var e in new[] { Edge(c.tile, c.toward.x, 0), Edge(c.tile, 0, c.toward.y) })
                    wanted[e] = wanted.TryGetValue(e, out int n) ? n + 1 : 1;
            corners = found.FindAll(c => wanted[Edge(c.tile, c.toward.x, 0)] == 1 && wanted[Edge(c.tile, 0, c.toward.y)] == 1);
            foreach (var c in corners)
            {
                boundary.Remove(Edge(c.tile, c.toward.x, 0));
                boundary.Remove(Edge(c.tile, 0, c.toward.y));
            }
            edges = new List<GroundEdge>();
            foreach (var kv in boundary) edges.Add(new GroundEdge { cell = kv.Key.Item1, a = kv.Key.Item2, of = kv.Value });
        }
    }
}
