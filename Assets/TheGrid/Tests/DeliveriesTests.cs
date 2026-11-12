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
        WorldModel world;
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

        [Test]
        public void ACourierRidesFromTheSenderToTheRecipientAlsoWithinOneSector()
        {
            var got = new Deliveries(Opened).New(pile, world);
            Delivery between = got.Single(d => d.key == "m-0001"), within = got.Single(d => d.key == "m-0002");
            Vector2 from = new Vector2(-40, 12), to = new Vector2(900, -300);   // where the two bots stand, in art pixels
            // between two sectors: from beside the sender, by its sector's crossing, the lanes and the road, to the recipient
            var ride = Iso.Ride(from, between.way, to);
            Assert.AreEqual(from, ride[0]);
            Assert.AreEqual(to, ride[ride.Count - 1]);
            CollectionAssert.AreEqual(Iso.Route(between.way).Select(c => Iso.Point(c.x, c.y)).ToList(), ride.Skip(1).Take(ride.Count - 2).ToList());
            // within one sector: straight from the one to the other
            CollectionAssert.AreEqual(new[] { from, to }, Iso.Ride(from, within.way, to));
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
            // at a sector's crossing: on the lane along its front, where no building of its own stands in the way
            Assert.AreEqual(new Vector2(a.x + Iso.Crossing, a.y + Iso.PlotTiles), route[0]);
            Assert.AreEqual(new Vector2(b.x + Iso.Crossing, b.y + Iso.PlotTiles), route[route.Count - 1]);
            var road = new HashSet<Vector2Int>(Iso.RoadTiles(a, b));
            for (int i = 1; i < route.Count; i++)
            {
                Vector2 p = route[i - 1], q = route[i];
                Assert.AreNotEqual(p, q, "a leg of no length");
                Assert.IsTrue(p.x == q.x || p.y == q.y, "a leg runs along columns or along rows, never both: " + p + " to " + q);
                int steps = (int)Mathf.Max(Mathf.Abs(q.x - p.x), Mathf.Abs(q.y - p.y));
                for (int k = 0; k <= steps; k++)
                {
                    int col = (int)(p.x + Math.Sign(q.x - p.x) * k), row = (int)(p.y + Math.Sign(q.y - p.y) * k);
                    Assert.IsTrue(Iso.OnLane(a, col, row) || Iso.OnLane(b, col, row) || road.Contains(new Vector2Int(col, row)), "tile " + col + ", " + row + " is neither lane nor road");
                }
            }
        }
    }
}
