using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TheGrid.Core.Tests
{
    // How a sector is arranged. Every sector has the same things in it; where they stand and how they are lit differs.
    public class LayoutTests
    {
        static readonly Structure[] Structures = (Structure[])Enum.GetValues(typeof(Structure));
        static readonly Place[] Places = (Place[])Enum.GetValues(typeof(Place));
        static IEnumerable<int> All => Enumerable.Range(0, Layout.Count);

        static IEnumerable<Vector2Int> TilesOf(Layout l, Structure s)
        {
            var f = l.Footprint(s);
            for (int c = f.xMin; c < f.xMax; c++) for (int r = f.yMin; r < f.yMax; r++) yield return new Vector2Int(c, r);
        }

        [Test]
        public void EveryArrangementHasEveryStructureOnThePlotAndNoTwoShareATile()
        {
            Assert.AreEqual(7, Structures.Length);
            foreach (int a in All)
            {
                var l = Layout.Of(a);
                var tiles = Structures.SelectMany(s => TilesOf(l, s)).ToList();
                Assert.AreEqual(59, tiles.Count, "arrangement " + a);                      // 3x3 three times, 4x3 twice, 2x2 twice
                Assert.AreEqual(59, tiles.Distinct().Count(), "arrangement " + a);
                Assert.IsTrue(tiles.All(t => t.x >= 0 && t.y >= 0 && t.x < Iso.PlotTiles && t.y < Iso.PlotTiles), "arrangement " + a);
                Assert.AreEqual(new Vector2Int(3, 3), l.Footprint(Structure.ServerTower).size);
                Assert.AreEqual(new Vector2Int(4, 3), l.Footprint(Structure.Workshop).size);
                Assert.AreEqual(new Vector2Int(2, 2), l.Footprint(Structure.Relay).size);
            }
        }

        [Test]
        public void ThereAreTwoDozenArrangementsAndNoTwoAreTheSame()
        {
            Assert.AreEqual(24, Layout.Count);
            string Key(Layout l) => string.Join(";", Structures.Select(s => l.Footprint(s).ToString()));
            Assert.AreEqual(24, All.Select(a => Key(Layout.Of(a))).Distinct().Count());
            // and they differ where it shows: the server tower stands in four different places, the workshop in two
            Assert.AreEqual(4, All.Select(a => Layout.Of(a).Footprint(Structure.ServerTower).position).Distinct().Count());
            Assert.AreEqual(2, All.Select(a => Layout.Of(a).Footprint(Structure.Workshop).position).Distinct().Count());
            Assert.AreEqual(2, All.Select(a => Layout.Of(a).Footprint(Structure.Relay).position).Distinct().Count());
        }

        [Test]
        public void ASectorKeepsItsArrangementAndNoNeighbourHasTheSame()
        {
            for (int plot = 0; plot < 49; plot++)
            {
                Assert.AreEqual(Layout.For(plot).Arrangement, Layout.For(plot).Arrangement);
                Assert.That(Layout.For(plot).Arrangement, Is.InRange(0, Layout.Count - 1));
            }
            var at = Enumerable.Range(0, 49).ToDictionary(Plots.Cell, p => p);
            for (int plot = 0; plot < 25; plot++)                    // every sector of a five by five map, and all eight round it
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int other = at[Plots.Cell(plot) + new Vector2Int(dx, dy)];
                        Assert.AreNotEqual(Layout.For(plot).Arrangement, Layout.For(other).Arrangement, "plots " + plot + " and " + other);
                    }
        }

        [Test]
        public void AStructureStandsAtTheCentreOfItsFootprint()
        {
            var cell = new Vector2Int(1, -2);
            var o = Iso.PlotOrigin(cell);
            foreach (int a in new[] { 0, 7, 13, 22 })
            {
                var l = Layout.Of(a);
                foreach (var s in Structures)
                {
                    var f = l.Footprint(s);
                    Assert.AreEqual(Iso.Point(o.x + f.x + (f.width - 1) / 2f, o.y + f.y + (f.height - 1) / 2f), Iso.StructureSpot(Iso.PlotOrigin(cell), l, s));
                }
            }
        }

        static Vector2 Tile(Vector2 at) => new Vector2((at.x / (Iso.TileW / 2) - at.y / (Iso.TileH / 2)) / 2, (-at.y / (Iso.TileH / 2) - at.x / (Iso.TileW / 2)) / 2);

        [Test]
        public void FortyBotsAtEveryPlaceEachHaveTheirOwnSpot()
        {
            var cell = new Vector2Int(2, 1);
            foreach (int a in All)
            {
                var seen = new HashSet<Vector2>();
                foreach (var place in Places)
                    for (int slot = 0; slot < 40; slot++)
                        Assert.IsTrue(seen.Add(Iso.BotSpot(Iso.PlotOrigin(cell), Layout.Of(a), place, slot)), "arrangement " + a + ": " + place + " slot " + slot + " shares a spot");
            }
        }

        [Test]
        public void TheFirstBotsOfAPlaceStandOnThePlotAndOnNoStructure()
        {
            var cell = new Vector2Int(0, 0);
            foreach (int a in All)
            {
                var l = Layout.Of(a);
                var built = new HashSet<Vector2Int>(Structures.SelectMany(s => TilesOf(l, s)));
                var workshop = l.Footprint(Structure.Workshop);
                foreach (var place in Places)
                    for (int slot = 0; slot < 6; slot++)
                    {
                        var t = Tile(Iso.BotSpot(Iso.PlotOrigin(cell), l, place, slot));
                        var tile = new Vector2Int(Mathf.RoundToInt(t.x), Mathf.RoundToInt(t.y));
                        string who = "arrangement " + a + ": " + place + " slot " + slot + " at " + t;
                        Assert.IsTrue(t.x >= -.5f && t.y >= -.5f && t.x <= Iso.PlotTiles - .5f && t.y <= Iso.PlotTiles - .5f, who + " is off the plot");
                        // the workshop's walls are cut away so that its first four can be seen inside it
                        if (place == Place.Workshop && slot < 4) Assert.IsTrue(workshop.Contains(tile), who + " should be inside the workshop");
                        else Assert.IsFalse(built.Contains(tile), who + " stands on a structure");
                    }
            }
        }

        [Test]
        public void CratesQueueInFrontOfTheDepotOnThePlotClearOfEveryBuildingAndEveryBot()
        {
            Assert.AreEqual(9, Layout.Crates);
            foreach (int a in All)
            {
                var l = Layout.Of(a);
                var built = new HashSet<Vector2Int>(Structures.SelectMany(s => TilesOf(l, s)));
                var depot = l.Footprint(Structure.Depot);
                var bots = new List<Vector2>();
                foreach (var place in Places)
                    for (int slot = 0; slot < 12; slot++) bots.Add(Tile(Iso.BotSpot(Vector2Int.zero, l, place, slot)));
                var seen = new HashSet<Vector2>();
                for (int slot = 0; slot < Layout.Crates; slot++)
                {
                    var t = Tile(Iso.CrateSpot(Vector2Int.zero, l, slot));
                    string which = "arrangement " + a + ": crate " + slot + " at " + t;
                    Assert.IsTrue(seen.Add(t), which + " shares a spot");
                    Assert.IsTrue(t.x >= -.5f && t.y >= -.5f && t.x <= Iso.PlotTiles - .5f && t.y <= Iso.PlotTiles - .5f, which + " is off the plot");
                    Assert.IsFalse(built.Contains(new Vector2Int(Mathf.RoundToInt(t.x), Mathf.RoundToInt(t.y))), which + " stands on a structure");
                    // in front of the depot, within its width
                    Assert.IsTrue(t.y > depot.yMax - .5f && t.x > depot.xMin - .5f && t.x < depot.xMax - .5f, which + " is not in front of the depot");
                    foreach (var b in bots) Assert.Greater((b - t).magnitude, .6f, which + " is where a bot stands");
                    Assert.LessOrEqual(Covered(Iso.CrateSpot(Vector2Int.zero, l, slot), l, Vector2Int.zero, true), 6, which + " is behind a building");
                }
            }
        }

        [Test]
        public void AClickOnTheDepotOrOnAnyCrateShownIsAClickOnTheDepot()
        {
            var origin = new Vector2Int(24, -7);
            foreach (int a in All)
            {
                var l = Layout.Of(a);
                string which = "arrangement " + a + ": ";
                var foot = Iso.StructureSpot(origin, l, Structure.Depot);
                Assert.IsTrue(Iso.OnDepot(origin, l, 0, foot + new Vector2(0, 40)), which + "the middle of the building");
                Assert.IsTrue(Iso.OnDepot(origin, l, 0, foot + new Vector2(-80, 30)) && Iso.OnDepot(origin, l, 0, foot + new Vector2(80, 30)), which + "its two ends");
                for (int crates = 1; crates <= Layout.Crates; crates++)
                    for (int slot = 0; slot < crates; slot++)
                        Assert.IsTrue(Iso.OnDepot(origin, l, crates, Iso.CrateSpot(origin, l, slot) + new Vector2(0, 10)), which + "crate " + slot + " of " + crates);
                // a crate that is not there is not clicked: with two shown, the place of the ninth is open ground
                Assert.IsFalse(Iso.OnDepot(origin, l, 2, Iso.CrateSpot(origin, l, 8) + new Vector2(0, 10)), which + "the place of a crate not shown");
                // and nothing else of the sector is the depot
                foreach (Structure s in Structures)
                    if (s != Structure.Depot) Assert.IsFalse(Iso.OnDepot(origin, l, Layout.Crates, Iso.StructureSpot(origin, l, s) + new Vector2(0, 20)), which + s);
                for (int slot = 0; slot < 12; slot++)
                    Assert.IsFalse(Iso.OnDepot(origin, l, Layout.Crates, Iso.BotSpot(origin, l, Place.Forecourt, slot) + new Vector2(0, 16)), which + "a bot that needs the owner, slot " + slot);
            }
        }

        // How many pixels of a bot's height a sector's structures cover, where the bot stands at `at` (art pixels).
        // A structure covers a bot only when it is drawn after it: when its own ground point is lower on the screen.
        static float Covered(Vector2 at, Layout l, Vector2Int cell, bool own)
        {
            const float botHalf = 9, botHigh = 34;
            float worst = 0;
            var o = Iso.PlotOrigin(cell);
            var local = Tile(at) - new Vector2(o.x, o.y);
            foreach (var s in Structures)
            {
                var f = l.Footprint(s);
                if (own && s == Structure.Workshop && local.x > f.xMin - .5f && local.x < f.xMax - .5f && local.y > f.yMin - .5f && local.y < f.yMax - .5f) continue;   // standing inside it
                if (!(at.y > Iso.StructureSpot(Iso.PlotOrigin(cell), l, s).y)) continue;
                Vector2 back = Iso.Point(o.x + f.xMin - .5f, o.y + f.yMin - .5f), left = Iso.Point(o.x + f.xMin - .5f, o.y + f.yMax - .5f),
                        right = Iso.Point(o.x + f.xMax - .5f, o.y + f.yMin - .5f), front = Iso.Point(o.x + f.xMax - .5f, o.y + f.yMax - .5f);
                foreach (float x in new[] { at.x - botHalf, at.x, at.x + botHalf })
                {
                    if (!(left.x < x && x < right.x)) continue;
                    float Edge(Vector2 p, Vector2 q) => p.y + (q.y - p.y) * (x - p.x) / (q.x - p.x);
                    float top = (x <= back.x ? Edge(left, back) : Edge(back, right)) + Layout.Wall(s);
                    float bottom = x <= front.x ? Edge(left, front) : Edge(front, right);
                    worst = Mathf.Max(worst, Mathf.Min(top, at.y + botHigh) - Mathf.Max(bottom, at.y));
                }
            }
            return worst;
        }

        [Test]
        public void NoBotIsHiddenByABuildingOfItsOwnSectorOrOfOneInFront()
        {
            var here = new Vector2Int(0, 0);
            var inFront = new[] { new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
            foreach (int a in All)
            {
                var l = Layout.Of(a);
                foreach (var place in Places)
                {
                    // A bot that needs the owner is seen whole: three rows of four, all there is room for on the plot. Elsewhere
                    // its boots may be behind a kerb.
                    int slots = place == Place.Forecourt ? 12 : 6;
                    float allowed = place == Place.Forecourt ? 0 : 6;
                    for (int slot = 0; slot < slots; slot++)
                    {
                        var at = Iso.BotSpot(Iso.PlotOrigin(here), l, place, slot);
                        string who = "arrangement " + a + ": " + place + " slot " + slot;
                        Assert.LessOrEqual(Covered(at, l, here, true), allowed, who + " is behind a building of its own sector");
                        foreach (int b in All)
                            foreach (var cell in inFront)
                                Assert.LessOrEqual(Covered(at, Layout.Of(b), cell, false), allowed, who + " is behind arrangement " + b + " at " + cell);
                    }
                }
            }
        }

        [Test]
        public void NoNameOfAnotherBotLiesOverABotThatNeedsTheOwner()
        {
            // A bot's name is drawn under its feet, on a plate about 80 pixels by 18 at actual size. The first three bots at
            // any other place and the first seven that need the owner: none of those names touches one of these bots.
            const float nameHalf = 40, nameDrop = 8, nameHigh = 18, botHalf = 8, botHigh = 30;
            var cell = new Vector2Int(0, 0);
            foreach (int a in All)
            {
                var l = Layout.Of(a);
                foreach (var place in Places)
                {
                    if (place == Place.Forecourt) continue;
                    for (int slot = 0; slot < 3; slot++)
                        for (int f = 0; f < 7; f++)
                        {
                            Vector2 other = Iso.BotSpot(Iso.PlotOrigin(cell), l, place, slot), bot = Iso.BotSpot(Iso.PlotOrigin(cell), l, Place.Forecourt, f);
                            bool beside = Mathf.Abs(other.x - bot.x) > nameHalf + botHalf;
                            bool clear = bot.y >= other.y - nameDrop || bot.y + botHigh <= other.y - nameDrop - nameHigh;
                            Assert.IsTrue(beside || clear, "arrangement " + a + ": the name of " + place + " slot " + slot + " lies over forecourt slot " + f);
                        }
                }
            }
        }

        [Test]
        public void ASpotMovesWithItsSector()
        {
            Vector2Int a = new Vector2Int(0, 0), b = new Vector2Int(2, -1);
            var shift = Iso.Point(Iso.PlotOrigin(b).x, Iso.PlotOrigin(b).y) - Iso.Point(0, 0);
            var l = Layout.Of(5);
            foreach (var place in Places)
                Assert.Less((Iso.BotSpot(Iso.PlotOrigin(a), l, place, 3) + shift - Iso.BotSpot(Iso.PlotOrigin(b), l, place, 3)).magnitude, .001f, place.ToString());   // to within float rounding
        }
    }
}
