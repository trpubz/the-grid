using System;
using System.IO;
using NUnit.Framework;

namespace TheGrid.Core.Tests
{
    public class ActivityTests
    {
        [TestCase("Edit", "", "", "editing")]
        [TestCase("Write", "", "", "editing")]
        [TestCase("NotebookEdit", "", "", "editing")]
        [TestCase("Read", "", "", "reading")]
        [TestCase("Grep", "", "", "reading")]
        [TestCase("Glob", "", "", "reading")]
        [TestCase("WebFetch", "", "", "reading")]
        [TestCase("WebSearch", "", "", "reading")]
        [TestCase("Bash", "pytest -q tests/", "", "testing")]
        [TestCase("Bash", "npm test", "", "testing")]
        [TestCase("Bash", "cargo test --all", "", "testing")]
        [TestCase("Bash", "xcodebuild test -scheme App", "", "testing")]
        [TestCase("Bash", "Unity -batchmode -runTests", "", "testing")]
        [TestCase("Bash", "git diff main...HEAD", "", "reviewing")]
        [TestCase("Bash", "git log --oneline", "", "reviewing")]
        [TestCase("Bash", "gh pr create --fill", "", "reviewing")]
        [TestCase("Bash", "git commit -m x && git push", "", "reviewing")]
        [TestCase("Bash", "git commit -m 'fix the test'", "", "reviewing")]
        [TestCase("Skill", "code-review", "", "reviewing")]
        [TestCase("Bash", "ls -la", "", "working")]
        [TestCase("Bash", "cat latest.txt", "", "working")]
        [TestCase("Agent", "", "", "working")]
        [TestCase("mcp__anything__tool", "", "", "working")]
        [TestCase("", "", "Reviewing commits before pushing the PR", "reviewing")]
        [TestCase("", "", "Editing the file now", "editing")]
        [TestCase("", "", "Running the tests", "testing")]
        [TestCase("", "", "Reading the config", "reading")]
        [TestCase("", "", "Thinking about lunch", "working")]
        [TestCase("", "", "The latest build is ready", "working")]
        [TestCase("", "", "", "working")]
        [TestCase(null, null, null, "working")]
        public void Classify(string tool, string command, string text, string expected) => Assert.AreEqual(expected, Activity.Classify(tool, command, text));

        string tmp, transcript;

        [SetUp]
        public void SetUp()
        {
            tmp = Path.Combine(Path.GetTempPath(), "grid-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            transcript = Path.Combine(tmp, "session.jsonl");
            File.Copy(Path.Combine(Directory.GetCurrentDirectory(), "fixtures", "claude-code", "transcript-records.jsonl"), transcript);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(tmp, true);

        void Append(string line) => File.AppendAllText(transcript, line);

        static string Assistant(string blocks, string extra = "") =>
            "{\"type\":\"assistant\"," + extra + "\"gitBranch\":\"feature/demo-12-example-change\",\"message\":{\"role\":\"assistant\",\"content\":[" + blocks + "]}}\n";

        [Test]
        public void TheNewestAssistantRecordDecides()
        {
            // the fixture ends on an AskUserQuestion tool call
            Assert.AreEqual("working", SessionFiles.ReadTranscriptTail(transcript).Activity);

            Append(Assistant("{\"type\":\"text\",\"text\":\"I will run the tests after this\"},{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":\"Edit\",\"input\":{\"file_path\":\"/work/alpha/a.cs\"}}"));
            Assert.AreEqual("editing", SessionFiles.ReadTranscriptTail(transcript).Activity);

            Append(Assistant("{\"type\":\"tool_use\",\"id\":\"t2\",\"name\":\"Bash\",\"input\":{\"command\":\"pytest -q\"}}"));
            Assert.AreEqual("testing", SessionFiles.ReadTranscriptTail(transcript).Activity);

            Append(Assistant("{\"type\":\"tool_use\",\"id\":\"t3\",\"name\":\"Skill\",\"input\":{\"skill\":\"code-review\"}}"));
            Assert.AreEqual("reviewing", SessionFiles.ReadTranscriptTail(transcript).Activity);

            Append(Assistant("{\"type\":\"text\",\"text\":\"Reading the config to see what changed\"}"));
            Assert.AreEqual("reading", SessionFiles.ReadTranscriptTail(transcript).Activity);
        }

        [Test]
        public void RecordsThatAreNotTheAgentsOwnWordsAreIgnored()
        {
            Append(Assistant("{\"type\":\"text\",\"text\":\"Reviewing commits before pushing\"}"));
            Assert.AreEqual("reviewing", SessionFiles.ReadTranscriptTail(transcript).Activity);

            // a tool result and a typed prompt come after it; neither is the agent
            Append("{\"type\":\"user\",\"gitBranch\":\"feature/demo-12-example-change\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"t9\",\"content\":\"assistant edited the file\"}]}}\n");
            Append("{\"type\":\"user\",\"gitBranch\":\"feature/demo-12-example-change\",\"message\":{\"role\":\"user\",\"content\":\"assistant, please edit and test this\"}}\n");
            Assert.AreEqual("reviewing", SessionFiles.ReadTranscriptTail(transcript).Activity);

            // a subagent's record written into the same file
            Append(Assistant("{\"type\":\"tool_use\",\"id\":\"t4\",\"name\":\"Write\",\"input\":{}}", "\"isSidechain\":true,"));
            Assert.AreEqual("reviewing", SessionFiles.ReadTranscriptTail(transcript).Activity);

            // a line still being written
            Append("{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"name\":\"Edit\"");
            var meta = SessionFiles.ReadTranscriptTail(transcript);
            Assert.AreEqual("reviewing", meta.Activity);
            Assert.AreEqual("feature/demo-12-example-change", meta.Branch); // the branch and pull request are still read
            Assert.AreEqual(7, meta.PrNumber);
        }

        [Test]
        public void AnAssistantRecordWithNeitherToolNorTextIsWorking()
        {
            Append(Assistant("{\"type\":\"text\",\"text\":\"Editing the file now\"}"));
            Append(Assistant("{\"type\":\"thinking\",\"thinking\":\"...\"}"));
            Assert.AreEqual("working", SessionFiles.ReadTranscriptTail(transcript).Activity);
        }

        [Test]
        public void NoAssistantRecordGivesNoActivity()
        {
            File.WriteAllText(transcript, "{\"type\":\"user\",\"gitBranch\":\"main\",\"message\":{\"role\":\"user\",\"content\":\"hello\"}}\n");
            Assert.AreEqual("", SessionFiles.ReadTranscriptTail(transcript).Activity);
            Assert.AreEqual("", SessionFiles.ReadTranscriptTail("/no/such/file").Activity);
        }
    }
}
