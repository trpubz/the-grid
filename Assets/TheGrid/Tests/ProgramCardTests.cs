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

        [Test]
        public void ANameIsNeverCutInsideAnEmoji()
        {
            var name = new string('x', ProgramCard.NameLetters - 2) + "\U0001F600" + "more letters";
            var cut = ProgramCard.Of(Only(Agent(name, "Running"))).rows[0].name;
            Assert.IsTrue(cut.EndsWith("…"));
            Assert.LessOrEqual(cut.Length, ProgramCard.NameLetters);
            for (var i = 0; i < cut.Length; i++)
            {
                if (char.IsHighSurrogate(cut[i])) Assert.IsTrue(i + 1 < cut.Length && char.IsLowSurrogate(cut[i + 1]), "lone high surrogate at " + i);
                if (char.IsLowSurrogate(cut[i])) Assert.IsTrue(i > 0 && char.IsHighSurrogate(cut[i - 1]), "lone low surrogate at " + i);
            }
        }

        static readonly Rect Screen = new Rect(0, 80, 1440, 820);   // a Dock 80 high at the bottom

        [Test]
        public void TheFigureStandsAtTheBottomRightOfWhateverTheScreenShowsAndWithItsBadgeItIsTheWholeFrame()
        {
            foreach (var screen in new[] { Screen, new Rect(100, 0, 1820, 1050) })   // Dock on the left, another size
            {
                var l = ProgramCard.Layout(screen, 3, false, false);
                Assert.AreEqual(new Rect(screen.xMax - 16 - 120, screen.y + 16, 120, 189), l.frame);   // nothing clear beyond the figure and its badge
                Assert.AreEqual(new Rect(0, 0, 120, 144), l.figure);
                Assert.AreEqual(new Rect(36, 129, 48, 60), l.badge);   // centred over the head, its foot in the picture's clear rows
                Assert.AreEqual(0, l.rows.Count);
            }
        }

        [Test]
        public void TheCardStandsOverTheFigureItsRowsInTheRostersOrderTopDownNoneOverlappingAllInside()
        {
            var l = ProgramCard.Layout(Screen, 5, true, true);
            Assert.GreaterOrEqual(l.card.yMin, l.badge.yMax);
            Assert.AreEqual(5, l.rows.Count);
            for (int i = 1; i < 5; i++) Assert.LessOrEqual(l.rows[i].yMax, l.rows[i - 1].yMin, "row " + i + " is under row " + (i - 1));
            Assert.LessOrEqual(l.more.yMax, l.rows[4].yMin);
            foreach (var r in l.rows.Concat(new[] { l.more, l.pin, l.figure, l.badge }))
                Assert.IsTrue(r.xMin >= 0 && r.yMin >= 0 && r.xMax <= l.frame.width && r.yMax <= l.frame.height, r + " is outside the frame");
            foreach (var r in l.rows.Concat(new[] { l.more, l.pin })) Assert.IsTrue(l.card.Contains(r.center));
            Assert.IsFalse(l.rows.Any(r => r.Overlaps(l.pin)));
            Assert.LessOrEqual(l.frame.xMax, Screen.xMax); Assert.GreaterOrEqual(l.frame.xMin, Screen.xMin); Assert.LessOrEqual(l.frame.yMax, Screen.yMax);
            Assert.AreEqual(Screen.xMax - 16, l.frame.xMax);          // the figure does not move when the card comes and goes
            Assert.AreEqual(l.frame.width - 120, l.figure.xMin);
        }

        [Test]
        public void WithNoRowsThereIsNoCardEvenWhenAskedFor()
        {
            var l = ProgramCard.Layout(Screen, 0, false, true);
            Assert.AreEqual(default(Rect), l.card);
            Assert.AreEqual(new Rect(0, 0, 120, 189), new Rect(0, 0, l.frame.width, l.frame.height));
            Assert.IsFalse(ProgramCard.Shows(true, 0, true, 0, 0));   // pinned and near, and still nothing to show
        }

        [Test]
        public void APointHitsTheRowThePinTheMoreLineOrTheFigureAndTheClearPartNothing()
        {
            var l = ProgramCard.Layout(Screen, 5, true, true);
            for (int i = 0; i < 5; i++) { Assert.AreEqual(ProgramHit.Row, ProgramCard.Hit(l, l.rows[i].center, out int row)); Assert.AreEqual(i, row); }
            Assert.AreEqual(ProgramHit.Pin, ProgramCard.Hit(l, l.pin.center, out _));
            Assert.AreEqual(ProgramHit.More, ProgramCard.Hit(l, l.more.center, out _));
            Assert.AreEqual(ProgramHit.Figure, ProgramCard.Hit(l, l.figure.center, out _));
            Assert.AreEqual(ProgramHit.None, ProgramCard.Hit(l, new Vector2(2, 2), out _));   // beside the figure, under the card's left part
        }

        [Test]
        public void ThePointerIsNearOnTheFigureAndALittleAroundItAndOnTheCardOnlyWhileTheCardIsThere()
        {
            var shut = ProgramCard.Layout(Screen, 5, false, false);
            Assert.IsTrue(ProgramCard.Near(shut, shut.frame.center));
            Assert.IsTrue(ProgramCard.Near(shut, new Vector2(shut.frame.xMin - 20, shut.frame.center.y)));
            Assert.IsFalse(ProgramCard.Near(shut, new Vector2(shut.frame.xMin - 40, shut.frame.center.y)));
            var open = ProgramCard.Layout(Screen, 5, false, true);
            var onCard = new Vector2(open.frame.x + open.card.center.x, open.frame.y + open.card.center.y);
            Assert.IsTrue(ProgramCard.Near(open, onCard));
            Assert.IsFalse(ProgramCard.Near(shut, onCard));   // where the card would be is not near while it is shut
        }

        [TestCase(false, true, 5000, 0, true)]      // pointer near
        [TestCase(false, false, 5000, 4500, true)]  // left half a second ago: still up
        [TestCase(false, false, 5000, 4000, false)] // left a second ago: gone
        [TestCase(true, false, 5000, 0, true)]      // pinned
        public void TheCardShowsWhilePinnedOrNearAndForAShortTimeAfter(bool pinned, bool near, long now, long leftAt, bool shows) =>
            Assert.AreEqual(shows, ProgramCard.Shows(pinned, 3, near, now, leftAt));

        static bool Inside(Rect outer, Rect inner) =>
            inner.xMin >= outer.xMin && inner.xMax <= outer.xMax && inner.yMin >= outer.yMin && inner.yMax <= outer.yMax;

        // A row of a card: badge or dot at its left, the word at its right and never wider than half the row, the name between
        // them, overlapping neither, whatever width the word is measured at; the text lines in the middle of the row.
        [TestCase(0f)]
        [TestCase(40f)]
        [TestCase(132f)]
        [TestCase(500f)]
        [TestCase(-5f)]
        public void ARowsPartsSitInsideItBadgeLeftWordRightNameBetween(float measured)
        {
            var row = ProgramCard.Layout(Screen, 5, true, true).rows[2];
            var p = ProgramCard.Parts(row, measured);
            foreach (var r in new[] { p.badge, p.dot, p.name, p.word }) Assert.IsTrue(Inside(row, r), r + " in " + row);
            Assert.AreEqual(row.x, p.badge.x);
            Assert.AreEqual(new Vector2(16, 20), p.badge.size);   // one point to each pixel of the badge's art
            Assert.AreEqual(row.center.y, p.badge.center.y, 1e-4);
            Assert.AreEqual(p.badge.center, p.dot.center);         // the dot stands where a badge would
            Assert.AreEqual(row.xMax, p.word.xMax, 1e-4);
            Assert.AreEqual(Mathf.Clamp(measured, 0, row.width / 2), p.word.width, 1e-4);
            Assert.GreaterOrEqual(p.name.xMin, p.badge.xMax);
            Assert.LessOrEqual(p.name.xMax, p.word.xMin);
            Assert.IsFalse(p.name.Overlaps(p.word) || p.name.Overlaps(p.badge) || p.name.Overlaps(p.dot));
            Assert.AreEqual(new[] { 22f, 8f }, new[] { p.name.x - row.x, p.word.xMin - p.name.xMax });   // room for the badge; a gap before the word
            foreach (var line in new[] { p.name, p.word })
            {
                Assert.AreEqual(16, line.height);
                Assert.AreEqual(row.center.y, line.center.y, 1e-4);
            }
        }

        // The pin is a 10 point square in the middle of its place; "+N more" is a line of text in line with the names.
        [Test]
        public void ThePinsSquareIsInTheMiddleOfThePinAndTheMoreLineIsInLineWithTheNames()
        {
            var l = ProgramCard.Layout(Screen, 5, true, true);
            var mark = ProgramCard.PinMark(l.pin);
            Assert.AreEqual(new Vector2(10, 10), mark.size);
            Assert.AreEqual(l.pin.center, mark.center);
            var line = ProgramCard.MoreLine(l.more);
            Assert.IsTrue(Inside(l.more, line));
            Assert.AreEqual(ProgramCard.Parts(l.rows[0], 40).name.x, line.x);
            Assert.AreEqual(l.more.xMax, line.xMax, 1e-4);
            Assert.AreEqual(16, line.height);
            Assert.AreEqual(l.more.center.y, line.center.y, 1e-4);
        }
    }
}
