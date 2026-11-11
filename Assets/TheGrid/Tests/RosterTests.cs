using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TheGrid.Core.Tests
{
    public class RosterTests
    {
        const string S2 = "00000000-0000-4000-8000-000000000002";
        const long Now = 1794000200000L;
        static readonly string[] None = new string[0];

        string tmp, cfg, transcript;
        Settings settings;
        List<LiveSession> live;

        static string Fixtures => Path.Combine(Directory.GetCurrentDirectory(), "fixtures");
        static DateTime At(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;

        [SetUp]
        public void SetUp()
        {
            tmp = Path.Combine(Path.GetTempPath(), "grid-tests-" + Guid.NewGuid().ToString("N"));
            cfg = Path.Combine(tmp, ".claude-demo");
            Directory.CreateDirectory(Path.Combine(cfg, "sessions"));
            var proj = Path.Combine(cfg, "projects", "p1");
            Directory.CreateDirectory(proj);
            File.WriteAllText(Path.Combine(cfg, "sessions", "1002.json"), "{\"pid\":1002,\"status\":\"waiting\",\"statusUpdatedAt\":1794000123000,\"entrypoint\":\"cli\"}");
            transcript = Path.Combine(proj, S2 + ".jsonl");
            File.Copy(Path.Combine(Fixtures, "claude-code", "transcript-records.jsonl"), transcript);
            var subs = Path.Combine(proj, S2, "subagents");
            Directory.CreateDirectory(subs);
            AddSubagent(subs, "a1", "{\"agentType\":\"general-purpose\",\"description\":\"Synthetic task\"}", Now - 1000);
            AddSubagent(subs, "a2", "{\"agentType\":\"general-purpose\",\"description\":\"Already done\",\"finished\":true}", Now - 1000);
            AddSubagent(subs, "a3", "{\"agentType\":\"general-purpose\",\"description\":\"Went quiet\"}", Now - 3600000);
            settings = new Settings();
            Assert.IsTrue(LiveSessions.TryParse(File.ReadAllText(Path.Combine(Fixtures, "claude-code", "agents.json")), cfg, out live, out var err), err);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(tmp, true);

        static void AddSubagent(string dir, string id, string metaJson, long changedAt)
        {
            File.WriteAllText(Path.Combine(dir, "agent-" + id + ".meta.json"), metaJson);
            var log = Path.Combine(dir, "agent-" + id + ".jsonl");
            File.WriteAllText(log, "{}\n");
            File.SetLastWriteTimeUtc(log, At(changedAt));
        }

        static LiveSession Idle(string id, string name, string cwd = "/work/none") =>
            new LiveSession { SessionId = id, Name = name, Cwd = cwd, Kind = "interactive", Status = "idle", WaitingFor = "", State = "", ConfigDir = "/cfg/.claude" };

        [Test]
        public void ActiveSubagentsAreUnfinishedAndRecentlyChanged()
        {
            var subs = Subagents.ReadActive(transcript, Now);
            Assert.AreEqual(1, subs.Count);
            Assert.AreEqual("a1", subs[0].agentId);
            Assert.AreEqual("Synthetic task", subs[0].description);
            Assert.AreEqual("general-purpose", subs[0].agentType);
            Assert.AreEqual(0, Subagents.ReadActive("/no/such/session.jsonl", Now).Count);
            Assert.AreEqual(0, Subagents.ReadActive(null, Now).Count);
        }

        [Test]
        public void OrdersByUrgencyAndFillsEachField()
        {
            var snap = new RosterBuilder(settings).Build(live, None, Now);
            CollectionAssert.AreEqual(new[] { "BlockedOnQuestion", "WaitingForPermission", "BackgroundBlocked", "Finished", "Running" }, snap.agents.Select(a => a.state).ToArray());
            Assert.AreEqual(Now, snap.generatedAt);
            Assert.AreEqual(0, snap.errors.Count);
            var a = snap.agents[0];
            Assert.AreEqual("alpha-02", a.name);
            Assert.AreEqual(S2, a.sessionId);
            Assert.AreEqual("demo", a.account);
            Assert.AreEqual("cli", a.app);
            Assert.AreEqual("feature/demo-12-example-change", a.branch);
            Assert.AreEqual("DEMO-12", a.ticket);
            Assert.AreEqual(7, a.prNumber);
            Assert.AreEqual("https://example.invalid/demo/alpha/pull/7", a.prUrl);
            Assert.AreEqual("waiting / input needed", a.rawStatus);
            Assert.AreEqual("Personal", a.workspace);
            Assert.AreEqual(1794000123000L, a.stateSince);
            Assert.AreEqual("No project", a.repoName);
            Assert.AreEqual("", a.repoRoot);
            Assert.AreEqual(1, a.subagents.Count);
            Assert.AreEqual("Synthetic task", a.subagents[0].description);
            var running = snap.agents[4];
            Assert.AreEqual("alpha-01", running.name);
            Assert.AreEqual("", running.ticket);
            Assert.AreEqual("", running.app);
            Assert.AreEqual(0, running.subagents.Count);
        }

        [Test]
        public void ErrorsArePassedThrough()
        {
            CollectionAssert.AreEqual(new[] { "boom" }, new RosterBuilder(settings).Build(live, new[] { "boom" }, Now).errors);
        }

        [Test]
        public void RepoAndWorkspaceComeFromTheFolderAndSettings()
        {
            var repo = Path.Combine(tmp, "alpha");
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
            Directory.CreateDirectory(Path.Combine(repo, "src"));
            settings.workspaces = new List<string> { "Personal", "Work" };
            settings.CycleWorkspace(repo);
            var snap = new RosterBuilder(settings).Build(new[] { Idle("s-9", "gamma-01", Path.Combine(repo, "src")) }, None, Now);
            Assert.AreEqual("alpha", snap.agents[0].repoName);
            Assert.AreEqual(repo, snap.agents[0].repoRoot);
            Assert.AreEqual("Work", snap.agents[0].workspace);
            Assert.AreEqual("claude", snap.agents[0].account);
        }

        [Test]
        public void WithoutASessionFileStateSinceIsFirstSeenAndDoesNotMove()
        {
            var builder = new RosterBuilder(settings);
            var first = builder.Build(live, None, Now).agents.First(x => x.name == "beta-01").stateSince;
            Assert.AreEqual(Now, first);
            Assert.AreEqual(first, builder.Build(live, None, Now + 4000).agents.First(x => x.name == "beta-01").stateSince);
        }

        [Test]
        public void AChangeOfStateRestartsTheClock()
        {
            var builder = new RosterBuilder(settings);
            var s = Idle("s-9", "gamma-01");
            builder.Build(new[] { s }, None, Now);
            s.Status = "busy";
            var after = builder.Build(new[] { s }, None, Now + 6000).agents[0];
            Assert.AreEqual("Running", after.state);
            Assert.AreEqual(Now + 6000, after.stateSince);
        }

        [Test]
        public void WithinAStateTheLongestWaitingComesFirst()
        {
            var builder = new RosterBuilder(settings);
            var early = Idle("s-1", "zeta");
            var late = Idle("s-2", "alpha");
            builder.Build(new[] { early }, None, Now);
            var snap = builder.Build(new[] { late, early }, None, Now + 5000);
            CollectionAssert.AreEqual(new[] { "zeta", "alpha" }, snap.agents.Select(a => a.name).ToArray());
        }

        [Test]
        public void ASessionThatDisappearsGoesOfflineThenIsDropped()
        {
            var builder = new RosterBuilder(settings);
            builder.Build(live, None, Now);
            var rest = live.Skip(1).ToList();
            var gone = builder.Build(rest, None, Now + 2000);
            Assert.AreEqual(5, gone.agents.Count);
            Assert.AreEqual("Offline", gone.agents.Last().state);
            Assert.AreEqual("alpha-01", gone.agents.Last().name);
            Assert.AreEqual(Now + 2000, gone.agents.Last().stateSince);
            Assert.IsFalse(builder.Build(rest, None, Now + 2000 + RosterBuilder.OfflineKeepMs + 1000).agents.Any(x => x.name == "alpha-01"));
        }

        [Test]
        public void APullRequestOnceSeenIsKeptAndTheBranchStillUpdates()
        {
            var builder = new RosterBuilder(settings);
            Assert.AreEqual(7, builder.Build(live, None, Now).agents[0].prNumber);
            File.WriteAllText(transcript, "{\"type\":\"user\",\"gitBranch\":\"feature/demo-13-next\"}\n");
            File.SetLastWriteTimeUtc(transcript, At(Now + 60000));
            var a = builder.Build(live, None, Now + 2000).agents[0];
            Assert.AreEqual(7, a.prNumber);
            Assert.AreEqual("feature/demo-13-next", a.branch);
            Assert.AreEqual("DEMO-13", a.ticket);
        }

        [Test]
        public void SnapshotSerialisesToTheGoldenFile()
        {
            var actual = JsonUtility.ToJson(new RosterBuilder(settings).Build(live, None, Now), true).Replace("\r\n", "\n").Trim();
            var goldenPath = Path.Combine(Fixtures, "roster", "snapshot.json");
            var golden = File.Exists(goldenPath) ? File.ReadAllText(goldenPath).Replace("\r\n", "\n").Trim() : "";
            if (actual != golden) File.WriteAllText(Path.Combine(Path.GetTempPath(), "grid-roster-snapshot.actual.json"), actual + "\n");
            Assert.AreEqual(golden, actual, "snapshot changed; the actual output is in the temp folder as grid-roster-snapshot.actual.json");
        }

        [Test]
        public void ARowWithNoSessionFileStartsItsClockWhenTheSessionStarted()
        {
            var old = Idle("s-1", "old-timer");
            old.StartedAt = Now - 86400000;
            var unknown = Idle("s-2", "no-start");
            var snap = new RosterBuilder(settings).Build(new[] { old, unknown }, None, Now);
            Assert.AreEqual(Now - 86400000, snap.agents.First(a => a.name == "old-timer").stateSince);
            Assert.AreEqual(Now, snap.agents.First(a => a.name == "no-start").stateSince);
        }

        static string Assistant(string tool, string input) =>
            "{\"type\":\"assistant\",\"gitBranch\":\"feature/demo-12-example-change\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"t\",\"name\":\"" + tool + "\",\"input\":" + input + "}]}}\n";

        [Test]
        public void ActivityIsGivenOnlyWhileRunning()
        {
            var snap = new RosterBuilder(settings).Build(live, None, Now);
            Assert.AreEqual("working", snap.agents.First(a => a.state == "Running").activity); // no transcript: cannot tell
            Assert.IsTrue(snap.agents.Where(a => a.state != "Running").All(a => a.activity == ""));
        }

        [Test]
        public void ActivityFollowsARunningSessionsTranscript()
        {
            var s = new LiveSession { SessionId = S2, Name = "alpha-02", Cwd = "/work/alpha", Kind = "interactive", Status = "busy", WaitingFor = "", State = "", ConfigDir = cfg };
            var builder = new RosterBuilder(settings);
            File.AppendAllText(transcript, Assistant("Edit", "{}"));
            Assert.AreEqual("editing", builder.Build(new[] { s }, None, Now).agents[0].activity);
            File.AppendAllText(transcript, Assistant("Bash", "{\"command\":\"pytest -q\"}"));
            Assert.AreEqual("testing", builder.Build(new[] { s }, None, Now + 2000).agents[0].activity);
            s.Status = "idle";
            Assert.AreEqual("", builder.Build(new[] { s }, None, Now + 4000).agents[0].activity);
            s.Status = "busy"; // running again, transcript untouched: what it was last doing is remembered
            Assert.AreEqual("testing", builder.Build(new[] { s }, None, Now + 6000).agents[0].activity);
        }

        [Test]
        public void ASessionThatGoesOfflineStopsShowingAnActivity()
        {
            var s = new LiveSession { SessionId = S2, Name = "alpha-02", Cwd = "/work/alpha", Kind = "interactive", Status = "busy", WaitingFor = "", State = "", ConfigDir = cfg };
            var builder = new RosterBuilder(settings);
            File.AppendAllText(transcript, Assistant("Edit", "{}"));
            Assert.AreEqual("editing", builder.Build(new[] { s }, None, Now).agents[0].activity);
            var gone = builder.Build(new LiveSession[0], None, Now + 2000).agents[0];
            Assert.AreEqual("Offline", gone.state);
            Assert.AreEqual("", gone.activity);
        }
    }
}
