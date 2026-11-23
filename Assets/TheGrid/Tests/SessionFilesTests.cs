using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace TheGrid.Core.Tests
{
    public class SessionFilesTests
    {
        const string S2 = "00000000-0000-4000-8000-000000000002";
        string cfg, transcript;

        [SetUp]
        public void SetUp()
        {
            cfg = Path.Combine(Path.GetTempPath(), "grid-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(cfg, "sessions"));
            Directory.CreateDirectory(Path.Combine(cfg, "projects", "p1"));
            File.WriteAllText(Path.Combine(cfg, "sessions", "1002.json"), "{\"pid\":1002,\"status\":\"waiting\",\"statusUpdatedAt\":1794000123000,\"entrypoint\":\"cli\"}");
            transcript = Path.Combine(cfg, "projects", "p1", S2 + ".jsonl");
            File.Copy(Path.Combine(Directory.GetCurrentDirectory(), "fixtures", "claude-code", "transcript-records.jsonl"), transcript);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(cfg, true);

        [Test]
        public void ReadsSessionFile()
        {
            Assert.IsTrue(SessionFiles.TryReadSessionFile(cfg, 1002, out var f));
            Assert.AreEqual(1794000123000L, f.StatusUpdatedAt);
            Assert.AreEqual("cli", f.Entrypoint);
        }

        [Test]
        public void MissingOrBrokenSessionFileIsNotAnError()
        {
            Assert.IsFalse(SessionFiles.TryReadSessionFile(cfg, 9999, out _));
            File.WriteAllText(Path.Combine(cfg, "sessions", "7.json"), "{ half");
            Assert.IsFalse(SessionFiles.TryReadSessionFile(cfg, 7, out _));
        }

        [Test]
        public void FindsTranscriptBySessionId()
        {
            Assert.AreEqual(transcript, SessionFiles.FindTranscript(cfg, S2));
            Assert.IsNull(SessionFiles.FindTranscript(cfg, "missing"));
            Assert.IsNull(SessionFiles.FindTranscript(Path.Combine(cfg, "nowhere"), S2));
        }

        [Test]
        public void TailGivesLastBranchAndPullRequest()
        {
            var m = SessionFiles.ReadTranscriptTail(transcript);
            Assert.AreEqual("feature/demo-12-example-change", m.Branch);
            Assert.AreEqual(7, m.PrNumber);
            Assert.AreEqual("https://example.invalid/demo/alpha/pull/7", m.PrUrl);
        }

        [Test]
        public void LaterBranchWins()
        {
            File.AppendAllText(transcript, "{\"type\":\"user\",\"gitBranch\":\"feature/demo-13-next\"}\n");
            Assert.AreEqual("feature/demo-13-next", SessionFiles.ReadTranscriptTail(transcript).Branch);
        }

        [Test]
        public void HalfWrittenLastLineIsSkipped()
        {
            File.AppendAllText(transcript, "{\"type\":\"user\",\"gitBranch\":\"half");
            Assert.AreEqual("feature/demo-12-example-change", SessionFiles.ReadTranscriptTail(transcript).Branch);
        }

        [Test]
        public void GarbageLineIsSkipped()
        {
            File.AppendAllText(transcript, "not json but mentions \"gitBranch\"\n");
            Assert.AreEqual("feature/demo-12-example-change", SessionFiles.ReadTranscriptTail(transcript).Branch);
        }

        [Test]
        public void TailStartingMidLineDoesNotThrowAndReadsOnlyTheWindow()
        {
            var m = SessionFiles.ReadTranscriptTail(transcript, 300);
            Assert.AreEqual(0, m.PrNumber);
            Assert.AreEqual("", m.PrUrl);
        }

        const string Asks = "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"name\":\"AskUserQuestion\",\"input\":{\"questions\":[" +
            "{\"question\":\"Which made-up colour?\",\"header\":\"Colour\",\"options\":[{\"label\":\"Red\",\"description\":\"\"}],\"multiSelect\":false}," +
            "{\"question\":\"And a second made-up one?\"}]}}]}}\n";
        const string Edits = "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"name\":\"Edit\",\"input\":{}}]}}\n";

        [Test]
        public void TheOpenQuestionIsTheFirstOneAndACountOfTheRest()
        {
            File.AppendAllText(transcript, Edits + Asks);
            Assert.AreEqual("Which made-up colour? +1", SessionFiles.ReadTranscriptTail(transcript).Question);
        }

        [Test]
        public void AQuestionTheAgentHasMovedOnFromIsNotOpen()
        {
            File.AppendAllText(transcript, Asks + Edits);
            Assert.AreEqual("", SessionFiles.ReadTranscriptTail(transcript).Question);
        }

        [Test]
        public void ASubagentsQuestionIsNotTheSessions()
        {
            File.AppendAllText(transcript, Edits + Asks.Replace("{\"type\":\"assistant\",", "{\"type\":\"assistant\",\"isSidechain\":true,"));
            Assert.AreEqual("", SessionFiles.ReadTranscriptTail(transcript).Question);
        }

        [Test]
        public void AQuestionCallWithNoQuestionsIsNoQuestion()
        {
            File.AppendAllText(transcript, Asks.Substring(0, Asks.IndexOf("\"input\"")) + "\"input\":{\"questions\":[]}}]}}\n");
            Assert.AreEqual("", SessionFiles.ReadTranscriptTail(transcript).Question);
            File.AppendAllText(transcript, Asks.Substring(0, Asks.IndexOf("\"input\"")) + "\"input\":{}}]}}\n");
            Assert.AreEqual("", SessionFiles.ReadTranscriptTail(transcript).Question);
        }

        [Test]
        public void MissingTranscriptGivesEmptyMeta()
        {
            var m = SessionFiles.ReadTranscriptTail("/no/such/file");
            Assert.AreEqual("", m.Branch);
            Assert.AreEqual(0, m.PrNumber);
        }

        [Test]
        public void AWindowThatStartsMidFileStillReadsTheCompleteLinesAfterIt()
        {
            long size = new FileInfo(transcript).Length;
            Assert.Greater(size, 1300);
            Assert.AreEqual("feature/demo-12-example-change", SessionFiles.ReadTranscriptTail(transcript, 1300).Branch);
        }

        [Test]
        public void ChangeStampMovesWhenASessionFileIsRewrittenAddedOrRemoved()
        {
            var first = SessionFiles.ChangeStamp(cfg);
            Assert.AreEqual(first, SessionFiles.ChangeStamp(cfg));
            var file = Path.Combine(cfg, "sessions", "1002.json");
            File.WriteAllText(file, "{\"pid\":1002,\"status\":\"idle\",\"statusUpdatedAt\":1794000999000}");
            File.SetLastWriteTimeUtc(file, new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var rewritten = SessionFiles.ChangeStamp(cfg);
            Assert.AreNotEqual(first, rewritten);
            File.WriteAllText(Path.Combine(cfg, "sessions", "1003.json"), "{}");
            var added = SessionFiles.ChangeStamp(cfg);
            Assert.AreNotEqual(rewritten, added);
            File.Delete(Path.Combine(cfg, "sessions", "1003.json"));
            Assert.AreEqual(rewritten, SessionFiles.ChangeStamp(cfg));
            Assert.AreEqual("", SessionFiles.ChangeStamp(Path.Combine(cfg, "nowhere")));
        }

        [Test]
        public void ChangeStampMovesWhenABackgroundSessionsJobFileIsAddedRewrittenOrRemoved()
        {
            var before = SessionFiles.ChangeStamp(cfg);
            var job = Path.Combine(cfg, "jobs", "bg000001");
            Directory.CreateDirectory(job);   // a job with no state file yet
            Assert.AreEqual(before, SessionFiles.ChangeStamp(cfg));
            var state = Path.Combine(job, "state.json");
            File.WriteAllText(state, "{}");
            var added = SessionFiles.ChangeStamp(cfg);
            Assert.AreNotEqual(before, added);
            File.WriteAllText(state, "{\"needs\":\"an answer\"}");
            Assert.AreNotEqual(added, SessionFiles.ChangeStamp(cfg));
            Directory.Delete(Path.Combine(cfg, "jobs"), true);
            Assert.AreEqual(before, SessionFiles.ChangeStamp(cfg));
        }

        [Test]
        public void ChangeStampCountsJobFilesInAFolderWithNoSessions()
        {
            var other = Path.Combine(cfg, "other");
            Directory.CreateDirectory(Path.Combine(other, "jobs", "bg000001"));
            File.WriteAllText(Path.Combine(other, "jobs", "bg000001", "state.json"), "{}");
            Assert.AreNotEqual("", SessionFiles.ChangeStamp(other));
        }

        const string ApiError = "{\"type\":\"assistant\",\"isSidechain\":false,\"isApiErrorMessage\":true,\"error\":\"server_error\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"API Error\"}]}}\n";
        const string TurnDuration = "{\"type\":\"system\",\"subtype\":\"turn_duration\",\"isSidechain\":false}\n";

        [Test]
        public void AConversationThatEndsOnAnApiErrorSaysSo()
        {
            Assert.IsFalse(SessionFiles.ReadTranscriptTail(transcript).ApiError);
            File.AppendAllText(transcript, ApiError);
            Assert.IsTrue(SessionFiles.ReadTranscriptTail(transcript).ApiError);
            File.AppendAllText(transcript, TurnDuration + "{\"type\":\"last-prompt\",\"role\":\"user\"}\n");
            Assert.IsTrue(SessionFiles.ReadTranscriptTail(transcript).ApiError, "bookkeeping records do not count");
        }

        [Test]
        public void AnApiErrorTheConversationHasMovedOnFromIsNotShown()
        {
            File.AppendAllText(transcript, ApiError + TurnDuration + "{\"type\":\"user\",\"isSidechain\":false,\"message\":{\"role\":\"user\",\"content\":\"go on\"}}\n");
            Assert.IsFalse(SessionFiles.ReadTranscriptTail(transcript).ApiError);
            File.AppendAllText(transcript, "{\"type\":\"user\",\"isSidechain\":true,\"message\":{\"role\":\"user\",\"content\":\"a subagent\"}}\n" + ApiError.Replace("\"isSidechain\":false", "\"isSidechain\":true"));
            Assert.IsFalse(SessionFiles.ReadTranscriptTail(transcript).ApiError, "a subagent's error is not the session's");
        }

        [Test]
        public void ReadNewLinesGivesOnlyMarkedCompleteLinesPastTheOffset()
        {
            var p = Path.Combine(cfg, "projects", "p1", "t.jsonl");
            File.WriteAllText(p, "{\"a\":1}\n{\"k\":\"hit\"}\n{\"k\":\"hit\",\"half");
            var markers = new[] { "\"hit\"", "\"other\"" };
            var seen = new List<string>();
            var offsets = new List<long>();
            long at = SessionFiles.ReadNewLines(p, 0, markers, (l, o) => { seen.Add(l); offsets.Add(o); });
            CollectionAssert.AreEqual(new[] { "{\"k\":\"hit\"}" }, seen); // the unmarked line is skipped, the half line is left
            CollectionAssert.AreEqual(new[] { 8L }, offsets);
            Assert.AreEqual(20, at);
            Assert.AreEqual("{\"k\":\"hit\"}", SessionFiles.ReadLineAt(p, 8));
            Assert.IsNull(SessionFiles.ReadLineAt(p, 20)); // a line with no newline yet
            Assert.IsNull(SessionFiles.ReadLineAt("/no/such/file", 0));

            File.AppendAllText(p, "\"}\n");
            long at2 = SessionFiles.ReadNewLines(p, at, markers, (l, o) => { seen.Add(l); offsets.Add(o); });
            Assert.AreEqual(new FileInfo(p).Length, at2);
            Assert.AreEqual(2, seen.Count); // only the appended line, now complete
            Assert.AreEqual(20L, offsets[1]);
            Assert.AreEqual(at2, SessionFiles.ReadNewLines(p, at2, markers, (l, o) => seen.Add(l)));
            Assert.AreEqual(2, seen.Count); // nothing new, nothing read
        }

        [Test]
        public void ReadNewLinesSurvivesReplacedFilesAndLongLines() // Review Focus 3
        {
            var p = Path.Combine(cfg, "projects", "p1", "t.jsonl");
            var markers = new[] { "\"hit\"" };
            var seen = new List<string>();
            File.WriteAllText(p, "{\"k\":\"hit\"}\n");
            Assert.AreEqual(12, SessionFiles.ReadNewLines(p, 500, markers, (l, o) => seen.Add(l))); // shorter than the offset: read from the start
            Assert.AreEqual(1, seen.Count);

            var longLine = "{\"pad\":\"" + new string('x', 5000) + "\",\"k\":\"hit\"}";
            File.WriteAllText(p, "{\"a\":1}\n" + longLine + "\n{\"k\":\"hit\"}\n{\"é\":\"hit\"}\n");
            seen.Clear();
            var offsets = new List<long>();
            long end = SessionFiles.ReadNewLines(p, 0, markers, (l, o) => { seen.Add(l); offsets.Add(o); }, bufferBytes: 64); // a line longer than the buffer
            CollectionAssert.AreEqual(new[] { longLine, "{\"k\":\"hit\"}", "{\"é\":\"hit\"}" }, seen);
            Assert.AreEqual(new FileInfo(p).Length, end);
            foreach (var i in new[] { 0, 1, 2 }) Assert.AreEqual(seen[i], SessionFiles.ReadLineAt(p, offsets[i]));
            Assert.AreEqual(-1, SessionFiles.ReadNewLines("/no/such/file", 7, markers, (l, o) => seen.Add(l))); // could not be read: try again later
        }

        [Test]
        public void TranscriptsListsTopLevelFilesOnly()
        {
            var p = Path.Combine(cfg, "projects", "p1", "t.jsonl");
            File.WriteAllText(p, "");
            var sub = Path.Combine(cfg, "projects", "p1", S2, "subagents");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "agent-a1.jsonl"), ""); // a subagent's transcript is not a session's
            CollectionAssert.AreEqual(new[] { transcript, p }, SessionFiles.Transcripts(cfg));
            Assert.AreEqual(0, SessionFiles.Transcripts(Path.Combine(cfg, "nowhere")).Count);
        }
    }
}
