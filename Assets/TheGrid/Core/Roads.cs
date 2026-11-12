using System.Collections.Generic;
using UnityEngine;

namespace TheGrid.Core
{
    // The roads between regions, and the way a courier finds through the map.
    public static class Roads
    {
        static readonly Vector2Int[] Sides = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        // One road for every two regions that stand side by side: straight across the open ground, from a sector of one
        // to the sector of the other in the same row or column, the two that are nearest each other. `from` is the one
        // behind or to the left, so a road is laid as a link is: to the right, or to the front.
        // A region none of whose sectors faces one of its neighbour's has no road to it.
        public static List<(Vector2Int from, Vector2Int to)> Between(WorldModel world)
        {
            var roads = new List<(Vector2Int, Vector2Int)>();
            for (int a = 0; a < world.regions.Count; a++)
                for (int b = 0; b < world.regions.Count; b++)
                {
                    var step = world.regions[b].at - world.regions[a].at;
                    if (step != Vector2Int.right && step != Vector2Int.up) continue;
                    int nearest = int.MaxValue;
                    Vector2Int from = default, to = default;
                    foreach (var s in world.sectors)
                        foreach (var t in world.sectors)
                        {
                            if (s.region != a || t.region != b) continue;
                            var d = t.cell - s.cell;   // on the one grid all sectors still share
                            int along = step.x == 1 ? d.x : d.y, across = step.x == 1 ? d.y : d.x;
                            if (across != 0 || along <= 0 || along >= nearest) continue;
                            nearest = along;
                            from = s.origin;
                            to = t.origin;
                        }
                    if (nearest != int.MaxValue) roads.Add((from, to));
                }
            return roads;
        }

        // The sectors a courier passes from one to another, both included: each stands beside the one before, where a
        // link joins them, or at the far end of a road from it. The fewest there can be. Null when no way joins the two.
        public static List<Vector2Int> Hops(WorldModel world, Vector2Int from, Vector2Int to)
        {
            var next = new Dictionary<Vector2Int, List<Vector2Int>>();
            foreach (var s in world.sectors) next[s.origin] = new List<Vector2Int>();
            if (!next.ContainsKey(from) || !next.ContainsKey(to)) return null;
            foreach (var s in world.sectors)
                foreach (var side in Sides)
                    if (next.ContainsKey(s.origin + side * Iso.Pitch)) next[s.origin].Add(s.origin + side * Iso.Pitch);
            foreach (var (a, b) in Between(world))
            {
                next[a].Add(b);
                next[b].Add(a);
            }

            var cameFrom = new Dictionary<Vector2Int, Vector2Int> { [from] = from };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(from);
            while (queue.Count > 0 && !cameFrom.ContainsKey(to))
            {
                var at = queue.Dequeue();
                foreach (var n in next[at])
                    if (!cameFrom.ContainsKey(n))
                    {
                        cameFrom[n] = at;
                        queue.Enqueue(n);
                    }
            }
            if (!cameFrom.ContainsKey(to)) return null;
            var hops = new List<Vector2Int> { to };
            while (hops[hops.Count - 1] != from) hops.Add(cameFrom[hops[hops.Count - 1]]);
            hops.Reverse();
            return hops;
        }
    }
}
