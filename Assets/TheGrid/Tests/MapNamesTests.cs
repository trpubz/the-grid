using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace TheGrid.Core.Tests
{
    public class MapNamesTests
    {
        [Test]
        public void NoNameShowsWithNothingSelectedOrPointedAt() => CollectionAssert.IsEmpty(MapNames.Shown("", ""));

        [Test]
        public void TheSelectedBotIsNamed() => CollectionAssert.AreEqual(new[] { "a" }, MapNames.Shown("a", ""));

        [Test]
        public void TheBotUnderThePointerIsNamed() => CollectionAssert.AreEqual(new[] { "b" }, MapNames.Shown("", "b"));

        // The selected one is placed first: its name stands still while the pointer goes from bot to bot.
        [Test]
        public void TheSelectedBotComesFirst() => CollectionAssert.AreEqual(new[] { "a", "b" }, MapNames.Shown("a", "b"));

        [Test]
        public void ABotBothSelectedAndPointedAtIsNamedOnce() => CollectionAssert.AreEqual(new[] { "a" }, MapNames.Shown("a", "a"));

        [Test]
        public void ANameStaysWhereNothingIsInItsWay()
        {
            var wanted = new Rect(100, 100, 80, 20);
            Assert.AreEqual(wanted, MapNames.Clear(wanted, new List<Rect>()));
            Assert.AreEqual(wanted, MapNames.Clear(wanted, new List<Rect> { new Rect(100, 200, 80, 20) }));
        }

        [Test]
        public void ANameThatOnlyTouchesAnotherStays()
        {
            var wanted = new Rect(100, 100, 80, 20);
            Assert.AreEqual(wanted, MapNames.Clear(wanted, new List<Rect> { new Rect(100, 120, 80, 20), new Rect(180, 100, 80, 20) }));
        }

        [Test]
        public void ANameMovesDownUnderTheSectorsName() =>
            Assert.AreEqual(new Rect(100, 125, 80, 20), MapNames.Clear(new Rect(100, 100, 80, 20), new List<Rect> { new Rect(90, 105, 40, 20) }));

        // Whichever order they come in: moving clear of one may put it over another that was passed already.
        [Test]
        public void ANameMovesUntilItIsClearOfAllOfThem()
        {
            Rect near = new Rect(90, 105, 40, 20), next = new Rect(150, 130, 40, 20), far = new Rect(100, 155, 40, 20);
            foreach (var taken in new[] { new[] { near, next, far }, new[] { far, next, near }, new[] { next, near, far } })
                Assert.AreEqual(new Rect(100, 175, 80, 20), MapNames.Clear(new Rect(100, 100, 80, 20), taken));
        }

        // A name off the view has an empty plate, at the view's corner. It is in nobody's way.
        [Test]
        public void AnEmptyPlateIsInNobodysWay() =>
            Assert.AreEqual(new Rect(-40, -10, 80, 20), MapNames.Clear(new Rect(-40, -10, 80, 20), new List<Rect> { default }));
    }
}
