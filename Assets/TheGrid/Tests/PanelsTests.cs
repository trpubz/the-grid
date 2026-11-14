using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TheGrid.Core.Tests
{
    public class PanelsTests
    {
        RosterSnapshot snap;

        [SetUp]
        public void Load() => snap = JsonUtility.FromJson<RosterSnapshot>(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "fixtures", "world", "snapshot.json")));

        // A panel asked for at a point is put there, or as near as keeps it inside the window. One that is larger than the
        // window keeps its top left corner in sight, where its title and its controls are. The window is 800 by 600.
        [TestCase(100f, 80f, 200f, 100f, 100f, 80f)]
        [TestCase(700f, 550f, 200f, 100f, 600f, 500f)]
        [TestCase(-20f, -5f, 200f, 100f, 0f, 0f)]
        [TestCase(300f, 300f, 900f, 100f, 0f, 300f)]
        [TestCase(300f, 300f, 200f, 700f, 300f, 0f)]
        public void APanelStaysInTheWindow(float x, float y, float wide, float high, float atX, float atY) =>
            Assert.AreEqual(new Vector2(atX, atY), Panels.Fit(new Vector2(x, y), new Vector2(wide, high), new Rect(0, 0, 800, 600)));

        static AgentRow Agent(string state, string activity = "", string raw = "") =>
            new AgentRow { sessionId = "s", name = "gamma-01", account = "one", repoName = "gamma", state = state, activity = activity, rawStatus = raw, stateSince = 1000 };

        static RosterSnapshot Only(AgentRow a)
        {
            var s = new RosterSnapshot();
            s.agents.Add(a);
            return s;
        }

        [Test]
        public void TheCardCarriesTheWayIntoTheConversation()
        {
            var a = Agent("BackgroundBlocked");
            a.enter = "cd /work/gamma && claude attach bg000009";
            a.pid = 7;
            var card = Panels.Card(Only(a), "one|s", 1000);
            Assert.AreEqual("cd /work/gamma && claude attach bg000009", card.enter);
            Assert.AreEqual(7, card.pid);
            Assert.AreEqual("", card.where, "where it lives is asked of the machine, not of the snapshot");
        }

        [Test]
        public void NeedsYouIsExactlyTheBotsThatNeedTheOwnerInTheRostersOrder()
        {
            var want = snap.agents.Where(a => World.NeedsOwner(a.state)).Select(a => a.account + "|" + a.sessionId).ToList();
            var rows = Panels.NeedsYou(snap);
            CollectionAssert.AreEqual(want, rows.Select(r => r.key).ToList());
            Assert.AreEqual(6, rows.Count);   // alpha 2, beta 3, no project 1
            Assert.AreEqual("alpha", rows[0].sector);
            Assert.AreEqual("alpha-01", rows[0].name);
            Assert.AreEqual(Badge.Question, rows[0].badge);
            Assert.AreEqual(World.NoProject, rows.Last().sector);
            Assert.AreEqual(0, Panels.NeedsYou(new RosterSnapshot()).Count);
        }

        [TestCase("BlockedOnQuestion", "", "Answer a question")]
        [TestCase("WaitingForPermission", "", "Approve a step")]
        [TestCase("WaitingOther", "", "Waiting")]
        [TestCase("BackgroundBlocked", "", "Blocked in the background")]
        [TestCase("Errored", "", "Failed")]
        [TestCase("Unknown", "compacting", "Unknown: compacting")]
        [TestCase("something new", "", "Unknown: something new")]   // a state this build has never heard of, and no words from the app
        public void ARowSaysWhatIsWantedOfTheOwner(string state, string raw, string what) =>
            Assert.AreEqual(what, Panels.NeedsYou(Only(Agent(state, "", raw)))[0].what);

        [TestCase("Running", "reading", "Reading")]
        [TestCase("Running", "editing", "Editing")]
        [TestCase("Running", "testing", "Testing")]
        [TestCase("Running", "reviewing", "Reviewing")]
        [TestCase("Running", "working", "Working")]
        [TestCase("Running", "", "Working")]
        [TestCase("Finished", "", "Done")]
        [TestCase("Offline", "", "Offline")]
        [TestCase("BlockedOnQuestion", "", "Answer a question")]
        [TestCase("Errored", "", "Failed")]
        public void ACardIsTitledWithWhatTheBotIsDoing(string state, string activity, string title) =>
            Assert.AreEqual(title, Panels.Card(Only(Agent(state, activity)), "one|s", 1000).title);

        [Test]
        public void ACardSaysWhoWhereAndForHowLong()
        {
            var a = Agent("Running", "editing");
            a.ticket = "DEMO-7";
            a.prUrl = "https://example.com/pull/7";
            a.prNumber = 7;
            var card = Panels.Card(Only(a), "one|s", 1000 + 5 * 60 * 1000);
            Assert.AreEqual("one|s", card.key);
            Assert.AreEqual("gamma-01 · one", card.who);
            Assert.AreEqual("gamma · DEMO-7", card.doing);
            Assert.AreEqual(RosterFormat.Elapsed(1000, 1000 + 5 * 60 * 1000), card.since);
            Assert.AreEqual("https://example.com/pull/7", card.prUrl);
            Assert.AreEqual(7, card.prNumber);

            a.ticket = "";
            a.repoName = "";
            Assert.AreEqual(World.NoProject, Panels.Card(Only(a), "one|s", 1000).doing);
        }

        static AgentRow With(int subagents)
        {
            var a = Agent("Running", "editing");
            a.workspace = "Work";
            a.repoRoot = "/work/gamma";
            for (int i = 0; i < subagents; i++) a.subagents.Add(new SubagentRow { agentId = "a" + i, description = "task " + i, agentType = "general-purpose" });
            return a;
        }

        [Test]
        public void ACardSaysWhichWorkspaceAndWhatItsSubagentsAreDoing()
        {
            var card = Panels.Card(Only(With(2)), "one|s", 1000);
            Assert.AreEqual("Work", card.workspace);
            Assert.AreEqual("/work/gamma", card.repoRoot);
            CollectionAssert.AreEqual(new[] { "task 0", "task 1" }, card.subagents);
            Assert.AreEqual(0, Panels.Card(Only(Agent("Running")), "one|s", 1000).subagents.Count);
        }

        [TestCase(0, 4, "")]
        [TestCase(2, 4, "↳ task 0|↳ task 1")]
        [TestCase(4, 4, "↳ task 0|↳ task 1|↳ task 2|↳ task 3")]            // exactly what fits: no line spent on saying so
        [TestCase(6, 4, "↳ task 0|↳ task 1|↳ task 2|and 3 more")]
        [TestCase(6, 2, "↳ task 0|and 5 more")]
        [TestCase(2, 1, "2 subagents")]                                     // room for one line only
        [TestCase(1, 1, "↳ task 0")]
        [TestCase(3, 0, "")]
        public void ACardListsAsManySubagentsAsFitAndCountsTheRest(int subagents, int lines, string expected) =>
            Assert.AreEqual(expected, string.Join("|", Panels.SubagentLines(Panels.Card(Only(With(subagents)), "one|s", 1000), lines)));

        [Test]
        public void ACardOffersOnlyAWebLink()
        {
            var a = Agent("Running");
            a.prUrl = "file:///etc/passwd";
            a.prNumber = 3;
            Assert.AreEqual("", Panels.Card(Only(a), "one|s", 1000).prUrl);
        }

        [Test]
        public void NoAgentNoCard()
        {
            Assert.IsNull(Panels.Card(snap, "no|such", 1000));
            Assert.IsNull(Panels.Card(snap, "", 1000));
            Assert.IsNull(Panels.Card(new RosterSnapshot(), "one|s", 1000));
        }

        // The depot's list stands over the card, under the title of "Needs you": between a floor and a ceiling.
        [Test]
        public void AListOverTheCardIsAsHighAsItWantsWhereThereIsRoom()
        {
            var map = new Rect(0, 32, 900, 700);
            var r = Panels.Above(map, floor: 568, ceiling: 75, wide: 330, wanted: 217, least: 47, margin: 8);
            Assert.AreEqual(new Rect(900 - 8 - 330, 568 - 8 - 217, 330, 217), r);
        }

        [Test]
        public void InAShortMapTheListTakesOnlyTheRoomThereIsAndItsTitleStaysInView()
        {
            // a map 300 high from 32 down, a card with four lines about subagents from 160 down, five pull requests
            var map = new Rect(0, 32, 400, 300);
            var r = Panels.Above(map, floor: 160, ceiling: 75, wide: 330, wanted: 217, least: 47, margin: 8);
            Assert.AreEqual(75, r.y, "its title is under the title of Needs you, not above the window");
            Assert.AreEqual(160 - 8, r.yMax, "and its foot is clear of the card");
            Assert.AreEqual(330, r.width);
            Assert.GreaterOrEqual(r.xMin, map.xMin);
        }

        [Test]
        public void WhereNotEvenATitleAndPartOfARowFitThereIsNoList()
        {
            var map = new Rect(0, 32, 400, 200);
            Assert.AreEqual(default(Rect), Panels.Above(map, floor: 110, ceiling: 75, wide: 330, wanted: 217, least: 47, margin: 8));
            // and a list is never wider than the map has room for
            Assert.AreEqual(184, Panels.Above(new Rect(0, 32, 200, 700), 600, 75, 330, 217, 47, 8).width);
        }
    }
}
