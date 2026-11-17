using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TheGrid.Core.Tests
{
    public class ProgramCardTests
    {
        [Test]
        public void APictureIsEnlargedInWholePixelsTopRowFirstAndClearPixelsCarryNoColour()
        {
            Color32 a = new Color32(1, 2, 3, 255), b = new Color32(9, 9, 9, 0), c = new Color32(4, 5, 6, 255), d = new Color32(7, 8, 9, 255);
            var got = ProgramCard.Scaled(new[] { a, b, c, d }, 2, 2, 2);   // Unity order: bottom row a b, top row c d
            var clear = new Color32(0, 0, 0, 0);
            CollectionAssert.AreEqual(new[] { c, c, d, d, c, c, d, d, a, a, clear, clear, a, a, clear, clear }, got);
        }

        static AgentRow Agent(string name, string state, string activity = "", int pid = 0) =>
            new AgentRow { sessionId = name, name = name, account = "one", state = state, activity = activity, pid = pid };

        static RosterSnapshot Only(AgentRow a)
        {
            var s = new RosterSnapshot();
            s.agents.Add(a);
            return s;
        }

        [Test]
        public void TheCardIsTheFirstFiveRowsOfTheRosterInItsOrderAndSaysHowManyMore()
        {
            var snap = new RosterSnapshot();
            foreach (var n in new[] { "a", "b", "c", "d", "e", "f", "g" }) snap.agents.Add(Agent(n, "Running"));
            var v = ProgramCard.Of(snap);
            CollectionAssert.AreEqual(new[] { "a", "b", "c", "d", "e" }, v.rows.Select(r => r.name).ToArray());
            Assert.AreEqual(2, v.more);
            Assert.AreEqual("one|a", v.rows[0].key);
        }

        [Test]
        public void WithFiveOrFewerThereIsNoMoreAndWithNoneThereAreNoRows()
        {
            var three = new RosterSnapshot(); foreach (var n in new[] { "a", "b", "c" }) three.agents.Add(Agent(n, "Finished"));
            Assert.AreEqual(3, ProgramCard.Of(three).rows.Count); Assert.AreEqual(0, ProgramCard.Of(three).more);
            Assert.AreEqual(0, ProgramCard.Of(new RosterSnapshot()).rows.Count);
        }

        [TestCase("BlockedOnQuestion", "", "Answer a question", Badge.Question)]
        [TestCase("WaitingForPermission", "", "Approve a step", Badge.Approve)]
        [TestCase("Errored", "", "Failed", Badge.Fault)]
        [TestCase("Running", "reading", "reading", Badge.None)]
        [TestCase("Running", "", "working", Badge.None)]
        [TestCase("Finished", "", "Done", Badge.Done)]
        [TestCase("Offline", "", "Offline", Badge.None)]
        public void ARowSaysTheStateInAWordAndARunningOneWhatItIsDoing(string state, string activity, string word, Badge badge)
        {
            var row = ProgramCard.Of(Only(Agent("a", state, activity, 41))).rows[0];
            Assert.AreEqual(word, row.word); Assert.AreEqual(badge, row.badge); Assert.AreEqual(41, row.pid);
        }

        [Test]
        public void TheProgramIsLitWhenAnySessionNeedsTheOwnerAlsoOneNotOnTheCard()
        {
            var snap = new RosterSnapshot();
            foreach (var n in new[] { "a", "b", "c", "d", "e", "f" }) snap.agents.Add(Agent(n, "Running"));
            Assert.IsFalse(ProgramCard.Of(snap).lit);
            snap.agents[5].state = "WaitingForPermission";   // sixth: under "+1 more"
            Assert.IsTrue(ProgramCard.Of(snap).lit);
            Assert.IsFalse(ProgramCard.Of(Only(Agent("a", "Finished"))).lit);   // done is not waiting
        }

        [Test]
        public void ALongNameIsCutWithAnEllipsis()
        {
            var row = ProgramCard.Of(Only(Agent(new string('x', 60), "Running"))).rows[0];
            Assert.AreEqual(ProgramCard.NameLetters, row.name.Length);
            Assert.IsTrue(row.name.EndsWith("…"));
            Assert.AreEqual("one|" + new string('x', 60), row.key);   // the key is never cut
        }
    }
}
