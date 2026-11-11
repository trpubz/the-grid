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

        static readonly Vector2Int LowerLeft = new Vector2Int(0, 1), LowerRight = new Vector2Int(1, 0), UpperLeft = new Vector2Int(-1, 0), UpperRight = new Vector2Int(0, -1);

        [Test]
        public void ThreeCitiesInAnLShareOneNotch()
        {
            // The owner's example: alpha, beta beside it, and a third below beta. The first and the third each touch only beta,
            // and the empty cell between them is where both get their road.
            Vector2Int alpha = new Vector2Int(0, 0), beta = new Vector2Int(1, 0), third = new Vector2Int(1, 1), notch = new Vector2Int(0, 1);
            var roads = Iso.Thoroughfares(new[] { alpha, beta, third });
            Assert.AreEqual(2, roads.Count);
            CollectionAssert.Contains(roads, (notch, alpha - notch));   // along alpha's lower-left side
            CollectionAssert.Contains(roads, (notch, third - notch));   // along the third city's upper-left side: its left
            Assert.AreEqual(UpperLeft, notch - third);
        }

        [Test]
        public void ACityOnItsOwnGetsRoadsOnItsTwoFrontSides()
        {
            var city = new Vector2Int(3, -2);
            var roads = Iso.Thoroughfares(new[] { city });
            Assert.AreEqual(2, roads.Count);
            CollectionAssert.Contains(roads, (city + LowerLeft, -LowerLeft));
            CollectionAssert.Contains(roads, (city + LowerRight, -LowerRight));
        }

        [Test]
        public void CitiesWithTwoNeighboursNeedNoRoads()
        {
            Assert.AreEqual(0, Iso.Thoroughfares(new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(0, 1) }).Count);
            Assert.AreEqual(0, Iso.Thoroughfares(new Vector2Int[0]).Count);
        }

        [Test]
        public void HoweverManyCitiesEachHasCompanyOnTwoSidesAndNoRoadRunsThroughACity()
        {
            for (int n = 1; n <= 40; n++)
            {
                var cities = Enumerable.Range(0, n).Select(Plots.Cell).ToList();
                var roads = Iso.Thoroughfares(cities);
                CollectionAssert.AreEqual(roads, Iso.Thoroughfares(cities), "the same cities give the same roads");
                foreach (var r in roads)
                {
                    CollectionAssert.DoesNotContain(cities, r.cell, n + " cities: a road runs through a city");
                    CollectionAssert.Contains(cities, r.cell + r.toCity, n + " cities: a road serves no city");
                }
                foreach (var city in cities)
                {
                    int company = Iso.Sides.Count(side => cities.Contains(city + side) || roads.Contains((city + side, -side)));
                    Assert.GreaterOrEqual(company, 2, n + " cities: " + city + " is stranded");
                    Assert.AreEqual(Math.Max(0, 2 - Iso.Sides.Count(side => cities.Contains(city + side))), roads.Count(r => r.cell + r.toCity == city), n + " cities: " + city + " has roads it does not need");
                }
            }
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
