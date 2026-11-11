using System;
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
    }
}
