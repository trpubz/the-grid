using System;
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
            Assert.AreEqual(world.sectors[0].cell, between.fromCell);
            Assert.AreEqual(world.sectors[1].cell, between.toCell);
            Assert.AreEqual(1794000095000, between.receivedAt);
            var within = got.Single(d => d.key == "m-0002");
            Assert.AreEqual(within.fromCell, within.toCell);
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
        public void EmptyPileEmptyWorld()
        {
            Assert.AreEqual(0, new Deliveries(Opened).New(new PileSnapshot(), world).Count);
            Assert.AreEqual(0, new Deliveries(Opened).New(pile, new WorldModel()).Count);
        }

        [Test]
        public void ARouteWithinOneSectorIsEmpty() => Assert.AreEqual(0, Iso.Route(new Vector2Int(3, -2), new Vector2Int(3, -2)).Count);

        static void Along(Vector2 a, Vector2 b, Action<int, int> tile)
        {
            Assert.IsTrue(a.x == b.x || a.y == b.y, "a leg runs along columns or along rows, never both: " + a + " to " + b);
            int steps = (int)Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
            for (int i = 0; i <= steps; i++) tile((int)(a.x + Math.Sign(b.x - a.x) * i), (int)(a.y + Math.Sign(b.y - a.y) * i));
        }

        [TestCase(0, 0, 2, 1)]
        [TestCase(2, 1, 0, 0)]
        [TestCase(0, 0, 1, 0)]     // side by side: one straight run
        [TestCase(0, 0, 0, 1)]
        [TestCase(-1, 2, 3, -2)]
        public void ARouteRunsAlongLanesFromBehindOneRelayToBehindTheOther(int ax, int ay, int bx, int by)
        {
            Vector2Int a = new Vector2Int(ax, ay), b = new Vector2Int(bx, by);
            var route = Iso.Route(a, b);
            Assert.GreaterOrEqual(route.Count, 2);
            // behind a relay: on the lane at the back of its sector, level with the relay
            RectInt relay = Iso.Footprint(Structure.Relay);
            Vector2Int oa = Iso.PlotOrigin(a), ob = Iso.PlotOrigin(b);
            Assert.AreEqual(new Vector2(oa.x + relay.xMax - 1, oa.y - 1), route[0]);
            Assert.AreEqual(new Vector2(ob.x + relay.xMax - 1, ob.y - 1), route[route.Count - 1]);
            for (int i = 1; i < route.Count; i++)
            {
                Assert.AreNotEqual(route[i - 1], route[i], "a leg of no length");
                Along(route[i - 1], route[i], (col, row) =>
                {
                    // on the lane round some cell: the cell this tile's column and row fall in, or one beside it
                    bool lane = false;
                    for (int cx = Mathf.FloorToInt((float)col / Iso.Pitch) - 1; cx <= Mathf.FloorToInt((float)col / Iso.Pitch) + 1; cx++)
                        for (int cy = Mathf.FloorToInt((float)row / Iso.Pitch) - 1; cy <= Mathf.FloorToInt((float)row / Iso.Pitch) + 1; cy++)
                            lane |= Iso.OnLane(new Vector2Int(cx, cy), col, row);
                    Assert.IsTrue(lane, "tile " + col + ", " + row + " is not lane");
                });
            }
        }
    }
}
