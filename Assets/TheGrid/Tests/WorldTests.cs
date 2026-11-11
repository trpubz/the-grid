using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TheGrid.Core.Tests
{
    public class WorldTests
    {
        string tmp;
        RosterSnapshot snap;

        [SetUp]
        public void SetUp()
        {
            tmp = Path.Combine(Path.GetTempPath(), "grid-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            snap = JsonUtility.FromJson<RosterSnapshot>(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "fixtures", "world", "snapshot.json")));
        }

        [TearDown]
        public void TearDown() => Directory.Delete(tmp, true);

        [Test]
        public void PlotCellsSpiralOutFromTheCentreWithoutRepeating()
        {
            CollectionAssert.AreEqual(
                new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(0, 1), new Vector2Int(-1, 1), new Vector2Int(-1, 0), new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1), new Vector2Int(2, -1) },
                Enumerable.Range(0, 10).Select(Plots.Cell).ToArray());
            Assert.AreEqual(200, Enumerable.Range(0, 200).Select(Plots.Cell).Distinct().Count());
            // the first nine fill the 3 by 3 square round the centre
            Assert.IsTrue(Enumerable.Range(0, 9).Select(Plots.Cell).All(c => Math.Abs(c.x) <= 1 && Math.Abs(c.y) <= 1));
        }

        [Test]
        public void ARepoKeepsItsPlotForGood()
        {
            var plots = new Plots();
            Assert.AreEqual(0, plots.IndexFor("/work/alpha", out var added)); Assert.IsTrue(added);
            Assert.AreEqual(1, plots.IndexFor("/work/beta", out added)); Assert.IsTrue(added);
            Assert.AreEqual(0, plots.IndexFor("/work/alpha", out added)); Assert.IsFalse(added);
            Assert.AreEqual(2, plots.IndexFor("", out added)); Assert.IsTrue(added); // sessions with no repo share one plot
            var file = Path.Combine(tmp, "world.json");
            plots.Save(file);
            var again = Plots.Load(file);
            CollectionAssert.AreEqual(new[] { "/work/alpha", "/work/beta", "" }, again.repos);
            Assert.AreEqual(1, again.IndexFor("/work/beta", out added)); Assert.IsFalse(added);
        }

        [Test]
        public void AMissingOrCorruptPlotFileStartsEmptyAndTheBadOneIsKept()
        {
            Assert.AreEqual(0, Plots.Load(Path.Combine(tmp, "missing.json")).repos.Count);
            var file = Path.Combine(tmp, "world.json");
            File.WriteAllText(file, "{ broken");
            Assert.AreEqual(0, Plots.Load(file).repos.Count);
            Assert.AreEqual("{ broken", File.ReadAllText(file + ".bad"));
        }

        [Test]
        public void AFailedSaveIsTriedAgainAndAnUnchangedLayoutIsNotRewritten()
        {
            var plots = new Plots();
            plots.IndexFor("/work/alpha", out _);
            var blocker = Path.Combine(tmp, "a-file");
            File.WriteAllText(blocker, "");
            Assert.Catch<IOException>(() => plots.SaveIfGrown(Path.Combine(blocker, "world.json"))); // no folder can be made under a file

            var file = Path.Combine(tmp, "world.json");
            plots.SaveIfGrown(file); // the write that failed is still owed
            CollectionAssert.AreEqual(new[] { "/work/alpha" }, Plots.Load(file).repos);

            var loaded = Plots.Load(file);
            File.Delete(file);
            plots.SaveIfGrown(file);
            loaded.SaveIfGrown(file);
            Assert.IsFalse(File.Exists(file), "nothing was added, so nothing is written");

            loaded.IndexFor("/work/beta", out _);
            loaded.SaveIfGrown(file);
            CollectionAssert.AreEqual(new[] { "/work/alpha", "/work/beta" }, Plots.Load(file).repos);
        }

        [TestCase("Running", "editing", Building.Garage)]
        [TestCase("Running", "reading", Building.Library)]
        [TestCase("Running", "testing", Building.Lab)]
        [TestCase("Running", "reviewing", Building.ReviewHall)]
        [TestCase("Running", "working", Building.Workshop)]
        [TestCase("Running", "", Building.Workshop)]
        [TestCase("Running", "juggling", Building.Workshop)]
        [TestCase("BlockedOnQuestion", "", Building.Booth)]
        [TestCase("BlockedOnQuestion", "editing", Building.Booth)]
        [TestCase("WaitingForPermission", "", Building.Gate)]
        [TestCase("WaitingOther", "", Building.Gate)]
        [TestCase("BackgroundBlocked", "", Building.Gate)]
        [TestCase("Errored", "", Building.Yard)]
        [TestCase("Unknown", "", Building.Yard)]
        [TestCase("something new", "", Building.Yard)]
        [TestCase("Finished", "", Building.Lot)]
        [TestCase("Offline", "", Building.Depot)]
        public void WorkOrStateDecidesTheBuilding(string state, string activity, Building expected) => Assert.AreEqual(expected, World.BuildingFor(state, activity));

        [TestCase(2, 1, "2 need you · 1 done")]
        [TestCase(1, 0, "1 needs you")]
        [TestCase(1, 4, "1 needs you · 4 done")]
        [TestCase(0, 3, "3 done")]
        [TestCase(0, 0, "")]
        public void ACitySeenFromFarSaysHowManyNeedTheOwnerAndHowManyAreDone(int need, int done, string expected) =>
            Assert.AreEqual(expected, World.Summary(new WorldCity { needOwner = need, done = done }));

        [TestCase("BlockedOnQuestion", true)]
        [TestCase("WaitingForPermission", true)]
        [TestCase("WaitingOther", true)]
        [TestCase("BackgroundBlocked", true)]
        [TestCase("Errored", true)]
        [TestCase("Unknown", true)]
        [TestCase("Running", false)]
        [TestCase("Finished", false)]
        [TestCase("Offline", false)]
        public void OnlyStoppedAndStuckRidersNeedTheOwner(string state, bool expected) => Assert.AreEqual(expected, World.NeedsOwner(state));

        [Test]
        public void TheWorldAndTheRosterNeverDisagree()
        {
            var plots = new Plots();
            var w = World.Build(snap, plots);
            Assert.AreEqual(snap.generatedAt, w.generatedAt);
            CollectionAssert.AreEqual(new[] { "alpha", "beta", "No project" }, w.cities.Select(c => c.name).ToArray());
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, w.cities.Select(c => c.plot).ToArray());
            CollectionAssert.AreEqual(new[] { Plots.Cell(0), Plots.Cell(1), Plots.Cell(2) }, w.cities.Select(c => c.cell).ToArray());
            Assert.AreEqual(snap.agents.Count, w.cities.Sum(c => c.riders.Count));
            foreach (var a in snap.agents)
            {
                var found = w.cities.SelectMany(c => c.riders.Select(r => new { c, r })).Where(x => x.r.key == a.account + "|" + a.sessionId).ToList();
                Assert.AreEqual(1, found.Count, a.name);
                Assert.AreEqual(a.repoRoot, found[0].c.repoRoot);
                Assert.AreEqual(World.BuildingFor(a.state, a.activity), found[0].r.building);
                Assert.AreEqual(World.NeedsOwner(a.state), found[0].r.needsOwner);
                Assert.AreEqual(a.subagents.Count, found[0].r.subagents);
                Assert.AreEqual(a.name, found[0].r.name);
                Assert.AreEqual(a.account, found[0].r.account);
            }
            var alpha = w.cities[0];
            Assert.AreEqual(2, alpha.needOwner); // one blocked on a question, one waiting for permission
            Assert.AreEqual(1, alpha.done);
            Assert.AreEqual(3, alpha.running);
            Assert.AreEqual("Work", w.cities[1].workspace);
            foreach (var c in w.cities)
                foreach (var g in c.riders.GroupBy(r => r.building))
                    CollectionAssert.AreEqual(Enumerable.Range(0, g.Count()).ToArray(), g.OrderBy(r => r.key, StringComparer.Ordinal).Select(r => r.slot).ToArray());
        }

        [Test]
        public void TwoAccountsWithTheSameSessionAreTwoRiders()
        {
            var twin = new RosterSnapshot();
            twin.agents.Add(new AgentRow { sessionId = "s", name = "same", account = "one", state = "Finished", repoRoot = "/work/alpha", repoName = "alpha" });
            twin.agents.Add(new AgentRow { sessionId = "s", name = "same", account = "two", state = "Finished", repoRoot = "/work/alpha", repoName = "alpha" });
            var riders = World.Build(twin, new Plots()).cities[0].riders;
            Assert.AreEqual(2, riders.Count);
            CollectionAssert.AreEqual(new[] { 0, 1 }, riders.OrderBy(r => r.key, StringComparer.Ordinal).Select(r => r.slot).ToArray());
        }

        [Test]
        public void NothingToShowIsNotAnError()
        {
            Assert.AreEqual(0, World.Build(new RosterSnapshot(), new Plots()).cities.Count);
        }

        [Test]
        public void PlotsOutliveTheirRidersAndNewReposTakeTheNextPlot()
        {
            var plots = new Plots();
            var first = JsonUtility.ToJson(World.Build(snap, plots));
            Assert.AreEqual(first, JsonUtility.ToJson(World.Build(snap, plots))); // same input, same world
            snap.agents.RemoveAll(a => a.repoRoot == "/work/alpha");
            snap.agents.Add(new AgentRow { sessionId = "n", name = "gamma-01", account = "one", state = "Running", activity = "editing", repoRoot = "/work/gamma", repoName = "gamma" });
            var w = World.Build(snap, plots);
            CollectionAssert.AreEqual(new[] { "alpha", "beta", "No project", "gamma" }, w.cities.Select(c => c.name).ToArray());
            Assert.AreEqual(0, w.cities[0].riders.Count); // the plot stays, empty
            Assert.AreEqual(3, w.cities[3].plot);
            Assert.AreEqual(4, plots.repos.Count);
        }
    }
}
