using System.Collections.Generic;
using UnityEngine;

namespace TheGrid.Core
{
    // The roads of the map, and the way a courier finds along them.
    public static class Roads
    {
        // Every road, as the origins of the two sectors it joins, told from the one to the left or behind (the way
        // Iso.RoadTiles lays it). First the links: one between every two sectors that stand side by side in one region.
        // Then as few more as it takes to join everything up, shortest first: between regions, and to a sector that
        // stands alone in its region because the owner put it there or the one beside it left.
        // A road that would run through a third sector is passed over while another will do.
        public static List<(Vector2Int from, Vector2Int to)> All(WorldModel world)
        {
            var roads = new List<(Vector2Int, Vector2Int)>();
            var sectors = world.sectors;
            var group = new int[sectors.Count];
            for (int i = 0; i < group.Length; i++) group[i] = i;
            int Find(int i) => group[i] == i ? i : group[i] = Find(group[i]);
            void Join(int i, int j)
            {
                bool forward = Iso.RoadRunsFrom(sectors[i].origin, sectors[j].origin);
                roads.Add(forward ? (sectors[i].origin, sectors[j].origin) : (sectors[j].origin, sectors[i].origin));
                group[Find(i)] = Find(j);
            }

            for (int i = 0; i < sectors.Count; i++)
                for (int j = 0; j < sectors.Count; j++)
                {
                    var step = sectors[j].cell - sectors[i].cell;
                    if (sectors[i].region == sectors[j].region && (step == Vector2Int.right || step == Vector2Int.up)) Join(i, j);
                }

            var pairs = new List<(int far, int i, int j)>();
            for (int i = 0; i < sectors.Count; i++)
                for (int j = i + 1; j < sectors.Count; j++)
                {
                    var d = sectors[j].origin - sectors[i].origin;
                    pairs.Add((Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y)), i, j));
                }
            pairs.Sort();
            bool Clear(int i, int j)
            {
                foreach (var t in Iso.RoadTiles(sectors[i].origin, sectors[j].origin))
                    foreach (var s in sectors)
                        if (Iso.Within(s.origin, t.x, t.y)) return false;
                return true;
            }
            // shortcut: when every road that would join two parts of the map runs through some sector, the shortest is
            // laid all the same, through it. Route such a road round if owners arrange maps that tightly.
            for (int pass = 0; pass < 2; pass++)
                foreach (var (_, i, j) in pairs)
                    if (Find(i) != Find(j) && (pass == 1 || Clear(i, j))) Join(i, j);
            return roads;
        }

        // The sectors a courier passes from one to another, by their origins, both included: each is joined to the one
        // before by a road. The fewest there can be. Null when either is not a sector of the map.
        public static List<Vector2Int> Hops(WorldModel world, Vector2Int from, Vector2Int to)
        {
            var next = new Dictionary<Vector2Int, List<Vector2Int>>();
            foreach (var s in world.sectors) next[s.origin] = new List<Vector2Int>();
            if (!next.ContainsKey(from) || !next.ContainsKey(to)) return null;
            foreach (var (a, b) in All(world))
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
