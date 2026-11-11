using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TheGrid.Core.Tests
{
    public class IsoTests
    {
        static readonly Building[] All = (Building[])Enum.GetValues(typeof(Building));

        [Test]
        public void ATileIsSixtyFourByThirtyTwo()
        {
            Assert.AreEqual(new Vector2(0, 0), Iso.TileCentre(0, 0));
            Assert.AreEqual(new Vector2(32, -16), Iso.TileCentre(1, 0));
            Assert.AreEqual(new Vector2(-32, -16), Iso.TileCentre(0, 1));
            Assert.AreEqual(new Vector2(0, -32), Iso.TileCentre(1, 1));
        }

        static IEnumerable<Vector2Int> Footprint(Building b)
        {
            var t = Iso.BuildingTile(b);
            for (int c = 0; c < 2; c++) for (int r = 0; r < 2; r++) yield return new Vector2Int(t.x + c, t.y + r);
        }

        [Test]
        public void TenBuildingsFitThePlotAndShareNoTile()
        {
            Assert.AreEqual(10, All.Length);
            var tiles = All.SelectMany(Footprint).ToList();
            Assert.AreEqual(40, tiles.Distinct().Count());
            Assert.IsTrue(tiles.All(t => t.x >= 0 && t.y >= 0 && t.x < Iso.PlotTiles && t.y < Iso.PlotTiles));
        }

        [Test]
        public void PlotsShareNoTile()
        {
            var cells = new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, -1) };
            var origins = cells.Select(Iso.PlotOrigin).ToList();
            Assert.AreEqual(new Vector2Int(0, 0), origins[0]);
            Assert.AreEqual(new Vector2Int(12, 0), origins[1]);
            Assert.AreEqual(new Vector2Int(0, 12), origins[2]);
            Assert.AreEqual(new Vector2Int(-12, -12), origins[3]);
        }

        [Test]
        public void EveryRiderHasItsOwnSpot()
        {
            var cell = new Vector2Int(1, -1);
            var seen = new HashSet<Vector2>();
            foreach (var b in All)
                for (int slot = 0; slot < 50; slot++)
                    Assert.IsTrue(seen.Add(Iso.RiderSpot(cell, b, slot)), b + " slot " + slot + " shares a spot");
            // a spot moves with its plot
            Assert.AreNotEqual(Iso.RiderSpot(new Vector2Int(0, 0), Building.Garage, 0), Iso.RiderSpot(cell, Building.Garage, 0));
        }

        [Test]
        public void BoundsCoverThePlots()
        {
            var one = Iso.Bounds(new[] { new Vector2Int(0, 0) });
            Assert.AreEqual(768f, one.width, .01f);
            Assert.AreEqual(384f, one.height, .01f);
            Assert.AreEqual(0f, one.center.x, .01f);                      // plot 0 is centred under its north corner
            var two = Iso.Bounds(new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) });
            Assert.AreEqual(768f + 384f, two.width, .01f);                // the neighbour sits half a plot across and half a plot down
            Assert.AreEqual(384f + 192f, two.height, .01f);
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
