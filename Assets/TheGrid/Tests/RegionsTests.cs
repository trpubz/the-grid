using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TheGrid.Core.Tests
{
    // A region is the part of the map one workspace's sectors stand in: together, in its own lights, apart from the rest.
    public class RegionsTests
    {
        string tmp;

        [SetUp]
        public void SetUp()
        {
            tmp = Path.Combine(Path.GetTempPath(), "grid-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(tmp, true);

        // One running session in each repo. "home/a" is repo /home/a in workspace home.
        static RosterSnapshot Of(params string[] repos)
        {
            var snap = new RosterSnapshot { generatedAt = 1 };
            foreach (var r in repos)
                snap.agents.Add(new AgentRow { sessionId = r, name = r, account = "one", repoRoot = "/" + r, workspace = r.Split('/')[0], state = "Running" });
            return snap;
        }

        static WorldSector Sector(WorldModel w, string repo) => w.sectors.Single(s => s.repoRoot == "/" + repo);

        static readonly string[] Three = { "home/a", "firm/a", "home/b", "work/a", "home/c", "work/b", "firm/b", "home/d", "home/e" };

        [Test]
        public void TheSectorsOfAWorkspaceStandTogetherWithOpenGroundRoundThem()
        {
            var w = World.Build(Of(Three), new Plots());
            CollectionAssert.AreEqual(new[] { "home", "firm", "work" }, w.regions.Select(r => r.name).ToArray());
            foreach (var s in w.sectors) Assert.AreEqual(s.repoRoot.Split('/')[1], w.regions[s.region].name, s.repoRoot);
            Assert.AreEqual(w.sectors.Count, w.sectors.Select(s => s.cell).Distinct().Count(), "two sectors on one cell");
            foreach (var a in w.sectors)
                foreach (var b in w.sectors)
                {
                    if (a.region == b.region) continue;
                    var d = a.cell - b.cell;
                    // not side by side and not corner to corner: a whole cell of open ground lies between
                    Assert.GreaterOrEqual(Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y)), 2, a.repoRoot + " and " + b.repoRoot);
                }
            // a region is one block: no sector of it is further from its first than the block is wide
            foreach (var r in Enumerable.Range(0, w.regions.Count))
            {
                var cells = w.sectors.Where(s => s.region == r).Select(s => s.cell).ToList();
                Assert.LessOrEqual(cells.Max(c => c.x) - cells.Min(c => c.x), 2, w.regions[r].name);
                Assert.LessOrEqual(cells.Max(c => c.y) - cells.Min(c => c.y), 2, w.regions[r].name);
            }
        }

        [Test]
        public void WithOneWorkspaceTheMapIsLaidOutOnTheOneSpiral()
        {
            var repos = Enumerable.Range(0, 12).Select(i => "home/r" + i).ToArray();
            var w = World.Build(Of(repos), new Plots());
            Assert.AreEqual(1, w.regions.Count);
            CollectionAssert.AreEqual(Enumerable.Range(0, 12).Select(Plots.Cell).ToArray(), w.sectors.Select(s => s.cell).ToArray());
        }

        [Test]
        public void MovingARepoToAnotherWorkspaceMovesItsSectorAndNoOther()
        {
            var plots = new Plots();
            var before = World.Build(Of("home/a", "home/b", "home/c", "work/a"), plots);
            // home/b becomes a work repo: the same repo, so the same plot, in the other region
            var snap = Of("home/a", "home/b", "home/c", "work/a");
            snap.agents[1].workspace = "work";
            var after = World.Build(snap, plots);
            foreach (var repo in new[] { "home/a", "home/c", "work/a" })
                Assert.AreEqual(Sector(before, repo).cell, Sector(after, repo).cell, repo + " moved");
            Assert.AreEqual(Sector(after, "work/a").region, Sector(after, "home/b").region);
            Assert.AreEqual(Sector(before, "home/b").plot, Sector(after, "home/b").plot);
            Assert.AreNotEqual(Sector(before, "home/b").cell, Sector(after, "home/b").cell);

            // the place it left is the next one given out in that region
            var grown = Of("home/a", "home/b", "home/c", "work/a", "home/d");
            grown.agents[1].workspace = "work";
            Assert.AreEqual(Sector(before, "home/b").cell, Sector(World.Build(grown, plots), "home/d").cell);

            // and all of it is in the world's file
            var file = Path.Combine(tmp, "world.json");
            plots.SaveIfGrown(file);
            var again = World.Build(grown, Plots.Load(file));
            CollectionAssert.AreEqual(World.Build(grown, plots).sectors.Select(s => s.cell).ToArray(), again.sectors.Select(s => s.cell).ToArray());
        }

        [Test]
        public void ASectorWithNobodyInItStaysInItsWorkspacesRegion()
        {
            var plots = new Plots();
            var settings = new Settings { workspaces = new List<string> { "home", "work" } };
            settings.repoWorkspaces.Add(new RepoWorkspace { repo = "/work/a", workspace = "work" });
            World.Build(Of("home/a", "work/a", "work/b"), plots, settings);
            var w = World.Build(Of("home/a", "work/b"), plots, settings);   // the sessions of /work/a have all gone
            Assert.AreEqual("work", w.regions[Sector(w, "work/a").region].name);
            Assert.AreEqual(0, Sector(w, "work/a").bots.Count);
        }

        [Test]
        public void ARegionTakesItsWorkspacesPlaceInTheListOfLightsUntilTheOwnerPicksAnother()
        {
            var settings = new Settings { workspaces = new List<string> { "home", "firm", "work" } };
            var w = World.Build(Of("home/a", "work/a"), new Plots(), settings);
            // work is the third workspace, so it has the third look, though no repo of the second is on the map
            CollectionAssert.AreEqual(new[] { 0, 2 }, w.regions.Select(r => r.look).ToArray());
            Assert.AreEqual("rose", Lights.LookNames[2]);
            settings.CycleLight("work", 2);
            w = World.Build(Of("home/a", "work/a"), new Plots(), settings);
            CollectionAssert.AreEqual(new[] { 0, 3 }, w.regions.Select(r => r.look).ToArray());
        }

        [Test]
        public void EveryLookHasAPrimaryLightOfItsOwnAndTwoAccents()
        {
            Assert.GreaterOrEqual(Lights.LookCount, 6);
            Assert.AreEqual(Lights.LookCount, Lights.LookNames.Length);
            Assert.AreEqual(Lights.LookCount, Enumerable.Range(0, Lights.LookCount).Select(Lights.Primary).Distinct().Count(), "two looks share a primary light");
            CollectionAssert.DoesNotContain(Lights.LookNames, "amber");   // amber means a bot needs the owner
            for (int look = 0; look < Lights.LookCount; look++)
            {
                var three = new[] { Lights.Primary(look), Lights.Accent(look, 0), Lights.Accent(look, 1) };
                Assert.AreEqual(3, three.Distinct().Count(), Lights.LookNames[look]);
                Assert.IsTrue(three.All(f => f >= 0 && f < Lights.Count), Lights.LookNames[look]);
            }
            Assert.AreEqual(0, Lights.Primary(0), "the first look is the one the ground is drawn in");
        }

        [Test]
        public void InASectorMostLightsAreTheRegionsPrimaryAndSomeAreAccents()
        {
            var buildings = ((Structure[])Enum.GetValues(typeof(Structure))).Where(s => s != Structure.Fountain).ToList();
            for (int look = 0; look < Lights.LookCount; look++)
                for (int plot = 0; plot < 30; plot++)
                {
                    var layout = Layout.For(plot, Plots.Cell(plot), look);
                    var lights = buildings.Select(layout.Light).ToList();
                    int primary = lights.Count(l => l == Lights.Primary(look));
                    Assert.GreaterOrEqual(primary, buildings.Count / 2, "look " + look + ", plot " + plot);
                    Assert.Less(primary, buildings.Count, "no accent in look " + look + ", plot " + plot);
                    Assert.IsTrue(lights.All(l => l == Lights.Primary(look) || l == Lights.Accent(look, 0) || l == Lights.Accent(look, 1)), "look " + look + ", plot " + plot);
                    Assert.AreEqual(-1, layout.Light(Structure.Fountain), "the fountain keeps its own violet everywhere");
                }
            // which building carries an accent is not the same in every sector
            Assert.Greater(Enumerable.Range(0, 8).Select(p => string.Join(",", buildings.Select(Layout.For(p, Plots.Cell(p), 1).Light))).Distinct().Count(), 3);
        }

        // Every tile a road between two regions lies on: straight from one sector's lane to the other's, two tiles wide,
        // where a link would leave.
        static HashSet<Vector2Int> RoadTiles(WorldModel w)
        {
            var tiles = new HashSet<Vector2Int>();
            foreach (var (from, to) in Roads.Between(w))
            {
                Vector2Int a = Iso.PlotOrigin(from), b = Iso.PlotOrigin(to);
                Assert.IsTrue((from.y == to.y && from.x < to.x) || (from.x == to.x && from.y < to.y), "a road runs straight, from the nearer sector: " + from + " to " + to);
                for (int k = 0; k < 2; k++)
                    if (from.y == to.y) for (int c = a.x + Iso.PlotTiles + Iso.LaneTiles; c < b.x - Iso.LaneTiles; c++) tiles.Add(new Vector2Int(c, a.y + Iso.Crossing + k));
                    else for (int r = a.y + Iso.PlotTiles + Iso.LaneTiles; r < b.y - Iso.LaneTiles; r++) tiles.Add(new Vector2Int(a.x + Iso.Crossing + k, r));
            }
            return tiles;
        }

        [Test]
        public void ARoadCrossesTheOpenGroundBetweenRegionsThatStandSideBySide()
        {
            var w = World.Build(Of(Three), new Plots());
            var roads = Roads.Between(w);
            Assert.AreEqual(2, roads.Count);   // home to firm, firm to work: the regions stand on a spiral of their own
            var cells = new HashSet<Vector2Int>(w.sectors.Select(s => s.cell));
            foreach (var (from, to) in roads)
            {
                Assert.AreNotEqual(Sector(w, from).region, Sector(w, to).region);
                var step = from.y == to.y ? Vector2Int.right : Vector2Int.up;
                for (var c = from + step; c != to; c += step) Assert.IsFalse(cells.Contains(c), "a road runs through the sector at " + c);
            }
        }

        static WorldSector Sector(WorldModel w, Vector2Int cell) => w.sectors.Single(s => s.cell == cell);

        [Test]
        public void ACourierBetweenAnyTwoSectorsKeepsToLanesLinksAndRoads()
        {
            var w = World.Build(Of(Three), new Plots());
            var cells = new HashSet<Vector2Int>(w.sectors.Select(s => s.cell));
            var roads = RoadTiles(w);
            foreach (var a in w.sectors)
                foreach (var b in w.sectors)
                {
                    if (a == b) continue;
                    var hops = Roads.Hops(w, a.cell, b.cell);
                    Assert.IsNotNull(hops, "no way from " + a.repoRoot + " to " + b.repoRoot);
                    Assert.AreEqual(a.cell, hops[0]);
                    Assert.AreEqual(b.cell, hops[hops.Count - 1]);
                    Assert.IsTrue(hops.All(cells.Contains), "a hop where no sector stands");
                    var route = Iso.Route(hops);
                    Vector2Int oa = Iso.PlotOrigin(a.cell), ob = Iso.PlotOrigin(b.cell);
                    Assert.AreEqual(new Vector2(oa.x + Iso.Crossing, oa.y + Iso.PlotTiles), route[0]);
                    Assert.AreEqual(new Vector2(ob.x + Iso.Crossing, ob.y + Iso.PlotTiles), route[route.Count - 1]);
                    for (int i = 1; i < route.Count; i++)
                    {
                        Vector2 p = route[i - 1], q = route[i];
                        Assert.IsTrue(p.x == q.x || p.y == q.y, "a leg runs along columns or along rows: " + p + " to " + q);
                        int steps = (int)Mathf.Max(Mathf.Abs(q.x - p.x), Mathf.Abs(q.y - p.y));
                        for (int k = 0; k <= steps; k++)
                        {
                            int col = (int)(p.x + Math.Sign(q.x - p.x) * k), row = (int)(p.y + Math.Sign(q.y - p.y) * k);
                            var cell = new Vector2Int(Mathf.FloorToInt((col + Iso.LaneTiles) / (float)Iso.Pitch), Mathf.FloorToInt((row + Iso.LaneTiles) / (float)Iso.Pitch));
                            bool lane = cells.Contains(cell) && Iso.OnLane(cell, col, row);
                            // a link is laid only between two sectors that stand side by side
                            bool link = cells.Contains(cell) && Iso.OnLink(col, row) && Linked(cells, cell, col, row);
                            Assert.IsTrue(lane || link || roads.Contains(new Vector2Int(col, row)), a.repoRoot + " to " + b.repoRoot + ": tile " + col + ", " + row + " is no lane, link or road");
                        }
                    }
                }
        }

        // True for a link tile in `cell`'s square of the map when the sector it leads to is there.
        static bool Linked(HashSet<Vector2Int> cells, Vector2Int cell, int col, int row)
        {
            var o = Iso.PlotOrigin(cell);
            bool beside = col - o.x >= Iso.PlotTiles + Iso.LaneTiles;   // in the open ground to the right; otherwise in front
            return cells.Contains(cell + (beside ? Vector2Int.right : Vector2Int.up));
        }

        [Test]
        public void WhereNoRoadJoinsTwoSectorsThereIsNoWay()
        {
            var w = World.Build(Of("home/a"), new Plots());
            Assert.IsNull(Roads.Hops(w, Vector2Int.zero, new Vector2Int(5, 5)));
            CollectionAssert.AreEqual(new[] { Vector2Int.zero }, Roads.Hops(w, Vector2Int.zero, Vector2Int.zero));
        }

        [Test]
        public void ADeliveryKnowsTheWayItsCourierRides()
        {
            var w = World.Build(Of("home/a", "work/a"), new Plots());
            var pile = new PileSnapshot();
            pile.rows.Add(new PileRow { key = "k", account = "one", from = "home/a", to = "work/a", receivedAt = 10 });
            var d = new Deliveries(5).New(pile, w).Single();
            CollectionAssert.AreEqual(Roads.Hops(w, d.fromCell, d.toCell), d.way);
            Assert.AreEqual(2, d.way.Count);
        }
    }
}
