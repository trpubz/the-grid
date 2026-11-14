using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace TheGrid.Core.Tests
{
    // Codex threads, read from session files written here by hand in the shape Codex writes them.
    public class CodexTests
    {
        const long Now = 1767348600000L;   // 2026-01-02T10:10:00Z
        const long Started = 1767348000000L, Ended = 1767348005000L;   // 10:00:00 and 10:00:05
        const string Id = "0199aaaa-0000-7000-8000-000000000001";
        const string At0 = "2026-01-02T10:00:00.000Z", At5 = "2026-01-02T10:00:05.000Z";

        string tmp, codex, repo;
        Settings settings;

        [SetUp]
        public void SetUp()
        {
            tmp = Path.Combine(Path.GetTempPath(), "grid-tests-" + Guid.NewGuid().ToString("N"));
            codex = Path.Combine(tmp, "home", ".codex");
            repo = Path.Combine(tmp, "work", "alpha");
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
            settings = new Settings();
        }

        [TearDown]
        public void TearDown() => Directory.Delete(tmp, true);

        static string Meta(string id, string cwd, string threadSource = "user", string originator = "Codex Desktop") =>
            "{\"timestamp\":\"" + At0 + "\",\"type\":\"session_meta\",\"payload\":{\"id\":\"" + id + "\",\"cwd\":\"" + cwd + "\",\"originator\":\"" + originator
            + "\",\"source\":" + (threadSource == "user" ? "\"vscode\"" : "{\"subagent\":{\"other\":\"guardian\"}}") + ",\"thread_source\":\"" + threadSource + "\",\"base_instructions\":{\"text\":\"many words\"}}}";

        static string Event(string type, string at, string more = "") =>
            "{\"timestamp\":\"" + at + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"" + type + "\",\"turn_id\":\"t1\"" + more + "}}";

        static string Item(string item, string at = At0) =>
            "{\"timestamp\":\"" + at + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"item_completed\",\"turn_id\":\"t1\",\"item\":" + item + "}}";

        static string Prompt(string text) => Item("{\"type\":\"UserMessage\",\"id\":\"u1\",\"content\":[{\"type\":\"text\",\"text\":\"" + text + "\",\"text_elements\":[]}]}");
        static string Said(string questions = "null", string at = At0) => Item("{\"type\":\"AgentMessage\",\"id\":\"a1\",\"content\":[{\"type\":\"text\",\"text\":\"words\"}],\"questions\":" + questions + "}", at);
        static string Ran(string command) => Item("{\"type\":\"CommandExecution\",\"id\":\"c1\",\"command\":[\"/bin/zsh\",\"-lc\",\"" + command + "\"],\"status\":\"completed\",\"aggregated_output\":\"out\"}");
        const string Changed = "{\"type\":\"FileChange\",\"id\":\"f1\",\"status\":\"completed\"}";
        const string Asked = "[{\"title\":\"Which one?\",\"options\":[{\"label\":\"a\"}]}]";

        // Writes a session file as Codex names them, last changed `agoMs` before Now.
        string Write(string id, long agoMs, params string[] lines)
        {
            var dir = Path.Combine(codex, "sessions", "2026", "01", "02");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "rollout-2026-01-02T10-00-00-" + id + ".jsonl");
            File.WriteAllText(path, string.Join("\n", lines) + "\n");
            File.SetLastWriteTimeUtc(path, DateTimeOffset.FromUnixTimeMilliseconds(Now - agoMs).UtcDateTime);
            return path;
        }

        List<AgentRow> Read() => new CodexReader(settings, codex).Read(Now);

        AgentRow One(long agoMs, params string[] turn)
        {
            var lines = new List<string> { Meta(Id, repo), Prompt("Fix the date picker\\nand more") };
            lines.AddRange(turn);
            Write(Id, agoMs, lines.ToArray());
            var rows = Read();
            Assert.AreEqual(1, rows.Count);
            return rows[0];
        }

        [Test]
        public void AThreadThatFinishedItsTurnIsARow()
        {
            var row = One(60000, Event("task_started", At0), Said(), Event("task_complete", At5, ",\"last_agent_message\":\"done\",\"error\":null"));
            Assert.AreEqual(Id, row.sessionId);
            Assert.AreEqual("Fix the date picker", row.name);
            Assert.AreEqual("codex", row.account);
            Assert.AreEqual("codex-desktop", row.app);
            Assert.AreEqual(repo, row.repoRoot);
            Assert.AreEqual("alpha", row.repoName);
            Assert.AreEqual(Settings.DefaultWorkspace, row.workspace);
            Assert.AreEqual("Finished", row.state);
            Assert.AreEqual("task_complete", row.rawStatus);
            Assert.AreEqual("", row.activity);
            Assert.AreEqual(Ended, row.stateSince);
            Assert.AreEqual("cd " + repo + " && codex resume " + Id, row.enter);
            Assert.AreEqual(0, row.pid);
        }

        // A turn that started and has not ended: running, and doing what its last command or file change says.
        [Test]
        public void AnOpenTurnIsRunning()
        {
            var row = One(1000, Event("task_started", At5));
            Assert.AreEqual("Running", row.state);
            Assert.AreEqual("working", row.activity);
            Assert.AreEqual(Ended, row.stateSince);
        }

        [Test]
        public void ARunningThreadsLastCommandSaysWhatItIsDoing() =>
            Assert.AreEqual("testing", One(1000, Event("task_started", At0), Item(Changed), Ran("npm test")).activity);

        [Test]
        public void ARunningThreadThatChangedAFileIsEditing() =>
            Assert.AreEqual("editing", One(1000, Event("task_started", At0), Ran("npm test"), Item(Changed)).activity);

        // What an earlier turn did says nothing about this one.
        [Test]
        public void ANewTurnStartsWithNoActivityOfItsOwn() =>
            Assert.AreEqual("working", One(1000, Event("task_started", At0), Ran("npm test"), Event("task_complete", At0), Event("task_started", At5)).activity);

        [Test]
        public void AThreadThatAskedAQuestionWaitsForTheAnswer()
        {
            var row = One(1000, Event("task_started", At0), Said(Asked, At5));
            Assert.AreEqual("BlockedOnQuestion", row.state);
            Assert.AreEqual(Ended, row.stateSince);
        }

        [Test]
        public void OnceAnsweredItRunsAgain() =>
            Assert.AreEqual("Running", One(1000, Event("task_started", At0), Said(Asked), Prompt("the first one")).state);

        // An open turn that has written nothing for a quarter of an hour: from outside, that is what waiting for the
        // owner's approval looks like.
        [TestCase(14 * 60000L, "Running")]
        [TestCase(16 * 60000L, "WaitingOther")]
        public void AnOpenTurnThatWentQuietIsWaiting(long agoMs, string expected) => Assert.AreEqual(expected, One(agoMs, Event("task_started", At0)).state);

        [Test]
        public void ATurnThatEndedWithAnErrorFailed() =>
            Assert.AreEqual("Errored", One(1000, Event("task_started", At0), Event("task_complete", At5, ",\"error\":{\"message\":\"stream closed\",\"codex_error_info\":\"other\"}")).state);

        [Test]
        public void ATurnTheOwnerStoppedIsDone() =>
            Assert.AreEqual("Finished", One(1000, Event("task_started", At0), Event("turn_aborted", At5, ",\"reason\":\"interrupted\"")).state);

        [TestCase("Codex Desktop", "codex-desktop")]
        [TestCase("codex-tui", "codex-cli")]
        [TestCase("something new", "codex")]
        public void TheAppIsReadFromWhoStartedTheThread(string originator, string expected)
        {
            Write(Id, 1000, Meta(Id, repo, "user", originator), Event("task_started", At0));
            Assert.AreEqual(expected, Read()[0].app);
        }

        [Test]
        public void ALongPromptIsCutShort()
        {
            Write(Id, 1000, Meta(Id, repo), Prompt("  Please look at the fiscal quarter date picker and tell me why it is slow"), Event("task_started", At0));
            Assert.AreEqual("Please look at the fiscal qu…", Read()[0].name);
        }

        [Test]
        public void AThreadWithNoPromptOnRecordIsNamedByItsId()
        {
            Write(Id, 1000, Meta(Id, repo), Event("task_started", At0));
            Assert.AreEqual("Codex 0199aaaa", Read()[0].name);
        }

        [Test]
        public void AThreadOutsideAnyRepoHasNoProject()
        {
            Write(Id, 1000, Meta(Id, Path.Combine(tmp, "nowhere")), Event("task_started", At0));
            var row = Read()[0];
            Assert.AreEqual("", row.repoRoot);
            Assert.AreEqual(RepoResolver.NoProject, row.repoName);
            Assert.AreEqual("cd " + Path.Combine(tmp, "nowhere") + " && codex resume " + Id, row.enter);
        }

        // Threads nobody started by hand, threads with no turn yet, old ones, and files that are not session files.
        [Test]
        public void WhatIsNotShown()
        {
            Write("0199aaaa-0000-7000-8000-000000000002", 1000, Meta("0199aaaa-0000-7000-8000-000000000002", repo, "guardian_review"), Event("task_started", At0));
            Write("0199aaaa-0000-7000-8000-000000000003", 1000, Meta("0199aaaa-0000-7000-8000-000000000003", repo, "subagent"), Event("task_started", At0));
            Write("0199aaaa-0000-7000-8000-000000000004", 1000, Meta("0199aaaa-0000-7000-8000-000000000004", repo), Prompt("hello"));
            Write("0199aaaa-0000-7000-8000-000000000005", 13 * 3600000L, Meta("0199aaaa-0000-7000-8000-000000000005", repo), Event("task_started", At0), Event("task_complete", At5));
            var kept = Write(Id, 1000, Meta(Id, repo), Event("task_started", At0));
            File.Copy(kept, kept + ".zst");
            File.Copy(kept, Path.Combine(Path.GetDirectoryName(kept), "notes.jsonl"));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(kept), "rollout-2026-01-02T10-00-00-broken.jsonl"), "not json\n");
            var rows = Read();
            Assert.AreEqual(1, rows.Count);
            Assert.AreEqual(Id, rows[0].sessionId);
        }

        [Test]
        public void TurnedOffNothingIsRead()
        {
            Write(Id, 1000, Meta(Id, repo), Event("task_started", At0));
            settings.codex = false;
            Assert.AreEqual(0, Read().Count);
        }

        [Test]
        public void NoCodexFolderNoRowsAndNoError() => Assert.AreEqual(0, Read().Count);

        // The same reader, asked again after the file grew, reads what was added; asked again with nothing changed, it
        // answers the same.
        [Test]
        public void AFileThatGrewIsReadAgain()
        {
            var path = Write(Id, 1000, Meta(Id, repo), Prompt("hello"), Event("task_started", At0));
            var reader = new CodexReader(settings, codex);
            Assert.AreEqual("Running", reader.Read(Now)[0].state);
            Assert.AreEqual("Running", reader.Read(Now)[0].state);
            File.AppendAllText(path, Event("task_complete", At5) + "\n");
            File.SetLastWriteTimeUtc(path, DateTimeOffset.FromUnixTimeMilliseconds(Now - 500).UtcDateTime);
            var row = reader.Read(Now)[0];
            Assert.AreEqual("Finished", row.state);
            Assert.AreEqual("hello", row.name);
        }

        [TestCase("/work/alpha", "cd /work/alpha && codex resume " + Id)]
        [TestCase("/work/my app", "cd '/work/my app' && codex resume " + Id)]
        [TestCase("", "codex resume " + Id)]
        public void TheCommandThatOpensACodexThread(string cwd, string expected) => Assert.AreEqual(expected, RosterFormat.EnterCodex(Id, cwd));

        [Test]
        public void NoThreadNoCommand() => Assert.AreEqual("", RosterFormat.EnterCodex("", "/work/alpha"));

        // In the roster, Codex rows stand among the Claude ones in the usual order, and a pass in which nothing changed
        // gives the very same snapshot.
        [Test]
        public void TheCollectorPutsCodexRowsAmongTheRest()
        {
            Write(Id, 1000, Meta(Id, repo), Prompt("hello"), Event("task_started", At0), Said(Asked));
            var home = Path.Combine(tmp, "home");
            const string agents = "[{\"pid\":1,\"cwd\":\"/work/a\",\"kind\":\"interactive\",\"startedAt\":1,\"sessionId\":\"s-1\",\"name\":\"a\",\"status\":\"idle\"}]";
            var claudeOnly = new Settings { configDirs = new List<string> { Path.Combine(home, ".claude") } };
            var reader = new CodexReader(claudeOnly, codex);
            using (var c = new Collector(claudeOnly, home, dir => agents, dir => "same", reader.Read))
            {
                var first = c.Poll(Now);
                Assert.AreEqual(2, first.agents.Count);
                // A thread waiting for an answer stands before a session that is done.
                Assert.AreEqual("codex", first.agents[0].account);
                Assert.AreEqual("BlockedOnQuestion", first.agents[0].state);
                Assert.AreEqual("Finished", first.agents[1].state);
                Assert.AreSame(first, c.Poll(Now + 2000), "nothing changed");
                Write(Id, 1000, Meta(Id, repo), Prompt("hello"), Event("task_started", At0), Said(Asked), Prompt("this one"), Event("task_complete", At5));
                var second = c.Poll(Now + 4000);
                Assert.AreNotSame(first, second);
                Assert.AreEqual("Finished", second.agents.Find(a => a.account == "codex").state);
            }
        }
    }
}
