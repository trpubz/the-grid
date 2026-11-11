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
            Assert.AreEqual(14, Iso.Pitch);   // a plot and the path beside it
            Assert.AreEqual(new Vector2Int(0, 0), origins[0]);
            Assert.AreEqual(new Vector2Int(14, 0), origins[1]);
            Assert.AreEqual(new Vector2Int(0, 14), origins[2]);
            Assert.AreEqual(new Vector2Int(-14, -14), origins[3]);
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

        static IEnumerable<Vector2Int> PlotTiles(Vector2Int cell)
        {
            var o = Iso.PlotOrigin(cell);
            for (int c = 0; c < Iso.PlotTiles; c++) for (int r = 0; r < Iso.PlotTiles; r++) yield return new Vector2Int(o.x + c, o.y + r);
        }

        [Test]
        public void APathRunsAlongAllFourSidesOfACityAndRoundItsCorners()
        {
            var cell = new Vector2Int(2, -1);
            var o = Iso.PlotOrigin(cell);
            int far = Iso.PlotTiles + Iso.PathTiles - 1;   // the last path tile beyond the plot
            foreach (var t in new[] { new Vector2Int(5, -1), new Vector2Int(5, Iso.PlotTiles), new Vector2Int(-1, 5), new Vector2Int(Iso.PlotTiles, 5),      // beside each side
                                      new Vector2Int(-Iso.PathTiles, -Iso.PathTiles), new Vector2Int(far, far), new Vector2Int(-Iso.PathTiles, far) })                  // corners
                Assert.IsTrue(Iso.OnPath(cell, o.x + t.x, o.y + t.y), t + " should be path");
            foreach (var t in new[] { new Vector2Int(0, 0), new Vector2Int(11, 11), new Vector2Int(5, 5), new Vector2Int(-Iso.PathTiles - 1, 5), new Vector2Int(5, far + 1) })
                Assert.IsFalse(Iso.OnPath(cell, o.x + t.x, o.y + t.y), t + " should not be path");
            // the path is as wide as PathTiles all the way round: a 16 by 16 square less the 12 by 12 plot
            int count = 0;
            for (int c = -5; c < 20; c++) for (int r = -5; r < 20; r++) if (Iso.OnPath(cell, o.x + c, o.y + r)) count++;
            Assert.AreEqual(16 * 16 - 12 * 12, count);
        }

        [Test]
        public void NeighbouringCitiesShareThePathBetweenThemAndNoPathRunsThroughAPlot()
        {
            Vector2Int a = new Vector2Int(0, 0), right = new Vector2Int(1, 0), below = new Vector2Int(0, 1);
            for (int i = 0; i < Iso.PlotTiles; i++)
                for (int lane = 0; lane < Iso.PathTiles; lane++)
                {
                    Assert.IsTrue(Iso.OnPath(a, Iso.PlotTiles + lane, i) && Iso.OnPath(right, Iso.PlotTiles + lane, i), "between a city and the one beside it");
                    Assert.IsTrue(Iso.OnPath(a, i, Iso.PlotTiles + lane) && Iso.OnPath(below, i, Iso.PlotTiles + lane), "between a city and the one below it");
                }

            var cells = Enumerable.Range(0, 40).Select(Plots.Cell).ToList();
            var plots = new HashSet<Vector2Int>(cells.SelectMany(PlotTiles));
            Assert.AreEqual(40 * Iso.PlotTiles * Iso.PlotTiles, plots.Count, "plots share no tile");
            foreach (var cell in cells)
            {
                var o = Iso.PlotOrigin(cell);
                for (int c = -Iso.PathTiles; c < Iso.PlotTiles + Iso.PathTiles; c++)
                    for (int r = -Iso.PathTiles; r < Iso.PlotTiles + Iso.PathTiles; r++)
                        Assert.AreEqual(!plots.Contains(new Vector2Int(o.x + c, o.y + r)), Iso.OnPath(cell, o.x + c, o.y + r), cell + ": tile " + c + ", " + r);
            }
        }

        static readonly Structure[] Structures = (Structure[])Enum.GetValues(typeof(Structure));
        static readonly Place[] Places = (Place[])Enum.GetValues(typeof(Place));

        static IEnumerable<Vector2Int> TilesOf(Structure s)
        {
            var f = Iso.Footprint(s);
            for (int c = f.xMin; c < f.xMax; c++) for (int r = f.yMin; r < f.yMax; r++) yield return new Vector2Int(c, r);
        }

        [Test]
        public void StructuresFitThePlotAndShareNoTile()
        {
            Assert.AreEqual(7, Structures.Length);
            var tiles = Structures.SelectMany(TilesOf).ToList();
            Assert.AreEqual(59, tiles.Count);                      // 3x3 three times, 4x3 twice, 2x2 twice
            Assert.AreEqual(59, tiles.Distinct().Count());
            Assert.IsTrue(tiles.All(t => t.x >= 0 && t.y >= 0 && t.x < Iso.PlotTiles && t.y < Iso.PlotTiles));
            Assert.AreEqual(new RectInt(0, 0, 3, 3), Iso.Footprint(Structure.ServerTower));
            Assert.AreEqual(new RectInt(8, 5, 4, 3), Iso.Footprint(Structure.Workshop));
            Assert.AreEqual(new RectInt(5, 5, 2, 2), Iso.Footprint(Structure.Fountain));
        }

        [Test]
        public void AStructureStandsAtTheCentreOfItsFootprint()
        {
            var cell = new Vector2Int(1, -2);
            var o = Iso.PlotOrigin(cell);
            Assert.AreEqual(Iso.Point(o.x + 1f, o.y + 1f), Iso.StructureSpot(cell, Structure.ServerTower));     // tiles 0..2 each way
            Assert.AreEqual(Iso.Point(o.x + 9.5f, o.y + 6f), Iso.StructureSpot(cell, Structure.Workshop));      // tiles 8..11 by 5..7
            Assert.AreEqual(Iso.Point(o.x + 5.5f, o.y + 5.5f), Iso.StructureSpot(cell, Structure.Fountain));
        }

        [Test]
        public void FiftyBotsAtEveryPlaceEachHaveTheirOwnSpot()
        {
            var cell = new Vector2Int(2, 1);
            var seen = new HashSet<Vector2>();
            foreach (var place in Places)
                for (int slot = 0; slot < 50; slot++)
                    Assert.IsTrue(seen.Add(Iso.BotSpot(cell, place, slot)), place + " slot " + slot + " shares a spot");
            Assert.AreEqual(400, seen.Count);
        }

        [Test]
        public void TheFirstSixOfAPlaceStandOnNoStructure()
        {
            var cell = new Vector2Int(0, 0);
            var built = new HashSet<Vector2Int>(Structures.SelectMany(TilesOf));
            foreach (var place in Places)
                for (int i = 0; i < 6; i++)
                {
                    int slot = place == Place.Workshop ? i + 2 : i;     // the workshop's first two stand inside it, on purpose
                    var at = Iso.BotSpot(cell, place, slot);
                    // back from pixels to the tile under the spot
                    float col = (at.x / (Iso.TileW / 2) - at.y / (Iso.TileH / 2)) / 2, row = (-at.y / (Iso.TileH / 2) - at.x / (Iso.TileW / 2)) / 2;
                    var tile = new Vector2Int(Mathf.RoundToInt(col), Mathf.RoundToInt(row));
                    Assert.IsFalse(built.Contains(tile), place + " slot " + slot + " stands on a structure at " + tile);
                }
        }

        [Test]
        public void TheWorkshopsFirstTwoStandInsideIt()
        {
            var cell = new Vector2Int(-1, 3);
            var o = Iso.PlotOrigin(cell);
            Assert.AreEqual(Iso.Point(o.x + 9.5f, o.y + 6.0f), Iso.BotSpot(cell, Place.Workshop, 0));    // at the terminal
            Assert.AreEqual(Iso.Point(o.x + 10.5f, o.y + 6.6f), Iso.BotSpot(cell, Place.Workshop, 1));   // at the bench
            Assert.AreEqual(Iso.Point(o.x + 8.7f, o.y + 8.25f), Iso.BotSpot(cell, Place.Workshop, 2));   // the rest wait outside
        }

        [Test]
        public void ASpotMovesWithItsSector()
        {
            Vector2Int a = new Vector2Int(0, 0), b = new Vector2Int(2, -1);
            var shift = Iso.Point(Iso.PlotOrigin(b).x, Iso.PlotOrigin(b).y) - Iso.Point(0, 0);
            foreach (var place in Places)
                Assert.Less((Iso.BotSpot(a, place, 3) + shift - Iso.BotSpot(b, place, 3)).magnitude, .001f, place.ToString());   // to within float rounding
            Assert.AreEqual(Iso.Point(3.5f, 9.75f), Iso.BotSpot(a, Place.Forecourt, 0));
            Assert.AreEqual(Iso.Point(4.5f, 9.75f), Iso.BotSpot(a, Place.Forecourt, 1));
            Assert.AreEqual(Iso.Point(3.5f, 10.5f), Iso.BotSpot(a, Place.Forecourt, 6));
        }

        [Test]
        public void BoundsCoverThePlots()
        {
            // a plot and the path round it: 16 tiles each way
            var one = Iso.Bounds(new[] { new Vector2Int(0, 0) });
            Assert.AreEqual(1024f, one.width, .01f);
            Assert.AreEqual(512f, one.height, .01f);
            Assert.AreEqual(0f, one.center.x, .01f);                      // plot 0 is centred under its north corner
            var two = Iso.Bounds(new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) });
            Assert.AreEqual(1024f + 448f, two.width, .01f);               // the neighbour sits one pitch along: half its width across, a quarter down
            Assert.AreEqual(512f + 224f, two.height, .01f);
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
