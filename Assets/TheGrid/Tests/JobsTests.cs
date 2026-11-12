using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TheGrid.Core.Tests
{
    // A bot at work inside a building does one of that building's jobs, a beat at a time.
    public class JobsTests
    {
        static readonly Place[] Rooms = { Place.ServerTower, Place.ReviewHall, Place.Works, Place.Workshop };
        static int Places(Place room) => room == Place.Workshop ? 4 : Layout.Inside;
        const int Minute = 86;   // beats: sixty seconds of Jobs.BeatSeconds

        static JobFrame At(Place room, int slot, string bot, int beat)
        {
            Assert.IsTrue(Jobs.TryFrame(room, slot, bot, beat, out var frame), room + " slot " + slot + " has no job");
            return frame;
        }

        [Test]
        public void EveryPlaceInsideABuildingHasJobsAndNoOtherPlaceHasAny()
        {
            Assert.AreEqual(.7f, Jobs.BeatSeconds);
            foreach (var room in Rooms)
            {
                for (int slot = 0; slot < Places(room); slot++) Assert.IsNotEmpty(At(room, slot, "one|a", 0).job);
                Assert.IsFalse(Jobs.TryFrame(room, Places(room), "one|a", 0, out _), room + ": the one waiting at the door has no job");
            }
            foreach (var open in new[] { Place.Plaza, Place.Forecourt, Place.Dock }) Assert.IsFalse(Jobs.TryFrame(open, 0, "one|a", 0, out _), open.ToString());
        }

        [Test]
        public void TheSameBotAtTheSameBeatDoesTheSameThingAndTwoBotsAreOutOfStep()
        {
            foreach (var room in Rooms)
                for (int slot = 0; slot < Places(room); slot++)
                {
                    bool differ = false;
                    for (int beat = 0; beat < 4 * Jobs.Beats; beat++)
                    {
                        JobFrame a = At(room, slot, "one|a", beat), again = At(room, slot, "one|a", beat), b = At(room, slot, "two|b", beat);
                        Assert.AreEqual(a.at, again.at);
                        Assert.AreEqual(a.pose + a.facing + a.job, again.pose + again.facing + again.job);
                        differ |= a.at != b.at || a.pose != b.pose;
                    }
                    Assert.IsTrue(differ, room + " slot " + slot + ": two bots there would move as one");
                }
        }

        [Test]
        public void InAMinuteABotIsSeenAtTwoJobsAndTheOwnersFiveAreAllThere()
        {
            var seen = new Dictionary<Place, HashSet<string>>();
            foreach (var room in Rooms)
            {
                seen[room] = new HashSet<string>();
                for (int slot = 0; slot < Places(room); slot++)
                {
                    var jobs = new HashSet<string>(Enumerable.Range(0, Minute).Select(beat => At(room, slot, "one|a", beat).job));
                    Assert.GreaterOrEqual(jobs.Count, 2, room + " slot " + slot + " does one thing all minute");
                    seen[room].UnionWith(jobs);
                }
            }
            // a drive swapped between racks, a cable plugged in; a button pushed; a core moved between reactors, a lever pulled
            CollectionAssert.IsSubsetOf(new[] { "swaps a drive", "plugs in a cable", "holds its disc up" }, seen[Place.ServerTower]);
            CollectionAssert.IsSubsetOf(new[] { "pushes buttons", "holds its disc up" }, seen[Place.ReviewHall]);
            CollectionAssert.IsSubsetOf(new[] { "moves a core", "pulls a lever" }, seen[Place.Works]);
            CollectionAssert.IsSubsetOf(new[] { "types", "pulls a lever" }, seen[Place.Workshop]);
        }

        [Test]
        public void ABotAtAJobStaysInItsRoomOnItsFloorInSightAndInAPictureThereIs()
        {
            var pictures = new HashSet<string>(ArtNames.All());
            foreach (int a in Enumerable.Range(0, Layout.Count))
            {
                var l = Layout.Of(a);
                foreach (var room in Rooms)
                {
                    var f = l.Footprint(World.StructureOf(room).Value);
                    for (int slot = 0; slot < Places(room); slot++)
                    {
                        var home = l.Spot(room, slot);
                        foreach (var job in Jobs.Of(room, slot))
                        {
                            string who = "arrangement " + a + ": " + room + " slot " + slot + ", " + job[0].job;
                            for (int i = 0; i < job.Length; i++)
                            {
                                var at = home + job[i].at;
                                Assert.IsTrue(pictures.Contains(ArtNames.Bot(job[i].pose, job[i].facing)), who + ": no picture of " + job[i].pose + " facing " + job[i].facing);
                                Assert.IsTrue(at.x > f.xMin - .3f && at.x < f.xMax - .7f && at.y > f.yMin - .4f && at.y < f.yMax - .6f, who + " leaves its room at " + at);
                                if (l.Floor(room, slot) > 0) Assert.Less(at.y - f.yMin, Layout.FloorReach - .05f, who + " steps off its floor at " + at);
                                // the far right of a room is behind the building next along the back row
                                if (room != Place.Workshop) Assert.LessOrEqual(at.x - f.xMin - (at.y - f.yMin), .8f, who + " is out of sight at " + at);
                                // a step at a time, round and back to where it began
                                var next = job[(i + 1) % job.Length].at;
                                Assert.LessOrEqual((next - job[i].at).magnitude, .45f, who + " jumps from " + job[i].at + " to " + next);
                            }
                            // what is carried is carried somewhere
                            if (job.Any(frame => frame.pose == ArtNames.CarryA)) Assert.GreaterOrEqual(job.Select(frame => frame.at).Distinct().Count(), 3, who);
                        }
                    }
                }
            }
        }
    }
}
