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
        static WorldSector Sector(WorldModel w, Vector2Int origin) => w.sectors.Single(s => s.origin == origin);

        static readonly string[] Three = { "home/a", "firm/a", "home/b", "work/a", "home/c", "work/b", "firm/b", "home/d", "home/e" };

        // How far apart two sectors stand: the larger of the two distances between their origins, in tiles.
        static int Apart(WorldSector a, WorldSector b) => Mathf.Max(Mathf.Abs(a.origin.x - b.origin.x), Mathf.Abs(a.origin.y - b.origin.y));
        const int Beside = Iso.PlotTiles + 2 * Iso.LaneTiles + Iso.RegionGap;   // origin to origin, with the gap between two regions' lanes

        static void AssertRegionsStandApart(WorldModel w)
        {
            Assert.AreEqual(w.sectors.Count, w.sectors.Select(s => s.origin).Distinct().Count(), "two sectors in one place");
            foreach (var a in w.sectors)
                foreach (var b in w.sectors)
                {
                    if (a == b) continue;
                    if (a.region != b.region) Assert.GreaterOrEqual(Apart(a, b), Beside, a.repoRoot + " and " + b.repoRoot + " are nearer than the gap");
                    else
                    {
                        // one region, one grid: a whole number of sectors apart each way
                        var d = a.origin - b.origin;
                        Assert.IsTrue(d.x % Iso.Pitch == 0 && d.y % Iso.Pitch == 0, a.repoRoot + " and " + b.repoRoot);
                        Assert.AreEqual(Iso.PlotOrigin(a.cell - b.cell), d);
                    }
                }
        }

        [Test]
        public void TheSectorsOfAWorkspaceStandTogetherAndARegionStandsTheGapFromTheNext()
        {
            var w = World.Build(Of(Three), new Plots());
            CollectionAssert.AreEqual(new[] { "home", "firm", "work" }, w.regions.Select(r => r.name).ToArray());
            foreach (var s in w.sectors) Assert.AreEqual(s.repoRoot.Split('/')[1], w.regions[s.region].name, s.repoRoot);
            AssertRegionsStandApart(w);
            // and no further than the gap: every region has a sector exactly that far from a sector of another
            for (int r = 0; r < w.regions.Count; r++)
                Assert.AreEqual(Beside, w.sectors.Where(s => s.region == r).Min(a => w.sectors.Where(s => s.region != r).Min(b => Apart(a, b))), w.regions[r].name);
        }

        [Test]
        public void WithOneWorkspaceTheMapIsLaidOutOnTheOneSpiral()
        {
            var repos = Enumerable.Range(0, 12).Select(i => "home/r" + i).ToArray();
            var w = World.Build(Of(repos), new Plots());
            Assert.AreEqual(1, w.regions.Count);
            CollectionAssert.AreEqual(Enumerable.Range(0, 12).Select(i => Iso.PlotOrigin(Plots.Cell(i))).ToArray(), w.sectors.Select(s => s.origin).ToArray());
        }

        [Test]
        public void NothingMovesWhenTheMapGrows()
        {
            var plots = new Plots();
            var before = World.Build(Of(Three), plots);
            var more = Three.Concat(new[] { "home/f", "firm/c", "home/g", "side/a", "work/c", "home/h", "firm/d", "firm/e", "side/b", "home/i", "work/d", "work/e", "work/f" }).ToArray();
            var after = World.Build(Of(more), plots);
            foreach (var s in before.sectors) Assert.AreEqual(s.origin, Sector(after, s.repoRoot.Substring(1)).origin, s.repoRoot + " moved");
            Assert.AreEqual(4, after.regions.Count);
            AssertRegionsStandApart(after);
            // and the same again from the world's file
            var file = Path.Combine(tmp, "world.json");
            plots.SaveIfGrown(file);
            CollectionAssert.AreEqual(after.sectors.Select(s => s.origin).ToArray(), World.Build(Of(more), Plots.Load(file)).sectors.Select(s => s.origin).ToArray());
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
                Assert.AreEqual(Sector(before, repo).origin, Sector(after, repo).origin, repo + " moved");
            Assert.AreEqual(Sector(after, "work/a").region, Sector(after, "home/b").region);
            Assert.AreEqual(Sector(before, "home/b").plot, Sector(after, "home/b").plot);
            Assert.AreNotEqual(Sector(before, "home/b").origin, Sector(after, "home/b").origin);
            AssertRegionsStandApart(after);

            // the place it left is the next one given out in that region
            var grown = Of("home/a", "home/b", "home/c", "work/a", "home/d");
            grown.agents[1].workspace = "work";
            Assert.AreEqual(Sector(before, "home/b").origin, Sector(World.Build(grown, plots), "home/d").origin);
        }

        [Test]
        public void TheOwnerMovesARegionAndItStaysWhereItWasPut()
        {
            var plots = new Plots();
            var before = World.Build(Of(Three), plots);
            int work = before.regions.FindIndex(r => r.name == "work");
            var by = new Vector2Int(-200, 131);   // anywhere: not a whole number of sectors
            Assert.IsTrue(World.CanMoveRegion(before, work, by));
            plots.MoveRegion("work", by);
            var after = World.Build(Of(Three), plots);
            foreach (var s in before.sectors)
                Assert.AreEqual(s.origin + (s.region == work ? by : Vector2Int.zero), Sector(after, s.repoRoot.Substring(1)).origin, s.repoRoot);
            AssertRegionsStandApart(after);

            var file = Path.Combine(tmp, "world.json");
            plots.SaveIfGrown(file);
            CollectionAssert.AreEqual(after.sectors.Select(s => s.origin).ToArray(), World.Build(Of(Three), Plots.Load(file)).sectors.Select(s => s.origin).ToArray());

            // not onto another region, nor nearer to it than the gap
            var home = after.sectors.First(s => after.regions[s.region].name == "home");
            var mine = after.sectors.First(s => s.region == work);
            Assert.IsFalse(World.CanMoveRegion(after, work, home.origin - mine.origin));
            Assert.IsFalse(World.CanMoveRegion(after, work, home.origin - mine.origin + new Vector2Int(Beside - 1, 0)));
            Assert.IsTrue(World.CanMoveRegion(after, work, Vector2Int.zero));
        }

        [Test]
        public void TheOwnerMovesASectorToAnotherPlaceOfItsRegionsGrid()
        {
            var plots = new Plots();
            var before = World.Build(Of(Three), plots);
            var e = Sector(before, "home/e");
            var first = Sector(before, "home/a");
            // three places behind the region's first sector: free, and far from every other region
            var to = first.cell + new Vector2Int(0, -3);
            Assert.IsTrue(World.CanMoveSector(before, e, to));
            plots.MoveSector(e.plot, to);
            var after = World.Build(Of(Three), plots);
            Assert.AreEqual(first.origin + Iso.PlotOrigin(new Vector2Int(0, -3)), Sector(after, "home/e").origin);
            foreach (var s in before.sectors.Where(s => s != e)) Assert.AreEqual(s.origin, Sector(after, s.repoRoot.Substring(1)).origin, s.repoRoot);
            // a new repo is not put on top of it; it takes the place that was left
            var grown = World.Build(Of(Three.Concat(new[] { "home/f" }).ToArray()), plots);
            Assert.AreEqual(e.origin, Sector(grown, "home/f").origin);
            Assert.AreEqual(Sector(after, "home/e").origin, Sector(grown, "home/e").origin);

            // not onto another sector, nor nearer to another region than the gap
            Assert.IsFalse(World.CanMoveSector(after, Sector(after, "home/e"), first.cell));
            var firm = after.sectors.First(s => after.regions[s.region].name == "firm");
            Assert.IsFalse(World.CanMoveSector(after, Sector(after, "home/e"), first.cell + new Vector2Int(Mathf.RoundToInt((firm.origin.x - first.origin.x) / (float)Iso.Pitch), Mathf.RoundToInt((firm.origin.y - first.origin.y) / (float)Iso.Pitch))));
            Assert.IsTrue(World.CanMoveSector(after, Sector(after, "home/e"), Sector(after, "home/e").cell), "where it stands");
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
        public void APulseTakesEveryLightOfABuildingAStepDownWithinItsOwnFamily()
        {
            for (int light = 0; light < Lights.Count; light++)
            {
                int[] full = Lights.Family(light), dim = Lights.Dimmed(light);
                Assert.AreEqual(Lights.Parts, dim.Length);
                foreach (int c in dim) CollectionAssert.Contains(full, c, "light " + light + " pulses to a colour of another family");
                Assert.AreEqual(full[1], dim[0], "the bright line goes down to the dimmer line's colour");
                Assert.AreNotEqual(full[0], dim[0]);
                Assert.AreEqual(full[3], dim[5], "the glow nearest the wall goes down to the farthest's");
            }
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

        // Every tile of every road on the map.
        static HashSet<Vector2Int> RoadTiles(WorldModel w)
        {
            var tiles = new HashSet<Vector2Int>();
            foreach (var (from, to) in Roads.All(w)) tiles.UnionWith(Iso.RoadTiles(from, to));
            return tiles;
        }

        static void AssertEverySectorIsReachedByRoad(WorldModel w)
        {
            var roads = RoadTiles(w);
            foreach (var t in roads)
                foreach (var s in w.sectors) Assert.IsFalse(Iso.Within(s.origin, t.x, t.y), "a road runs through " + s.repoRoot + " at " + t);
            foreach (var a in w.sectors)
                foreach (var b in w.sectors)
                {
                    if (a == b) continue;
                    var hops = Roads.Hops(w, a.origin, b.origin);
                    Assert.IsNotNull(hops, "no way from " + a.repoRoot + " to " + b.repoRoot);
                    Assert.AreEqual(a.origin, hops[0]);
                    Assert.AreEqual(b.origin, hops[hops.Count - 1]);
                    var route = Iso.Route(hops);
                    Assert.AreEqual(new Vector2(a.origin.x + Iso.Crossing, a.origin.y + Iso.PlotTiles), route[0]);
                    Assert.AreEqual(new Vector2(b.origin.x + Iso.Crossing, b.origin.y + Iso.PlotTiles), route[route.Count - 1]);
                    for (int i = 1; i < route.Count; i++)
                    {
                        Vector2 p = route[i - 1], q = route[i];
                        Assert.IsTrue(p.x == q.x || p.y == q.y, "a leg runs along columns or along rows: " + p + " to " + q);
                        int steps = (int)Mathf.Max(Mathf.Abs(q.x - p.x), Mathf.Abs(q.y - p.y));
                        for (int k = 0; k <= steps; k++)
                        {
                            int col = (int)(p.x + Math.Sign(q.x - p.x) * k), row = (int)(p.y + Math.Sign(q.y - p.y) * k);
                            Assert.IsTrue(roads.Contains(new Vector2Int(col, row)) || w.sectors.Any(s => Iso.OnLane(s.origin, col, row)), a.repoRoot + " to " + b.repoRoot + ": tile " + col + ", " + row + " is no lane, link or road");
                        }
                    }
                }
        }

        [Test]
        public void EverySectorIsReachedByLanesLinksAndRoads()
        {
            var w = World.Build(Of(Three), new Plots());
            var all = Roads.All(w);
            // two sectors of one region that stand side by side are joined by a link
            foreach (var a in w.sectors)
                foreach (var b in w.sectors)
                    if (a.region == b.region && (b.cell - a.cell == Vector2Int.right || b.cell - a.cell == Vector2Int.up))
                        CollectionAssert.Contains(all, (a.origin, b.origin));
            // and there are no more roads than it takes to join everything up
            Assert.LessOrEqual(all.Count(r => Sector(w, r.from).region != Sector(w, r.to).region), w.regions.Count - 1);
            foreach (var (from, to) in all)
            {
                var d = to - from;
                Assert.IsTrue(Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? d.x > 0 : d.y > 0, "a road is told from the sector to the left or behind: " + from + " to " + to);
            }
            AssertEverySectorIsReachedByRoad(w);
        }

        [Test]
        public void AfterTheOwnerMovesThingsEverySectorIsStillReachedByRoad()
        {
            var plots = new Plots();
            var w = World.Build(Of(Three), plots);
            plots.MoveRegion("work", new Vector2Int(-200, 131));
            plots.MoveRegion("firm", new Vector2Int(107, -245));
            var e = Sector(w, "home/e");
            plots.MoveSector(e.plot, Sector(w, "home/a").cell + new Vector2Int(0, -3));   // off on its own, no neighbour beside it
            w = World.Build(Of(Three), plots);
            AssertRegionsStandApart(w);
            AssertEverySectorIsReachedByRoad(w);
        }

        [Test]
        public void WhereNoSectorStandsThereIsNoWay()
        {
            var w = World.Build(Of("home/a"), new Plots());
            Assert.IsNull(Roads.Hops(w, Vector2Int.zero, new Vector2Int(95, 95)));
            CollectionAssert.AreEqual(new[] { Vector2Int.zero }, Roads.Hops(w, Vector2Int.zero, Vector2Int.zero));
        }

        [Test]
        public void ADeliveryKnowsTheWayItsCourierRides()
        {
            var w = World.Build(Of("home/a", "work/a"), new Plots());
            var pile = new PileSnapshot();
            pile.rows.Add(new PileRow { key = "k", account = "one", from = "home/a", to = "work/a", receivedAt = 10 });
            var d = new Deliveries(5).New(pile, w).Single();
            CollectionAssert.AreEqual(Roads.Hops(w, d.fromAt, d.toAt), d.way);
            Assert.AreEqual(2, d.way.Count);
        }
    }
}
