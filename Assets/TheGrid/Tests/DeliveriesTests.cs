using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TheGrid.Core.Tests
{
    public class DeliveriesTests
    {
        const long Opened = 1794000092000;   // the world was opened between the two messages being sent
        RosterSnapshot snap;
        PileSnapshot pile;

        [SetUp]
        public void Load()
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "fixtures", "world");
            snap = JsonUtility.FromJson<RosterSnapshot>(File.ReadAllText(Path.Combine(dir, "snapshot.json")));
            pile = JsonUtility.FromJson<PileSnapshot>(File.ReadAllText(Path.Combine(dir, "messages.json")));
            world = World.Build(snap, new Plots());
        }

        static PileSnapshot Only(PileRow row)
        {
            var p = new PileSnapshot();
            p.rows.Add(row);
            return p;
        }

        static PileRow Row(string from, string to, string account, long receivedAt, string key = "k") =>
            new PileRow { key = key, from = from, to = to, account = account, at = receivedAt - 1000, receivedAt = receivedAt };

        [Test]
        public void AMessageReceivedAfterTheWorldOpenedIsOneDelivery()
        {
            var got = new Deliveries(Opened).New(pile, world);
            Assert.AreEqual(2, got.Count);
            var between = got.Single(d => d.key == "m-0001");
            Assert.AreEqual("two|00000000-0000-4000-8000-000000000004", between.fromBot);   // alpha-04
            Assert.AreEqual("two|00000000-0000-4000-8000-000000000008", between.toBot);     // beta-01
            Assert.AreEqual(world.sectors[0].origin, between.fromAt);
            Assert.AreEqual(world.sectors[1].origin, between.toAt);
            Assert.AreEqual(1794000095000, between.receivedAt);
            var within = got.Single(d => d.key == "m-0002");
            Assert.AreEqual(within.fromAt, within.toAt);
            // between two sectors a courier rides the lanes and a road; within one there are none to ride
            Assert.Greater(Iso.Route(between.way).Count, 1);
            CollectionAssert.AreEqual(new[] { within.fromAt }, within.way);
            Assert.AreEqual(0, Iso.Route(within.way).Count);
        }

        // True when a point of the map, in tiles, is on a building, the relay or the fountain of that sector.
        static bool Built(WorldSector s, Vector2 at)
        {
            foreach (Structure kind in Enum.GetValues(typeof(Structure)))
            {
                var f = s.layout.Footprint(kind);
                if (at.x > s.origin.x + f.xMin - .5f && at.x < s.origin.x + f.xMax - .5f && at.y > s.origin.y + f.yMin - .5f && at.y < s.origin.y + f.yMax - .5f) return true;
            }
            return false;
        }

        static void AssertRidesRoundEverything(List<Vector2> ride, string who)
        {
            for (int i = 1; i < ride.Count; i++)
            {
                Vector2 p = ride[i - 1], q = ride[i];
                Assert.AreNotEqual(p, q, who + ": a leg of no length");
                Assert.IsTrue(p.x == q.x || p.y == q.y, who + ": a courier turns square corners: " + p + " to " + q);
                // and never rides a stretch only to come straight back along it
                if (i > 1) Assert.GreaterOrEqual(Vector2.Dot(q - p, p - ride[i - 2]), 0f, who + ": doubles back at " + p + ", from " + ride[i - 2] + " and on to " + q);
                for (float k = 0; k <= (q - p).magnitude; k += .25f)
                    foreach (var sector in world.sectors) Assert.IsFalse(Built(sector, Vector2.MoveTowards(p, q, k)), who + ": rides through something of " + sector.name + " at " + Vector2.MoveTowards(p, q, k));
            }
        }

        static WorldModel world;

        [Test]
        public void ACourierRidesFromTheFrontOfOneBuildingToTheFrontOfAnotherRoundEverythingBetween()
        {
            var got = new Deliveries(Opened).New(pile, world);
            Delivery between = got.Single(d => d.key == "m-0001"), within = got.Single(d => d.key == "m-0002");
            WorldSector alpha = world.sectors[0], beta = world.sectors[1];
            WorldBot Bot(WorldSector s, string name) => s.bots.Single(b => b.name == name);
            Vector2 Before(WorldSector s, WorldBot b) => new Vector2(s.origin.x, s.origin.y) + s.layout.Front(b.place, b.slot);

            // between two sectors: from the front of the sender's building, over its plot to its crossing, by the lanes and
            // the road, and over the recipient's plot to the front of its building
            WorldBot sender = Bot(alpha, "alpha-04"), recipient = Bot(beta, "beta-01");
            var ride = Iso.RideTiles(alpha.origin, alpha.layout, sender.place, sender.slot, beta.origin, beta.layout, recipient.place, recipient.slot, between.way);
            Assert.AreEqual(Before(alpha, sender), ride[0]);
            Assert.AreEqual(Before(beta, recipient), ride[ride.Count - 1]);
            // all of the lanes' way but its two ends, the crossings: a crossing is ridden through only where it is on the way
            var lanes = Iso.Route(between.way);
            var middle = lanes.GetRange(1, lanes.Count - 2);
            int at = ride.IndexOf(middle[0]);
            Assert.Greater(at, 0, "it comes to the lanes over the plot");
            CollectionAssert.AreEqual(middle, ride.GetRange(at, middle.Count));
            AssertRidesRoundEverything(ride, "between sectors");
            // what the world draws is that ride, in art pixels
            CollectionAssert.AreEqual(ride.Select(p => Iso.Point(p.x, p.y)).ToList(), between.ride);

            // within one sector: over the plot from the one front to the other, and never out on the lane
            WorldBot a = Bot(alpha, "alpha-02"), b = Bot(alpha, "alpha-03");
            var across = Iso.RideTiles(alpha.origin, alpha.layout, a.place, a.slot, alpha.origin, alpha.layout, b.place, b.slot, within.way);
            Assert.AreEqual(Before(alpha, a), across[0]);
            Assert.AreEqual(Before(alpha, b), across[across.Count - 1]);
            Assert.IsTrue(across.All(p => p.y < alpha.origin.y + Iso.PlotTiles - .5f), "it stays on the plot");
            AssertRidesRoundEverything(across, "within a sector");
            CollectionAssert.AreEqual(across.Select(p => Iso.Point(p.x, p.y)).ToList(), within.ride);

            // from and to every place a bot can be, in every arrangement, nothing is ridden through
            foreach (var from in alpha.bots)
                foreach (var to in beta.bots)
                    AssertRidesRoundEverything(Iso.RideTiles(alpha.origin, alpha.layout, from.place, from.slot, beta.origin, beta.layout, to.place, to.slot, between.way), from.name + " to " + to.name);
            foreach (var from in alpha.bots)
                foreach (var to in alpha.bots)
                    if (from != to) AssertRidesRoundEverything(Iso.RideTiles(alpha.origin, alpha.layout, from.place, from.slot, alpha.origin, alpha.layout, to.place, to.slot, within.way), from.name + " to " + to.name);
        }

        [Test]
        public void ARideIsFourTilesASecondAndNeverSoShortThatItIsMissed()
        {
            Assert.AreEqual(3f, Iso.RideSeconds(0));
            Assert.AreEqual(3f, Iso.RideSeconds(11 * Iso.TileStep));
            Assert.AreEqual(10f, Iso.RideSeconds(40 * Iso.TileStep), .001f);
        }

        [Test]
        public void ATrailFadesInFourStepsOverTwoMinutesAndIsThenGone()
        {
            Assert.AreEqual(0, Deliveries.Trail(-2f), "while the courier is still on its way");
            Assert.AreEqual(0, Deliveries.Trail(0f));
            Assert.AreEqual(0, Deliveries.Trail(29.9f));
            Assert.AreEqual(1, Deliveries.Trail(30f));
            Assert.AreEqual(2, Deliveries.Trail(60f));
            Assert.AreEqual(3, Deliveries.Trail(119.9f));
            Assert.AreEqual(-1, Deliveries.Trail(120f));
            Assert.AreEqual(4, Deliveries.TrailSteps);
        }

        [Test]
        public void EveryMessageThatArrivesIsSaidOnceWithItsCourierOrWhyItHasNone()
        {
            var said = new List<string>();
            var d = new Deliveries(Opened);
            d.New(pile, world, said);
            CollectionAssert.AreEqual(new[] { "courier: alpha-04 to beta-01, through 2 sectors", "courier: alpha-02 to alpha-03, within one sector" }, said);
            // once: looked at again, nothing more is said
            said.Clear();
            d.New(pile, world, said);
            Assert.AreEqual(0, said.Count);
            // old mail is not news, and is not spoken of
            new Deliveries(Opened + 100000).New(pile, world, said);
            Assert.AreEqual(0, said.Count);
            // a message that cannot be placed: why
            d.New(Only(Row("alpha-04", "nobody", "two", Opened + 5, "x1")), world, said);
            d.New(Only(Row("nobody", "beta-01", "two", Opened + 6, "x2")), world, said);
            CollectionAssert.AreEqual(new[] { "no courier: alpha-04 to nobody: the recipient is not one bot on the map", "no courier: nobody to beta-01: the sender is not one bot on the map" }, said);
        }

        [Test]
        public void ARowIsDeliveredAtMostOnceEver()
        {
            var d = new Deliveries(Opened);
            Assert.AreEqual(2, d.New(pile, world).Count);
            Assert.AreEqual(0, d.New(pile, world).Count);
            Assert.AreEqual(0, d.New(pile, World.Build(snap, new Plots())).Count);
        }

        [Test]
        public void ARowThatChangesItsKeyWhenItsSendIsFoundIsStillTheSameDelivery()
        {
            // The pile pairs a receipt with its send by their text when the app gives no shared id, and keeps the send's row:
            // the message that was shown as row 7 comes back as row 9, with the same people and the same moment of arrival.
            var d = new Deliveries(Opened);
            Assert.AreEqual(1, d.New(Only(Row("alpha-04", "beta-01", "two", Opened + 5, "7")), world).Count);
            Assert.AreEqual(0, d.New(Only(Row("alpha-04", "beta-01", "two", Opened + 5, "9")), world).Count);
            // a second message between the same two, a moment later, is its own delivery
            Assert.AreEqual(1, d.New(Only(Row("alpha-04", "beta-01", "two", Opened + 6, "10")), world).Count);
        }

        [Test]
        public void WhatWasReceivedBeforeTheWorldOpenedIsNeverShown()
        {
            var d = new Deliveries(1794000095500);   // after the first was received, before the second
            CollectionAssert.AreEqual(new[] { "m-0002" }, d.New(pile, world).Select(x => x.key).ToArray());
            Assert.AreEqual(0, d.New(pile, world).Count);
            Assert.AreEqual(0, new Deliveries(1794000096000).New(pile, world).Count);   // received at the very moment of opening: not after
        }

        [Test]
        public void ARowWithNoReceivedTimeWaitsForOne()
        {
            var d = new Deliveries(Opened);
            Assert.AreEqual(0, d.New(Only(Row("alpha-04", "beta-01", "two", 0)), world).Count);
            Assert.AreEqual(1, d.New(Only(Row("alpha-04", "beta-01", "two", Opened + 1)), world).Count);
        }

        [TestCase("nobody", "beta-01", "two")]      // the sender matches no bot
        [TestCase("alpha-04", "nobody", "two")]     // nor the recipient
        [TestCase("alpha-04", "twin", "two")]       // the recipient matches two bots of one account
        [TestCase("alpha-02", "beta-01", "one")]    // sender and recipient are on different accounts
        [TestCase("alpha-04", "beta-01", "")]       // no account at all
        [TestCase("", "", "two")]
        public void AMessageThatCannotBePlacedIsNotShownAndNothingIsThrown(string from, string to, string account)
        {
            snap.agents.Add(new AgentRow { sessionId = "t1", name = "twin", account = "two", repoRoot = "/work/beta", repoName = "beta", state = "Running" });
            snap.agents.Add(new AgentRow { sessionId = "t2", name = "twin", account = "two", repoRoot = "/work/alpha", repoName = "alpha", state = "Running" });
            var d = new Deliveries(Opened);
            Assert.AreEqual(0, d.New(Only(Row(from, to, account, Opened + 1)), World.Build(snap, new Plots())).Count);
        }

        [Test]
        public void WhatArrivedWhileTheWorldWasClosedIsNotShownOnReturn()
        {
            // The owner looks at the lists for a while and comes back: mail that arrived meanwhile is old, not arriving now.
            var d = new Deliveries(Opened);
            d.Reopen(Opened + 10);
            Assert.AreEqual(0, d.New(Only(Row("alpha-04", "beta-01", "two", Opened + 5, "while-away")), world).Count);
            Assert.AreEqual(1, d.New(Only(Row("alpha-04", "beta-01", "two", Opened + 11, "after")), world).Count);
        }

        [Test]
        public void ASessionThatHasEndedDoesNotStandInTheWayOfItsNamesake()
        {
            // The roster keeps an ended session for hours as an offline bot. Restart a session under the same name and there
            // are two bots called that; the message is for the one that is alive.
            snap.agents.Add(new AgentRow { sessionId = "old", name = "beta-01", account = "two", repoRoot = "/work/beta", repoName = "beta", state = "Offline" });
            var got = new Deliveries(Opened).New(Only(Row("alpha-04", "beta-01", "two", Opened + 1)), World.Build(snap, new Plots()));
            Assert.AreEqual(1, got.Count);
            Assert.AreEqual("two|00000000-0000-4000-8000-000000000008", got[0].toBot);
        }

        [Test]
        public void EmptyPileEmptyWorld()
        {
            Assert.AreEqual(0, new Deliveries(Opened).New(new PileSnapshot(), world).Count);
            Assert.AreEqual(0, new Deliveries(Opened).New(pile, new WorldModel()).Count);
        }

        [TestCase(0, 0, 19, 0)]      // side by side: one straight run over the link
        [TestCase(19, 0, 0, 0)]
        [TestCase(0, 0, 0, 19)]
        [TestCase(0, 19, 0, 0)]
        [TestCase(0, 0, 43, 0)]      // across the open ground between two regions
        [TestCase(-30, 50, 9, 20)]   // and where the two do not face each other squarely
        public void ARouteRunsAlongLanesAndARoadFromOneSectorsCrossingToTheOthers(int ax, int ay, int bx, int by)
        {
            Vector2Int a = new Vector2Int(ax, ay), b = new Vector2Int(bx, by);
            var route = Iso.Route(new[] { a, b });
            Assert.GreaterOrEqual(route.Count, 2);
            // at a sector's crossing: in the middle of the lane along its front, where no building of its own stands in the way
            Assert.AreEqual(new Vector2(a.x + Iso.Crossing + .5f, a.y + Iso.PlotTiles + .5f), route[0]);
            Assert.AreEqual(new Vector2(b.x + Iso.Crossing + .5f, b.y + Iso.PlotTiles + .5f), route[route.Count - 1]);
            var road = new HashSet<Vector2Int>(Iso.RoadTiles(a, b));
            bool Paved(float col, float row) => Iso.OnLane(a, (int)col, (int)row) || Iso.OnLane(b, (int)col, (int)row) || road.Contains(new Vector2Int((int)col, (int)row));
            for (int i = 1; i < route.Count; i++)
            {
                Vector2 p = route[i - 1], q = route[i];
                Assert.AreNotEqual(p, q, "a leg of no length");
                Assert.IsTrue(p.x == q.x || p.y == q.y, "a leg runs along columns or along rows, never both: " + p + " to " + q);
                // down the middle: lanes and roads are two tiles wide, and all the way there is one of their tiles on either side
                float length = Mathf.Abs(q.x - p.x) + Mathf.Abs(q.y - p.y);
                for (float k = 0; k <= length; k += .5f)
                {
                    var at = Vector2.MoveTowards(p, q, k);
                    foreach (float col in new[] { Mathf.Floor(at.x), Mathf.Ceil(at.x) })
                        foreach (float row in new[] { Mathf.Floor(at.y), Mathf.Ceil(at.y) })
                            Assert.IsTrue(Paved(col, row), "at " + at + ", tile " + col + ", " + row + " is neither lane nor road");
                }
            }
        }

        [Test]
        public void ARibbonIsCutIntoUprightSlicesSideBySide()
        {
            // down a lane to the right, then straight down the screen, then back to the left
            var ride = new List<Vector2> { new Vector2(0, 0), new Vector2(64, -32), new Vector2(64, -100), new Vector2(30, -117) };
            var slices = Iso.Ribbon(ride, 4);
            Assert.AreEqual(16 + 1 + 9, slices.Count);
            float legOne = new Vector2(64, -32).magnitude;
            for (int k = 0; k < 16; k++)
            {
                // each four pixels wide, standing on the lower end of its piece of the way, and that piece's climb taller
                Assert.AreEqual(2 + 4 * k, slices[k].x, .001f);
                Assert.AreEqual(4f, slices[k].wide, .001f);
                Assert.AreEqual(-2 * (k + 1), slices[k].y, .001f);
                Assert.AreEqual(2f, slices[k].rise, .001f);
                Assert.AreEqual(legOne * k / 16, slices[k].at, .001f);
            }
            // straight down the screen the ribbon is seen edge on: one slice, as much taller as the way is long
            Assert.AreEqual(64f, slices[16].x, .001f);
            Assert.AreEqual(-100f, slices[16].y, .001f);
            Assert.AreEqual(68f, slices[16].rise, .001f);
            Assert.AreEqual(legOne, slices[16].at, .001f);
            // to the left, the last slice only as wide as what is left
            Assert.AreEqual(62f, slices[17].x, .001f);
            Assert.AreEqual(31f, slices[25].x, .001f);
            Assert.AreEqual(2f, slices[25].wide, .001f);
            Assert.AreEqual(34f, slices.Skip(17).Sum(s => s.wide), .001f);
            for (int i = 1; i < slices.Count; i++) Assert.GreaterOrEqual(slices[i].at, slices[i - 1].at, "slice " + i + " comes before the one before it");
            Assert.AreEqual(0, Iso.Ribbon(new List<Vector2> { new Vector2(5, 5) }, 4).Count);
        }
    }
}
