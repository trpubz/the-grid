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

        [TestCase(2, 1, "2 need you · 1 done")]
        [TestCase(1, 0, "1 needs you")]
        [TestCase(1, 4, "1 needs you · 4 done")]
        [TestCase(0, 3, "3 done")]
        [TestCase(0, 0, "")]
        public void ASectorSeenFromFarSaysHowManyNeedTheOwnerAndHowManyAreDone(int need, int done, string expected) =>
            Assert.AreEqual(expected, World.Summary(new WorldSector { needOwner = need, done = done }));

        [TestCase(0, 0, 3, "3 PRs for you")]
        [TestCase(2, 1, 1, "2 need you · 1 done · 1 PR for you")]
        public void ASectorSeenFromFarAlsoSaysHowManyPullRequestsAreForTheOwner(int need, int done, int pulls, string expected) =>
            Assert.AreEqual(expected, World.Summary(new WorldSector { needOwner = need, done = done, pullsForOwner = pulls }));

        [Test]
        public void ASectorHasItsReposOpenPullRequestsAsLastHeardFromGitHub()
        {
            var pulls = new PullsSnapshot();
            pulls.repos.Add(new RepoPulls
            {
                repoRoot = "/work/alpha",
                open =
                {
                    new PullRequest { number = 7, state = PullState.ForOwner, status = "Ready to merge" },
                    new PullRequest { number = 9, state = PullState.ForOwner, status = "Your review is asked for" },
                    new PullRequest { number = 8, state = PullState.Draft, status = "Draft" },
                },
            });
            pulls.repos.Add(new RepoPulls { repoRoot = "/work/beta", problem = "gh: not signed in" });
            var w = World.Build(snap, new Plots(), null, pulls);
            var alpha = w.sectors.Single(s => s.name == "alpha");
            CollectionAssert.AreEqual(new[] { 7, 9, 8 }, alpha.pulls.Select(p => p.number).ToArray());
            Assert.AreEqual(2, alpha.pullsForOwner);
            Assert.AreEqual("", alpha.pullsProblem);
            var beta = w.sectors.Single(s => s.name == "beta");
            Assert.AreEqual(0, beta.pulls.Count);
            Assert.AreEqual("gh: not signed in", beta.pullsProblem);
            // not asked about, or not asked yet: nothing, and nothing wrong
            var loose = w.sectors.Single(s => s.name == World.NoProject);
            Assert.AreEqual(0, loose.pulls.Count);
            Assert.AreEqual("", loose.pullsProblem);
            Assert.AreEqual(0, World.Build(snap, new Plots()).sectors[0].pulls.Count);
        }

        // State is on the bot: a building stands for a kind of work, and everything else is where the bot stands, how, and its badge.
        [TestCase("Running", "reading", Place.ServerTower, Stance.Inspect, Badge.None)]
        [TestCase("Running", "editing", Place.Works, Stance.Type, Badge.None)]       // the depot is for pull requests; editing is done at the works
        [TestCase("Running", "testing", Place.Workshop, Stance.Type, Badge.None)]
        [TestCase("Running", "reviewing", Place.ReviewHall, Stance.Inspect, Badge.None)]
        [TestCase("Running", "working", Place.Works, Stance.Stand, Badge.None)]
        [TestCase("Running", "", Place.Works, Stance.Stand, Badge.None)]
        [TestCase("Running", "juggling", Place.Works, Stance.Stand, Badge.None)]
        [TestCase("BlockedOnQuestion", "", Place.Forecourt, Stance.Stand, Badge.Question)]
        [TestCase("BlockedOnQuestion", "editing", Place.Forecourt, Stance.Stand, Badge.Question)]   // a stale activity changes nothing
        [TestCase("WaitingForPermission", "", Place.Forecourt, Stance.Stand, Badge.Approve)]
        [TestCase("WaitingOther", "", Place.Forecourt, Stance.Stand, Badge.Approve)]
        [TestCase("BackgroundBlocked", "", Place.Forecourt, Stance.Stand, Badge.Approve)]
        [TestCase("Errored", "", Place.Forecourt, Stance.Stand, Badge.Fault)]
        [TestCase("Unknown", "", Place.Forecourt, Stance.Stand, Badge.Fault)]
        [TestCase("something new", "", Place.Forecourt, Stance.Stand, Badge.Fault)]                  // a state this build has never heard of
        [TestCase("Finished", "", Place.Plaza, Stance.Stand, Badge.Done)]
        [TestCase("Offline", "", Place.Dock, Stance.Off, Badge.None)]
        public void StateDecidesWhereABotStandsHowAndItsBadge(string state, string activity, Place place, Stance stance, Badge badge)
        {
            Assert.AreEqual(place, World.PlaceFor(state, activity));
            Assert.AreEqual(stance, World.StanceFor(state, activity));
            Assert.AreEqual(badge, World.BadgeFor(state));
            Assert.AreEqual(badge == Badge.Question || badge == Badge.Approve || badge == Badge.Fault, World.NeedsOwner(state));
        }

        [TestCase("BlockedOnQuestion", true)]
        [TestCase("WaitingForPermission", true)]
        [TestCase("WaitingOther", true)]
        [TestCase("BackgroundBlocked", true)]
        [TestCase("Errored", true)]
        [TestCase("Unknown", true)]
        [TestCase("Running", false)]
        [TestCase("Finished", false)]
        [TestCase("Offline", false)]
        public void OnlyStoppedAndStuckBotsNeedTheOwner(string state, bool expected) => Assert.AreEqual(expected, World.NeedsOwner(state));

        [Test]
        public void TheWorldAndTheRosterNeverDisagree()
        {
            var plots = new Plots();
            var w = World.Build(snap, plots);
            Assert.AreEqual(snap.generatedAt, w.generatedAt);
            CollectionAssert.AreEqual(new[] { "alpha", "beta", "No project" }, w.sectors.Select(c => c.name).ToArray());
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, w.sectors.Select(c => c.plot).ToArray());
            // alpha and the sessions with no repo are Personal; beta is Work, so it stands apart, in a region of its own to the
            // right of alpha, eight tiles from its lane. The third sector takes the nearest place of Personal's grid that keeps
            // that distance from beta: in front of alpha.
            CollectionAssert.AreEqual(new[] { new Vector2Int(0, 0), new Vector2Int(24, 0), new Vector2Int(0, 19) }, w.sectors.Select(c => c.origin).ToArray());
            CollectionAssert.AreEqual(new[] { "Personal", "Work" }, w.regions.Select(r => r.name).ToArray());
            Assert.AreEqual(snap.agents.Count, w.sectors.Sum(c => c.bots.Count));
            foreach (var a in snap.agents)
            {
                var found = w.sectors.SelectMany(c => c.bots.Select(r => new { c, r })).Where(x => x.r.key == a.account + "|" + a.sessionId).ToList();
                Assert.AreEqual(1, found.Count, a.name);
                Assert.AreEqual(a.repoRoot, found[0].c.repoRoot);
                Assert.AreEqual(World.NeedsOwner(a.state), found[0].r.needsOwner);
                Assert.AreEqual(World.PlaceFor(a.state, a.activity), found[0].r.place);
                Assert.AreEqual(World.StanceFor(a.state, a.activity), found[0].r.stance);
                Assert.AreEqual(World.BadgeFor(a.state), found[0].r.badge);
                Assert.AreEqual(a.subagents.Count, found[0].r.subagents);
                Assert.AreEqual(a.name, found[0].r.name);
                Assert.AreEqual(a.account, found[0].r.account);
            }
            var alpha = w.sectors[0];
            Assert.AreEqual(2, alpha.needOwner); // one blocked on a question, one waiting for permission
            Assert.AreEqual(1, alpha.done);
            Assert.AreEqual(6, alpha.running);
            Assert.AreEqual("Work", w.sectors[1].workspace);
            foreach (var c in w.sectors)
            {
                foreach (var g in c.bots.GroupBy(r => r.place))
                    CollectionAssert.AreEqual(Enumerable.Range(0, g.Count()).ToArray(), g.OrderBy(r => r.key, StringComparer.Ordinal).Select(r => r.slot).ToArray());
            }
        }

        static RosterSnapshot Of(params string[] accounts)
        {
            var s = new RosterSnapshot();
            for (int i = 0; i < accounts.Length; i++)
                s.agents.Add(new AgentRow { sessionId = "s" + i, name = "n" + i, account = accounts[i], state = "Running", repoRoot = "/work/alpha", repoName = "alpha" });
            return s;
        }

        static int Colour(WorldModel w, string account) => w.sectors.SelectMany(c => c.bots).First(b => b.account == account).colour;

        [Test]
        public void AnAccountKeepsItsColourWhenOthersComeAndGo()
        {
            var plots = new Plots();
            var w = World.Build(Of("two", "one", "two"), plots);
            Assert.AreEqual(0, Colour(w, "two"));   // in the order they were first seen, not the alphabet's
            Assert.AreEqual(1, Colour(w, "one"));
            Assert.IsTrue(w.sectors[0].bots.Where(b => b.account == "two").All(b => b.colour == 0));

            // the first account's last session ends: the other does not change
            Assert.AreEqual(1, Colour(World.Build(Of("one"), plots), "one"));
            // a third turns up, earlier in the alphabet: it takes the next colour and moves nobody
            w = World.Build(Of("aaa", "one"), plots);
            Assert.AreEqual(2, Colour(w, "aaa"));
            Assert.AreEqual(1, Colour(w, "one"));
            // and the first is still itself when it comes back
            Assert.AreEqual(0, Colour(World.Build(Of("two"), plots), "two"));
        }

        [Test]
        public void AccountColoursAreKeptInTheWorldsFile()
        {
            string file = Path.Combine(tmp, "world.json");
            var plots = new Plots();
            World.Build(Of("two", "one"), plots);
            plots.Save(file);
            var again = Plots.Load(file);
            CollectionAssert.AreEqual(new[] { "two", "one" }, again.accounts);
            Assert.AreEqual(1, Colour(World.Build(Of("one"), again), "one"));

            // a new account is owed to the file, like a new repo
            File.Delete(file);
            again.SaveIfGrown(file);
            Assert.IsFalse(File.Exists(file), "nothing new: nothing written");
            World.Build(Of("one", "three"), again);
            again.SaveIfGrown(file);
            CollectionAssert.AreEqual(new[] { "two", "one", "three" }, Plots.Load(file).accounts);
        }

        [Test]
        public void TheWorldOpensWhereThereIsSomeoneToSee()
        {
            // on the fixture: the first sector with a bot that needs the owner
            Assert.AreEqual("alpha", World.Opening(World.Build(snap, new Plots())).name);

            // Plot 0 belongs for good to a repo whose sessions are long gone. Nobody needs the owner: open on the first sector
            // that has anyone in it, not on empty concrete.
            var plots = new Plots();
            plots.IndexFor("/work/gone", out _);
            var quiet = new RosterSnapshot();
            quiet.agents.Add(new AgentRow { sessionId = "g", name = "gamma-01", account = "one", state = "Running", activity = "editing", repoRoot = "/work/gamma", repoName = "gamma" });
            Assert.AreEqual("gamma", World.Opening(World.Build(quiet, plots)).name);

            // a sector with a waiting bot comes before a busy one that is listed first
            quiet.agents.Add(new AgentRow { sessionId = "d", name = "delta-01", account = "one", state = "BlockedOnQuestion", repoRoot = "/work/delta", repoName = "delta" });
            Assert.AreEqual("delta", World.Opening(World.Build(quiet, plots)).name);

            // nobody anywhere, as before the first roster arrives: nowhere to open yet
            Assert.IsNull(World.Opening(World.Build(new RosterSnapshot(), plots)));
            Assert.IsNull(World.Opening(new WorldModel()));
        }

        [Test]
        public void TwoAccountsWithTheSameSessionAreTwoBots()
        {
            var twin = new RosterSnapshot();
            twin.agents.Add(new AgentRow { sessionId = "s", name = "same", account = "one", state = "Finished", repoRoot = "/work/alpha", repoName = "alpha" });
            twin.agents.Add(new AgentRow { sessionId = "s", name = "same", account = "two", state = "Finished", repoRoot = "/work/alpha", repoName = "alpha" });
            var bots = World.Build(twin, new Plots()).sectors[0].bots;
            Assert.AreEqual(2, bots.Count);
            CollectionAssert.AreEqual(new[] { 0, 1 }, bots.OrderBy(r => r.key, StringComparer.Ordinal).Select(r => r.slot).ToArray());
        }

        [Test]
        public void NothingToShowIsNotAnError()
        {
            Assert.AreEqual(0, World.Build(new RosterSnapshot(), new Plots()).sectors.Count);
        }

        [Test]
        public void PlotsOutliveTheirBotsAndNewReposTakeTheNextPlot()
        {
            var plots = new Plots();
            var first = JsonUtility.ToJson(World.Build(snap, plots));
            Assert.AreEqual(first, JsonUtility.ToJson(World.Build(snap, plots))); // same input, same world
            snap.agents.RemoveAll(a => a.repoRoot == "/work/alpha");
            snap.agents.Add(new AgentRow { sessionId = "n", name = "gamma-01", account = "one", state = "Running", activity = "editing", repoRoot = "/work/gamma", repoName = "gamma" });
            var w = World.Build(snap, plots);
            CollectionAssert.AreEqual(new[] { "alpha", "beta", "No project", "gamma" }, w.sectors.Select(c => c.name).ToArray());
            Assert.AreEqual(0, w.sectors[0].bots.Count); // the plot stays, empty
            Assert.AreEqual(3, w.sectors[3].plot);
            Assert.AreEqual(4, plots.repos.Count);
        }
    }
}
