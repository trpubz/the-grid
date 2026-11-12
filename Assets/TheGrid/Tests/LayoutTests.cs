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
                        // the tile it stands over: a bot on an upper floor is drawn a storey up for each floor
                        var t = Tile(Iso.BotSpot(Iso.PlotOrigin(cell), l, place, slot) - new Vector2(0, l.Lift(place, slot)));
                        var tile = new Vector2Int(Mathf.RoundToInt(t.x), Mathf.RoundToInt(t.y));
                        string who = "arrangement " + a + ": " + place + " slot " + slot + " at " + t;
                        Assert.IsTrue(t.x >= -.5f && t.y >= -.5f && t.x <= Iso.PlotTiles - .5f && t.y <= Iso.PlotTiles - .5f, who + " is off the plot");
                        // the workshop's walls are cut away so that its first four can be seen inside it
                        if (place == Place.Workshop && slot < 4) Assert.IsTrue(workshop.Contains(tile), who + " should be inside the workshop");
                        // and a building a bot works in is opened the same way: its first six stand inside
                        else if (World.StructureOf(place) is Structure own && own != Structure.Workshop) Assert.IsTrue(l.Footprint(own).Contains(tile), who + " should be inside the " + own);
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

        [Test]
        public void ABotInsideABuildingIsDrawnOverItWhereverInTheRoomItStands()
        {
            var origin = new Vector2Int(-19, 43);
            foreach (int a in All)
            {
                var l = Layout.Of(a);
                foreach (var place in Places)
                    for (int slot = 0; slot < 10; slot++)
                    {
                        float depth = Iso.BotDepth(origin, l, place, slot), stands = Iso.BotSpot(origin, l, place, slot).y;
                        string who = "arrangement " + a + ": " + place + " slot " + slot;
                        bool inside = place == Place.Workshop ? slot < 4 : World.StructureOf(place) != null && slot < Layout.Inside;
                        // lower on the screen is drawn later: a bot in a room is drawn after the room, also at its back
                        if (inside) Assert.Less(depth, Iso.StructureSpot(origin, l, World.StructureOf(place).Value).y, who + " would be drawn under its own building");
                        else Assert.AreEqual(stands, depth, who + " stands in the open and is drawn where it stands");
                        Assert.LessOrEqual(depth, stands, who);
                    }
                // and two in one room keep their order: the one nearer the viewer is drawn later
                foreach (var place in new[] { Place.ServerTower, Place.ReviewHall, Place.Works })
                    for (int i = 0; i < Layout.Inside; i++)
                        for (int j = 0; j < Layout.Inside; j++)
                            if (Iso.BotSpot(origin, l, place, i).y < Iso.BotSpot(origin, l, place, j).y)
                                Assert.Less(Iso.BotDepth(origin, l, place, i), Iso.BotDepth(origin, l, place, j), "arrangement " + a + ": " + place + " slots " + i + " and " + j);
            }
        }

        static readonly Place[] Tall = { Place.ServerTower, Place.ReviewHall, Place.Works };

        [Test]
        public void TheSixInsideATallBuildingAreSpreadOverItsFloors()
        {
            Assert.AreEqual(3, Layout.Floors(Structure.ServerTower));
            Assert.AreEqual(2, Layout.Floors(Structure.ReviewHall));
            foreach (var low in new[] { Structure.Works, Structure.Workshop, Structure.Depot, Structure.Relay, Structure.Fountain }) Assert.AreEqual(1, Layout.Floors(low), low.ToString());
            foreach (int a in All)
            {
                var l = Layout.Of(a);
                foreach (var place in Tall)
                {
                    int floors = Layout.Floors(World.StructureOf(place).Value);
                    var on = Enumerable.Range(0, Layout.Inside).Select(slot => l.Floor(place, slot)).ToList();
                    for (int floor = 0; floor < floors; floor++)
                        Assert.AreEqual(Layout.Inside / floors, on.Count(f => f == floor), "arrangement " + a + ": " + place + " floor " + floor);
                    Assert.AreEqual(0, l.Floor(place, Layout.Inside), "the one waiting at the door stands on the ground");
                    Assert.AreEqual(0f, l.Lift(place, Layout.Inside), "and on the paving");
                    var f0 = l.Footprint(World.StructureOf(place).Value);
                    for (int slot = 0; slot < Layout.Inside; slot++)
                    {
                        var origin = new Vector2Int(7, -30);
                        var t = l.Spot(place, slot);
                        // the floor of the room is a step up from the paving, and a floor up is so many pixels more, whatever the floor
                        Assert.AreEqual(Layout.FloorPixels + l.Floor(place, slot) * Layout.StoreyPixels, l.Lift(place, slot), .01f);
                        Assert.AreEqual(Iso.Point(origin.x + t.x, origin.y + t.y).y + l.Lift(place, slot), Iso.BotSpot(origin, l, place, slot).y, .01f);
                        // an upper floor reaches two floor squares out from the back wall: who stands on it stands within them
                        if (l.Floor(place, slot) > 0)
                            Assert.IsTrue(t.y - f0.yMin > -.1f && t.y - f0.yMin < Layout.FloorReach - .1f, "arrangement " + a + ": " + place + " slot " + slot + " at " + t + " is off its floor");
                    }
                }
                foreach (var place in Places.Except(Tall))
                    for (int slot = 0; slot < 8; slot++)
                    {
                        Assert.AreEqual(0, l.Floor(place, slot), place.ToString());
                        Assert.AreEqual(0f, l.Lift(place, slot), place.ToString());
                    }
            }
        }

        [Test]
        public void ABotGoesToItsPlaceInATallBuildingByTheDoorAndTheStairs()
        {
            foreach (int a in All)
            {
                var l = Layout.Of(a);
                foreach (var place in Tall)
                {
                    var f = l.Footprint(World.StructureOf(place).Value);
                    bool Within(Vector3 p) => p.x > f.xMin - .5f && p.x < f.xMax - .5f && p.y > f.yMin - .5f && p.y < f.yMax - .5f;
                    for (int slot = 0; slot < Layout.Inside; slot++)
                    {
                        string who = "arrangement " + a + ": " + place + " slot " + slot;
                        var way = l.Way(place, slot);
                        var spot = l.Spot(place, slot);
                        Assert.AreEqual(new Vector3(spot.x, spot.y, l.Floor(place, slot)), way[way.Count - 1], who + " ends where it stands");
                        Assert.IsFalse(Within(way[0]), who + " starts outside");
                        Assert.Greater(way[0].y, f.yMax - .5f, who + " starts in front of the building");
                        Assert.AreEqual(0f, way[0].z, who);
                        int stairs = 0;
                        for (int i = 1; i < way.Count; i++)
                        {
                            Vector3 p = way[i - 1], q = way[i];
                            Assert.AreNotEqual(p, q, who + ": a leg of no length");
                            if (i > 1) Assert.IsTrue(Within(p) && Within(q), who + ": leg " + i + " leaves the building");
                            if (p.z == q.z)
                            {
                                // along a floor; an upper floor is only so deep, and where a stair comes up through it there is
                                // only a walk along its edge: so up there a bot turns square corners
                                if (p.z > 0)
                                {
                                    Assert.IsTrue(p.y - f.yMin < Layout.FloorReach - .1f && q.y - f.yMin < Layout.FloorReach - .1f, who + ": leg " + i + " walks off the edge of floor " + p.z);
                                    Assert.IsTrue(p.x == q.x || p.y == q.y, who + ": leg " + i + " cuts a corner on floor " + p.z);
                                }
                                continue;
                            }
                            // a stair: one floor up, along a wall, long enough to be a stair
                            stairs++;
                            Assert.AreEqual(1f, q.z - p.z, who + ": leg " + i);
                            Assert.IsTrue(p.x == q.x || p.y == q.y, who + ": stair " + stairs + " runs askew");
                            Assert.GreaterOrEqual(Mathf.Abs(q.x - p.x) + Mathf.Abs(q.y - p.y), .8f, who + ": stair " + stairs + " is a ladder");
                        }
                        Assert.AreEqual(l.Floor(place, slot), stairs, who + " takes one stair for each floor up");
                    }
                    // the one who waits at the door, and everyone in the open, is simply there
                    Assert.AreEqual(1, l.Way(place, Layout.Inside).Count);
                }
                foreach (var place in Places.Except(Tall)) Assert.AreEqual(1, l.Way(place, 0).Count, place.ToString());
                // in art pixels, with the floors' heights: what the stage walks a bot along
                var origin = new Vector2Int(-12, 5);
                var points = Iso.Way(origin, l, Place.ServerTower, 2);
                Assert.AreEqual(l.Way(Place.ServerTower, 2).Count, points.Count);
                Assert.AreEqual(Iso.BotSpot(origin, l, Place.ServerTower, 2), points[points.Count - 1]);
                var door = l.Way(Place.ServerTower, 2)[0];
                Assert.AreEqual(Iso.Point(origin.x + door.x, origin.y + door.y), points[0], "the door is on the paving");
                // the second floor is reached from the left, where a bot on the stair is not behind the building next along
                var up = l.Way(Place.ServerTower, 2);
                var corner = l.Footprint(Structure.ServerTower);
                foreach (var p in up.Skip(1)) Assert.Less(p.x - corner.xMin - (p.y - corner.yMin), 1f, "arrangement " + a + ": the way up the tower goes behind the next building at " + p);
            }
        }

        [Test]
        public void SixWorkInsideABuildingAndTheSeventhWaitsAtItsDoor()
        {
            Assert.AreEqual(6, Layout.Inside);
            foreach (int a in All)
            {
                var l = Layout.Of(a);
                foreach (var place in new[] { Place.ServerTower, Place.ReviewHall, Place.Works })
                {
                    var f = l.Footprint(World.StructureOf(place).Value);
                    for (int slot = 0; slot < Layout.Inside + 6; slot++)
                    {
                        var t = l.Spot(place, slot);
                        string who = "arrangement " + a + ": " + place + " slot " + slot + " at " + t;
                        // inside: on the floor, clear of the walls and of the racks along the two far ones
                        bool inside = t.x > f.xMin - .5f + .7f && t.x < f.xMax - .5f - .3f && t.y > f.yMin - .5f + .9f && t.y < f.yMax - .5f - .3f;
                        if (slot < Layout.Inside) Assert.IsTrue(inside, who + " should be inside");
                        else Assert.Greater(t.y, f.yMax - .5f, who + " should wait in front of the door");
                    }
                }
            }
        }

        [Test]
        public void ABotChangingPlacesGoesByTheStairsAndNoFurtherThanItMust()
        {
            foreach (int a in All)
            {
                var l = Layout.Of(a);
                int On(Place place, int floor, int nth) => Enumerable.Range(0, Layout.Inside).Where(slot => l.Floor(place, slot) == floor).ElementAt(nth);
                var f = l.Footprint(Structure.ServerTower);
                bool Within(Vector3 p) => p.x > f.xMin - .5f && p.x < f.xMax - .5f && p.y > f.yMin - .5f && p.y < f.yMax - .5f;
                int top = On(Place.ServerTower, 2, 0);
                var plaza = l.Spot(Place.Plaza, 0);

                // from the open to the top of the tower: from where it stood, then the way there
                var up = l.Walk(Place.Plaza, 0, Place.ServerTower, top);
                Assert.AreEqual(new Vector3(plaza.x, plaza.y, 0), up[0]);
                CollectionAssert.AreEqual(l.Way(Place.ServerTower, top), up.Skip(1).ToList(), "arrangement " + a);
                // and back the same way: down the stairs before it crosses the sector
                var down = l.Walk(Place.ServerTower, top, Place.Plaza, 0);
                CollectionAssert.AreEqual(Enumerable.Reverse(up).ToList(), down, "arrangement " + a);

                // between two places on one floor: straight there
                for (int floor = 0; floor < 3; floor++)
                    Assert.AreEqual(2, l.Walk(Place.ServerTower, On(Place.ServerTower, floor, 0), Place.ServerTower, On(Place.ServerTower, floor, 1)).Count, "arrangement " + a + ": along floor " + floor);

                // from one floor to another: by the stairs between them, and never out by the door
                foreach (var (from, to) in new[] { (0, 1), (1, 2), (2, 0), (2, 1) })
                {
                    var between = l.Walk(Place.ServerTower, On(Place.ServerTower, from, 0), Place.ServerTower, On(Place.ServerTower, to, 1));
                    string who = "arrangement " + a + ": floor " + from + " to floor " + to;
                    Assert.IsTrue(between.All(Within), who + " leaves the building");
                    int stairs = 0;
                    for (int i = 1; i < between.Count; i++)
                    {
                        Vector3 p = between[i - 1], q = between[i];
                        if (p.z != q.z) stairs++;
                        else if (p.z > 0) Assert.IsTrue(p.x == q.x || p.y == q.y, who + ": leg " + i + " cuts a corner on floor " + p.z);
                    }
                    Assert.AreEqual(Mathf.Abs(to - from), stairs, who);
                }

                // from one building to another: out by its door, in by the other's
                var across = l.Walk(Place.ServerTower, top, Place.ReviewHall, On(Place.ReviewHall, 1, 0));
                CollectionAssert.AreEqual(Enumerable.Reverse(l.Way(Place.ServerTower, top)).Concat(l.Way(Place.ReviewHall, On(Place.ReviewHall, 1, 0))).ToList(), across, "arrangement " + a);

                // staying put
                Assert.AreEqual(1, l.Walk(Place.Works, 2, Place.Works, 2).Count);
            }
        }

        [Test]
        public void AWalkIsInArtPixelsWithHowFarDownEachPointIsDrawn()
        {
            var o = new Vector2Int(-12, 5);
            foreach (int a in All)
            {
                var l = Layout.Of(a);
                // a point of the plot is in a room when it is inside a building a bot works in
                foreach (var place in Places)
                    for (int slot = 0; slot < 12; slot++)
                    {
                        string who = "arrangement " + a + ": " + place + " slot " + slot;
                        Assert.AreEqual(l.IsInside(place, slot) ? World.StructureOf(place) : null, l.Room(l.Spot(place, slot)), who);
                        // a walk ends where the bot will stand, drawn as it will be drawn standing there
                        var walk = Iso.Walk(o, l, Place.Plaza, 0, place, slot);
                        var end = walk[walk.Count - 1];
                        var spot = Iso.BotSpot(o, l, place, slot);
                        Assert.AreEqual(spot.x, end.x, .01f, who);
                        Assert.AreEqual(spot.y, end.y, .01f, who);
                        Assert.AreEqual(spot.y - Iso.BotDepth(o, l, place, slot), end.z, .01f, who);
                        Assert.AreEqual(l.Walk(Place.Plaza, 0, place, slot).Count, walk.Count, who);
                    }
                // on the stairs and on every floor a bot is drawn over its building: after the building, whose place in the
                // order is the point it is drawn from
                foreach (var place in Tall)
                    for (int slot = 0; slot < Layout.Inside; slot++)
                    {
                        var walk = Iso.Walk(o, l, Place.Plaza, 0, place, slot);
                        float building = Iso.StructureSpot(o, l, World.StructureOf(place).Value).y;
                        for (int i = 2; i < walk.Count; i++)
                            Assert.Less(walk[i].y - walk[i].z, building, "arrangement " + a + ": " + place + " slot " + slot + " point " + i + " is drawn under its building");
                        Assert.AreEqual(0f, walk[0].z, "in the open a bot is drawn where it stands");
                    }
            }
        }

        // How many pixels of a bot's height a sector's structures cover, where the bot stands at `at` (art pixels).
        // A structure covers a bot only when it is drawn after it: when its own ground point is lower on the screen than
        // the bot is drawn as if it stood (`depth`: Iso.BotDepth for a bot inside a building, else where it stands).
        static float Covered(Vector2 at, Layout l, Vector2Int cell, bool own, Structure? inside = null, float? depth = null)
        {
            const float botHalf = 9, botHigh = 34;
            float worst = 0;
            var o = Iso.PlotOrigin(cell);
            var local = Tile(at) - new Vector2(o.x, o.y);
            foreach (var s in Structures)
            {
                var f = l.Footprint(s);
                if (own && s == inside) continue;   // standing inside it: it is opened
                if (!((depth ?? at.y) > Iso.StructureSpot(Iso.PlotOrigin(cell), l, s).y)) continue;
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
                        Structure? inside = l.IsInside(place, slot) ? World.StructureOf(place) : null;
                        float depth = Iso.BotDepth(Iso.PlotOrigin(here), l, place, slot);
                        Assert.LessOrEqual(Covered(at, l, here, true, inside, depth), allowed, who + " is behind a building of its own sector");
                        foreach (int b in All)
                            foreach (var cell in inFront)
                                Assert.LessOrEqual(Covered(at, Layout.Of(b), cell, false, null, depth), allowed, who + " is behind arrangement " + b + " at " + cell);
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
