using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TheGrid.Core.Tests
{
    public class IsoTests
    {
        [Test]
        public void ATileIsSixtyFourByThirtyTwo()
        {
            Assert.AreEqual(new Vector2(0, 0), Iso.TileCentre(0, 0));
            Assert.AreEqual(new Vector2(32, -16), Iso.TileCentre(1, 0));
            Assert.AreEqual(new Vector2(-32, -16), Iso.TileCentre(0, 1));
            Assert.AreEqual(new Vector2(0, -32), Iso.TileCentre(1, 1));
        }

        [Test]
        public void PlotsShareNoTile()
        {
            var cells = new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, -1) };
            var origins = cells.Select(Iso.PlotOrigin).ToList();
            Assert.AreEqual(19, Iso.Pitch);   // a plot, the lane on each side of it, and three tiles of open ground to the next
            Assert.AreEqual(new Vector2Int(0, 0), origins[0]);
            Assert.AreEqual(new Vector2Int(19, 0), origins[1]);
            Assert.AreEqual(new Vector2Int(0, 19), origins[2]);
            Assert.AreEqual(new Vector2Int(-19, -19), origins[3]);
        }

        static IEnumerable<Vector2Int> PlotTiles(Vector2Int cell)
        {
            var o = Iso.PlotOrigin(cell);
            for (int c = 0; c < Iso.PlotTiles; c++) for (int r = 0; r < Iso.PlotTiles; r++) yield return new Vector2Int(o.x + c, o.y + r);
        }

        [Test]
        public void APathRunsAlongAllFourSidesOfASectorAndRoundItsCorners()
        {
            var cell = new Vector2Int(2, -1);
            var o = Iso.PlotOrigin(cell);
            int far = Iso.PlotTiles + Iso.LaneTiles - 1;   // the last lane tile beyond the plot
            foreach (var t in new[] { new Vector2Int(5, -1), new Vector2Int(5, Iso.PlotTiles), new Vector2Int(-1, 5), new Vector2Int(Iso.PlotTiles, 5),      // beside each side
                                      new Vector2Int(-Iso.LaneTiles, -Iso.LaneTiles), new Vector2Int(far, far), new Vector2Int(-Iso.LaneTiles, far) })                  // corners
                Assert.IsTrue(Iso.OnLane(cell, o.x + t.x, o.y + t.y), t + " should be lane");
            foreach (var t in new[] { new Vector2Int(0, 0), new Vector2Int(11, 11), new Vector2Int(5, 5), new Vector2Int(-Iso.LaneTiles - 1, 5), new Vector2Int(5, far + 1) })
                Assert.IsFalse(Iso.OnLane(cell, o.x + t.x, o.y + t.y), t + " should not be lane");
            // the lane is as wide as LaneTiles all the way round: a 16 by 16 square less the 12 by 12 plot
            int count = 0;
            for (int c = -5; c < 20; c++) for (int r = -5; r < 20; r++) if (Iso.OnLane(cell, o.x + c, o.y + r)) count++;
            Assert.AreEqual(16 * 16 - 12 * 12, count);
        }

        [Test]
        public void NeighbouringSectorsStandApartAndALinkJoinsThem()
        {
            Vector2Int a = new Vector2Int(0, 0), right = new Vector2Int(1, 0), below = new Vector2Int(0, 1);
            int ring = Iso.PlotTiles + Iso.LaneTiles;       // the first tile beyond a sector's own lane
            // between a sector and the next: tiles that are neither's lane
            for (int gap = 0; gap < Iso.GapTiles; gap++)
                for (int i = -Iso.LaneTiles; i < ring; i++)
                {
                    Assert.IsFalse(Iso.OnLane(a, ring + gap, i) || Iso.OnLane(right, ring + gap, i), "open ground beside a sector");
                    Assert.IsFalse(Iso.OnLane(a, i, ring + gap) || Iso.OnLane(below, i, ring + gap), "open ground in front of a sector");
                    // and one link across it each way, two tiles wide: at the crossing in front, at the same count down the side
                    bool link = i == Iso.Crossing || i == Iso.Crossing + 1;
                    Assert.AreEqual(link, Iso.OnLink(ring + gap, i), "beside, tile " + i);
                    Assert.AreEqual(link, Iso.OnLink(i, ring + gap), "in front, tile " + i);
                }
            Assert.IsFalse(Iso.OnLink(Iso.Crossing, Iso.PlotTiles), "a lane is not a link");
            Assert.IsFalse(Iso.OnLink(3, 3), "nor is a plot");
            Assert.IsTrue(Iso.OnLink(ring - Iso.Pitch, Iso.Crossing - 2 * Iso.Pitch), "the same on the far side of the map's middle");

            var cells = Enumerable.Range(0, 40).Select(Plots.Cell).ToList();
            var plots = new HashSet<Vector2Int>(cells.SelectMany(PlotTiles));
            Assert.AreEqual(40 * Iso.PlotTiles * Iso.PlotTiles, plots.Count, "plots share no tile");
            var lanes = new HashSet<Vector2Int>();
            foreach (var cell in cells)
            {
                var o = Iso.PlotOrigin(cell);
                for (int c = -Iso.LaneTiles; c < ring; c++)
                    for (int r = -Iso.LaneTiles; r < ring; r++)
                    {
                        var t = new Vector2Int(o.x + c, o.y + r);
                        if (!Iso.OnLane(cell, t.x, t.y)) continue;
                        Assert.IsFalse(plots.Contains(t), "a lane runs through a plot at " + t);
                        Assert.IsTrue(lanes.Add(t), "two sectors share the lane tile " + t);
                        Assert.IsFalse(Iso.OnLink(t.x, t.y), "a lane tile is also a link at " + t);
                    }
            }
        }

        [Test]
        public void BoundsCoverThePlots()
        {
            // a plot and the lane round it: 16 tiles each way
            var one = Iso.Bounds(new[] { new Vector2Int(0, 0) });
            Assert.AreEqual(1024f, one.width, .01f);
            Assert.AreEqual(512f, one.height, .01f);
            Assert.AreEqual(0f, one.center.x, .01f);                      // plot 0 is centred under its north corner
            var two = Iso.Bounds(new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) });
            Assert.AreEqual(1024f + 608f, two.width, .01f);               // the neighbour sits one pitch along: 19 tiles, half a tile's width across each
            Assert.AreEqual(512f + 304f, two.height, .01f);
            Assert.AreEqual(0f, Iso.Bounds(new Vector2Int[0]).width);
        }

        [Test]
        public void ZoomLevelsDoubleAndTheViewOpensAtTheClosestThatFits()
        {
            CollectionAssert.AreEqual(new[] { .25f, .5f, 1f, 2f, 4f }, Iso.Zoom);
            Assert.AreEqual(0, Iso.ClampLevel(-1));
            Assert.AreEqual(4, Iso.ClampLevel(9));
            Assert.AreEqual(2, Iso.ClampLevel(2));
            var onePlot = new Rect(0, 0, 768, 384);
            Assert.AreEqual(3, Iso.FitLevel(2000, 1250, onePlot));        // 2x is 1536 by 768; 4x would be 3072 wide
            Assert.AreEqual(1, Iso.FitLevel(2000, 1250, new Rect(0, 0, 2304, 1152)));   // nine plots fit at half size
            Assert.AreEqual(0, Iso.FitLevel(300, 200, onePlot));          // a small view: the farthest level
            Assert.AreEqual(2, Iso.FitLevel(2000, 1250, new Rect(0, 0, 0, 0)));        // nothing to show: actual size
        }
    }
}
