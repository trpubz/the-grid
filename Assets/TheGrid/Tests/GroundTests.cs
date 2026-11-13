using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TheGrid.Core.Tests
{
    // Where the lines of light on the ground run, and where a corner of lane or road is rounded.
    public class GroundTests
    {
        static HashSet<Vector2Int> Lane(params Vector2Int[] origins)
        {
            var paved = new HashSet<Vector2Int>();
            foreach (var o in origins)
                for (int c = o.x - Iso.LaneTiles; c < o.x + Iso.PlotTiles + Iso.LaneTiles; c++)
                    for (int r = o.y - Iso.LaneTiles; r < o.y + Iso.PlotTiles + Iso.LaneTiles; r++)
                        if (Iso.OnLane(o, c, r)) paved.Add(new Vector2Int(c, r));
            return paved;
        }

        static System.Func<Vector2Int, bool> Plots(params Vector2Int[] origins) =>
            t => origins.Any(o => t.x >= o.x && t.y >= o.y && t.x < o.x + Iso.PlotTiles && t.y < o.y + Iso.PlotTiles);

        // Every edge between what is paved and what is not is lit exactly once: by a straight line, or by the arc of the
        // one rounded corner it belongs to.
        static void AssertEveryEdgeIsLitOnce(HashSet<Vector2Int> paved, List<GroundEdge> edges, List<RoundCorner> corners, string what)
        {
            var lit = new Dictionary<(Vector2Int, bool), int>();
            void Count(Vector2Int cell, bool a) => lit[(cell, a)] = lit.TryGetValue((cell, a), out int n) ? n + 1 : 1;
            foreach (var e in edges) Count(e.cell, e.a);
            foreach (var c in corners)
            {
                Count(new Vector2Int(c.tile.x, c.toward.y < 0 ? c.tile.y : c.tile.y + 1), true);
                Count(new Vector2Int(c.toward.x > 0 ? c.tile.x : c.tile.x - 1, c.tile.y), false);
            }
            int boundary = 0;
            foreach (var t in paved)
                foreach (var d in new[] { new Vector2Int(0, -1), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(1, 0) })
                {
                    if (paved.Contains(t + d)) continue;
                    boundary++;
                    var edge = d.y != 0 ? (new Vector2Int(t.x, d.y < 0 ? t.y : t.y + 1), true) : (new Vector2Int(d.x > 0 ? t.x : t.x - 1, t.y), false);
                    Assert.IsTrue(lit.TryGetValue(edge, out int times) && times == 1, what + ": the edge of " + t + " toward " + d + " is lit " + times + " times");
                }
            Assert.AreEqual(boundary, lit.Count, what + ": a line or an arc lies where no edge is");
        }

        [Test]
        public void ASectorsLaneIsLitDownBothEdgesAndRoundedAtItsFourOuterCorners()
        {
            var o = new Vector2Int(0, 0);
            var paved = Lane(o);
            Ground.Outline(paved, Plots(o), out var edges, out var corners);
            // the four outer corners, each rounded away from the plot; the plot's own corners stay square
            CollectionAssert.AreEquivalent(
                new[] { (new Vector2Int(-2, -2), new Vector2Int(-1, -1)), (new Vector2Int(13, -2), new Vector2Int(1, -1)), (new Vector2Int(13, 13), new Vector2Int(1, 1)), (new Vector2Int(-2, 13), new Vector2Int(-1, 1)) },
                corners.Select(c => (c.tile, c.toward)).ToArray());
            Assert.IsTrue(corners.All(c => !c.filled && c.beside == c.tile), "an outer corner is cut from its own tile");
            // sixteen tiles a side outside less the two a corner takes at each end, and twelve a side inside
            Assert.AreEqual(4 * 16 - 8 + 4 * 12, edges.Count);
            Assert.IsTrue(edges.All(e => paved.Contains(e.of)), "every line belongs to a tile of the lane");
            AssertEveryEdgeIsLitOnce(paved, edges, corners, "one sector");
        }

        [Test]
        public void WhereARoadJoinsALaneTheLaneIsOpenAndTheMouthIsRoundedAndSoIsEveryBend()
        {
            // side by side in one region: a straight road across the gap
            Vector2Int a = new Vector2Int(0, 0), b = new Vector2Int(Iso.Pitch, 0);
            var paved = Lane(a, b);
            var road = Iso.RoadTiles(a, b);
            paved.UnionWith(road);
            Ground.Outline(paved, Plots(a, b), out var edges, out var corners);
            // no line across the road's mouth at either end
            foreach (var t in road)
                foreach (var e in edges) Assert.IsFalse(!e.a && (e.cell == new Vector2Int(a.x + 13, t.y) || e.cell == new Vector2Int(b.x - 3, t.y)), "a line lies across the road's mouth at " + e.cell);
            // each mouth has its two corners filled in; the two sectors keep their outer corners
            Assert.AreEqual(4, corners.Count(c => c.filled));
            Assert.AreEqual(8, corners.Count(c => !c.filled));
            Assert.IsTrue(corners.Where(c => c.filled).All(c => !paved.Contains(c.tile) && paved.Contains(c.beside)), "a filled corner is on open ground, beside what is paved");
            AssertEveryEdgeIsLitOnce(paved, edges, corners, "two sectors and a link");

            // across the open ground between two regions, not squarely: the road bends twice, each bend rounded outside and in
            b = new Vector2Int(30, 7);
            paved = Lane(a, b);
            paved.UnionWith(Iso.RoadTiles(a, b));
            Ground.Outline(paved, Plots(a, b), out edges, out corners);
            Assert.AreEqual(4 + 2, corners.Count(c => c.filled), "two mouths and the inside of two bends");
            Assert.AreEqual(8 + 2, corners.Count(c => !c.filled), "eight outer corners of the lanes and the outside of two bends");
            AssertEveryEdgeIsLitOnce(paved, edges, corners, "a road with two bends");
        }

        [Test]
        public void HoweverTwoSectorsStandEveryEdgeIsLitOnceAndNoPlotIsCutInto()
        {
            var a = new Vector2Int(0, 0);
            // every offset a drag can leave between two regions, by the tile: bends a tile apart among them
            for (int dx = 24; dx <= 34; dx += 5)
                for (int dy = -9; dy <= 9; dy++)
                    foreach (bool turned in new[] { false, true })
                    {
                        var b = turned ? new Vector2Int(dy, dx) : new Vector2Int(dx, dy);
                        var paved = Lane(a, b);
                        paved.UnionWith(Iso.RoadTiles(a, b));
                        var plots = Plots(a, b);
                        Ground.Outline(paved, plots, out var edges, out var corners);
                        AssertEveryEdgeIsLitOnce(paved, edges, corners, "b at " + b);
                        Assert.IsFalse(corners.Any(c => plots(c.tile)), "b at " + b + ": a corner of a plot is rounded away");
                        Assert.GreaterOrEqual(corners.Count(c => !c.filled), 8, "b at " + b);
                    }
        }
    }
}
