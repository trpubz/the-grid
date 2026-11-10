using System.IO;
using System.Linq;
using NUnit.Framework;

namespace TheGrid.Core.Tests
{
    public class LiveSessionsTests
    {
        static string Fixture => Path.Combine(Directory.GetCurrentDirectory(), "fixtures", "claude-code", "agents.json");

        [Test]
        public void ParsesFixtureRowsAndMapsEachState()
        {
            Assert.IsTrue(LiveSessions.TryParse(File.ReadAllText(Fixture), "/cfg", out var s, out var err), err);
            Assert.AreEqual(5, s.Count);
            CollectionAssert.AreEqual(
                new[] { AgentState.Running, AgentState.BlockedOnQuestion, AgentState.WaitingForPermission, AgentState.Finished, AgentState.BackgroundBlocked },
                s.Select(LiveSessions.Map).ToArray());
            Assert.AreEqual("/cfg", s[0].ConfigDir);
            Assert.AreEqual("alpha-01", s[0].Name);
            Assert.AreEqual("/work/alpha", s[0].Cwd);
            Assert.AreEqual(1001, s[0].Pid);
            Assert.AreEqual(1794000000000L, s[0].StartedAt);
            Assert.AreEqual(0, s[4].Pid); // background rows have no pid
        }

        [TestCase("interactive", "shell", null, null, AgentState.Unknown)]
        [TestCase("interactive", "waiting", "something new", null, AgentState.WaitingOther)]
        [TestCase("interactive", "waiting", "sandbox request", null, AgentState.WaitingForPermission)]
        [TestCase("interactive", null, null, null, AgentState.Unknown)]
        [TestCase("background", null, null, "working", AgentState.Running)]
        [TestCase("background", null, null, "done", AgentState.Finished)]
        [TestCase("background", null, null, "stopped", AgentState.Finished)]
        [TestCase("background", null, null, "failed", AgentState.Errored)]
        [TestCase("background", null, null, "brand new", AgentState.Unknown)]
        public void MapsStatus(string kind, string status, string waitingFor, string state, AgentState expected)
        {
            Assert.AreEqual(expected, LiveSessions.Map(new LiveSession { Kind = kind, Status = status, WaitingFor = waitingFor, State = state }));
        }

        [Test]
        public void MalformedJsonIsAnErrorNotAnException()
        {
            Assert.IsFalse(LiveSessions.TryParse("not json", "/cfg", out var s, out var e));
            Assert.IsNotEmpty(e);
            Assert.AreEqual(0, s.Count);
        }

        [Test]
        public void EmptyListParses()
        {
            Assert.IsTrue(LiveSessions.TryParse("[]", "/cfg", out var none, out _));
            Assert.AreEqual(0, none.Count);
        }

        [Test]
        public void RawStatusKeepsTextTheMappingDoesNotKnow()
        {
            Assert.AreEqual("shell", LiveSessions.RawStatus(new LiveSession { Status = "shell" }));
            Assert.AreEqual("waiting / input needed", LiveSessions.RawStatus(new LiveSession { Status = "waiting", WaitingFor = "input needed" }));
            Assert.AreEqual("blocked", LiveSessions.RawStatus(new LiveSession { Kind = "background", State = "blocked" }));
            Assert.AreEqual("", LiveSessions.RawStatus(new LiveSession()));
        }
    }
}
