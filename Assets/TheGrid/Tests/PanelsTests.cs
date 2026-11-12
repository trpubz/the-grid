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

        static AgentRow Agent(string state, string activity = "", string raw = "") =>
            new AgentRow { sessionId = "s", name = "gamma-01", account = "one", repoName = "gamma", state = state, activity = activity, rawStatus = raw, stateSince = 1000 };

        static RosterSnapshot Only(AgentRow a)
        {
            var s = new RosterSnapshot();
            s.agents.Add(a);
            return s;
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
    }
}
