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
                Assert.IsTrue(Iso.OnLane(Iso.PlotOrigin(cell), o.x + t.x, o.y + t.y), t + " should be lane");
            foreach (var t in new[] { new Vector2Int(0, 0), new Vector2Int(11, 11), new Vector2Int(5, 5), new Vector2Int(-Iso.LaneTiles - 1, 5), new Vector2Int(5, far + 1) })
                Assert.IsFalse(Iso.OnLane(Iso.PlotOrigin(cell), o.x + t.x, o.y + t.y), t + " should not be lane");
            // the lane is as wide as LaneTiles all the way round: a 16 by 16 square less the 12 by 12 plot
            int count = 0;
            for (int c = -5; c < 20; c++) for (int r = -5; r < 20; r++) if (Iso.OnLane(Iso.PlotOrigin(cell), o.x + c, o.y + r)) count++;
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
                    Assert.IsFalse(Iso.OnLane(Iso.PlotOrigin(a), ring + gap, i) || Iso.OnLane(Iso.PlotOrigin(right), ring + gap, i), "open ground beside a sector");
                    Assert.IsFalse(Iso.OnLane(Iso.PlotOrigin(a), i, ring + gap) || Iso.OnLane(Iso.PlotOrigin(below), i, ring + gap), "open ground in front of a sector");
                }
            // and one link across it each way, two tiles wide: at the crossing in front, at the same count down the side
            var beside = new HashSet<Vector2Int>(Iso.RoadTiles(Iso.PlotOrigin(a), Iso.PlotOrigin(right)));
            var inFront = new HashSet<Vector2Int>(Iso.RoadTiles(Iso.PlotOrigin(below), Iso.PlotOrigin(a)));   // either way round
            Assert.AreEqual(2 * Iso.GapTiles, beside.Count);
            Assert.AreEqual(2 * Iso.GapTiles, inFront.Count);
            for (int gap = 0; gap < Iso.GapTiles; gap++)
                for (int k = 0; k < 2; k++)
                {
                    Assert.IsTrue(beside.Contains(new Vector2Int(ring + gap, Iso.Crossing + k)), "beside");
                    Assert.IsTrue(inFront.Contains(new Vector2Int(Iso.Crossing + k, ring + gap)), "in front");
                }

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
                        if (!Iso.OnLane(Iso.PlotOrigin(cell), t.x, t.y)) continue;
                        Assert.IsFalse(plots.Contains(t), "a lane runs through a plot at " + t);
                        Assert.IsTrue(lanes.Add(t), "two sectors share the lane tile " + t);
                    }
            }
        }

        [Test]
        public void ThePointerRestingAtAnEdgeOfTheMapMovesItThatWay()
        {
            var view = new Rect(8, 32, 1000, 600);   // the map, in the window's points: y runs down
            Assert.AreEqual(Vector2.zero, Iso.EdgePan(view, new Vector2(500, 300), 28));
            // to the left and the right; up is toward the top of the window
            Assert.AreEqual(new Vector2(-1, 0), Iso.EdgePan(view, new Vector2(13, 300), 28));
            Assert.AreEqual(new Vector2(1, 0), Iso.EdgePan(view, new Vector2(1003, 300), 28));
            Assert.AreEqual(new Vector2(0, 1), Iso.EdgePan(view, new Vector2(500, 37), 28));
            Assert.AreEqual(new Vector2(0, -1), Iso.EdgePan(view, new Vector2(500, 627), 28));
            Assert.AreEqual(new Vector2(-1, 1), Iso.EdgePan(view, new Vector2(13, 37), 28), "a corner moves it both ways");
            // only within the edge
            Assert.AreEqual(Vector2.zero, Iso.EdgePan(view, new Vector2(8 + 28, 300), 28));
            Assert.AreNotEqual(Vector2.zero, Iso.EdgePan(view, new Vector2(8 + 27.5f, 300), 28));
            // and only while the pointer is on the map: off it, in the bar above or outside the window, nothing moves
            Assert.AreEqual(Vector2.zero, Iso.EdgePan(view, new Vector2(2, 300), 28));
            Assert.AreEqual(Vector2.zero, Iso.EdgePan(view, new Vector2(500, 20), 28));
            Assert.AreEqual(Vector2.zero, Iso.EdgePan(view, new Vector2(1200, 300), 28));
            Assert.AreEqual(Vector2.zero, Iso.EdgePan(view, new Vector2(500, 640), 28));
        }

        [Test]
        public void TheWheelZoomsOneLevelAtATimeHoweverItIsTurned()
        {
            float pending = 0;
            // a notch of a mouse's wheel is a level: in for a turn away, out for a turn toward
            Assert.AreEqual(1, Iso.WheelStep(ref pending, -1f, 1f));
            Assert.AreEqual(-1, Iso.WheelStep(ref pending, 1f, 1f));
            Assert.AreEqual(1, Iso.WheelStep(ref pending, -5f, 1f), "a hard turn is still one level");
            Assert.AreEqual(0f, pending);
            // two fingers on a trackpad send many small turns: they add up to a level
            for (int i = 0; i < 3; i++) Assert.AreEqual(0, Iso.WheelStep(ref pending, -.3f, 1f));
            Assert.AreEqual(1, Iso.WheelStep(ref pending, -.3f, 1f));
            // and then the map rests: what is turned in the next moment is dropped, or one swipe would run through every level
            Assert.AreEqual(0, Iso.WheelStep(ref pending, -3f, .05f));
            Assert.AreEqual(0f, pending);
            Assert.AreEqual(1, Iso.WheelStep(ref pending, -3f, Iso.WheelRest));
            // turning back starts afresh: nothing left over has to be undone first
            Assert.AreEqual(0, Iso.WheelStep(ref pending, -.6f, 1f));
            Assert.AreEqual(0, Iso.WheelStep(ref pending, .6f, 1f));
            Assert.AreEqual(-1, Iso.WheelStep(ref pending, .5f, 1f));
        }

        [Test]
        public void ARoadBetweenTwoSectorsThatDoNotFaceEachOtherSquarelyBendsTwiceInTheOpenGroundBetween()
        {
            Vector2Int a = new Vector2Int(0, 0), b = new Vector2Int(30, 7);   // b to the right, seven tiles further forward
            var tiles = new HashSet<Vector2Int>(Iso.RoadTiles(a, b));
            CollectionAssert.AreEquivalent(tiles, Iso.RoadTiles(b, a));
            foreach (var t in tiles)
            {
                Assert.IsFalse(Iso.Within(a, t.x, t.y) || Iso.Within(b, t.x, t.y), "the road runs into a sector at " + t);
                Assert.IsTrue(t.x >= 14 && t.x <= 27, "the road leaves the ground between the two at " + t);
            }
            // it leaves a's lane at a's crossing rows and meets b's at b's
            Assert.IsTrue(tiles.Contains(new Vector2Int(14, Iso.Crossing)) && tiles.Contains(new Vector2Int(14, Iso.Crossing + 1)));
            Assert.IsTrue(tiles.Contains(new Vector2Int(27, 7 + Iso.Crossing)) && tiles.Contains(new Vector2Int(27, 7 + Iso.Crossing + 1)));
            Assert.IsFalse(tiles.Contains(new Vector2Int(27, Iso.Crossing)));

            // a courier rides it down the middle: out of a's side, along, across, along, into b's side, and round to b's crossing
            var route = Iso.Route(new[] { a, b });
            CollectionAssert.AreEqual(new[]
            {
                new Vector2(5.5f, 12.5f), new Vector2(12.5f, 12.5f), new Vector2(12.5f, 5.5f), new Vector2(20.5f, 5.5f), new Vector2(20.5f, 12.5f), new Vector2(28.5f, 12.5f), new Vector2(28.5f, 19.5f), new Vector2(35.5f, 19.5f),
            }, route);
            // and back the same way
            var back = Iso.Route(new[] { b, a });
            CollectionAssert.Contains(back, new Vector2(20.5f, 12.5f));
            CollectionAssert.Contains(back, new Vector2(20.5f, 5.5f));
            Assert.AreEqual(new Vector2(5.5f, 12.5f), back[back.Count - 1]);

            // in front, the same turned a quarter
            var front = new HashSet<Vector2Int>(Iso.RoadTiles(a, new Vector2Int(-4, 26)));
            Assert.IsTrue(front.Contains(new Vector2Int(Iso.Crossing, 14)) && front.Contains(new Vector2Int(-4 + Iso.Crossing, 23)));
            foreach (var t in front) Assert.IsTrue(t.y >= 14 && t.y <= 23, "the road leaves the ground between the two at " + t);
            Assert.AreEqual(0, Iso.Route(new[] { a }).Count, "within one sector there is no way to ride");
        }

        [Test]
        public void ADragOnTheScreenIsSoManyTilesOfTheMap()
        {
            Assert.AreEqual(new Vector2Int(3, -2), Iso.Tiles(Iso.Point(3, -2)));
            Assert.AreEqual(new Vector2Int(0, 0), Iso.Tiles(new Vector2(10, 5)));          // less than half a tile
            Assert.AreEqual(new Vector2Int(-7, 12), Iso.Tiles(Iso.Point(-7, 12) + new Vector2(6, -3)));
        }

        [Test]
        public void TwoRegionsAreApartWhenEightTilesLieBetweenTheirLanes()
        {
            Assert.AreEqual(8, Iso.RegionGap);
            Assert.IsTrue(Iso.Apart(new Vector2Int(0, 0), new Vector2Int(24, 0)));    // lane to 13, eight tiles, lane from 22
            Assert.IsFalse(Iso.Apart(new Vector2Int(0, 0), new Vector2Int(23, 0)));
            Assert.IsTrue(Iso.Apart(new Vector2Int(0, 0), new Vector2Int(-3, -24)));
            Assert.IsFalse(Iso.Apart(new Vector2Int(0, 0), new Vector2Int(-20, 20)));
        }

        [Test]
        public void BoundsCoverThePlots()
        {
            // a plot and the lane round it: 16 tiles each way
            var one = Iso.Bounds(new[] { new Vector2Int(0, 0) });
            Assert.AreEqual(1024f, one.width, .01f);
            Assert.AreEqual(512f, one.height, .01f);
            Assert.AreEqual(0f, one.center.x, .01f);                      // plot 0 is centred under its north corner
            var two = Iso.Bounds(new[] { new Vector2Int(0, 0), new Vector2Int(Iso.Pitch, 0) });
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
