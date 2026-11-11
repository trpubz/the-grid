using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;

namespace TheGrid.Core.Tests
{
    // Runs real child processes: small shell scripts standing in for the claude program.
    public class ClaudeCliTests
    {
        string tmp, home;

        [SetUp]
        public void SetUp()
        {
            tmp = Path.Combine(Path.GetTempPath(), "grid-tests-" + Guid.NewGuid().ToString("N"));
            home = Path.Combine(tmp, "home");
            Directory.CreateDirectory(home);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(tmp, true);

        string Script(string body)
        {
            var path = Path.Combine(tmp, "fake-claude-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
            using (var chmod = Process.Start("/bin/chmod", "+x \"" + path + "\"")) chmod.WaitForExit();
            return path;
        }

        [Test]
        public void ReturnsWhatTheProgramPrintsAndPassesTheArguments()
        {
            Assert.AreEqual("agents --json", ClaudeCli.AgentsJson(Script("echo \"$1 $2\""), Path.Combine(home, ".claude"), home).Trim());
        }

        [Test]
        public void NamesTheConfigDirectoryExceptForTheDefaultOne()
        {
            var script = Script("echo \"${CLAUDE_CONFIG_DIR:-unset}\"");
            var other = Path.Combine(home, ".claude-two");
            Assert.AreEqual(other, ClaudeCli.AgentsJson(script, other, home).Trim());
            Assert.AreEqual("unset", ClaudeCli.AgentsJson(script, Path.Combine(home, ".claude"), home).Trim());
        }

        [Test]
        public void ANonZeroExitIsAnErrorThatCarriesTheProgramsMessage()
        {
            var e = Assert.Throws<InvalidOperationException>(() => ClaudeCli.AgentsJson(Script("echo boom >&2; exit 3"), Path.Combine(home, ".claude"), home));
            StringAssert.Contains("3", e.Message);
            StringAssert.Contains("boom", e.Message);
        }

        [Test]
        public void AProgramThatHangsIsStoppedAtTheTimeout()
        {
            var started = DateTime.UtcNow;
            Assert.Throws<TimeoutException>(() => ClaudeCli.AgentsJson(Script("sleep 5"), Path.Combine(home, ".claude"), home, 300));
            Assert.Less((DateTime.UtcNow - started).TotalSeconds, 3.0);
        }

        [Test]
        public void AMissingProgramIsAnError()
        {
            Assert.Throws<InvalidOperationException>(() => ClaudeCli.AgentsJson("", Path.Combine(home, ".claude"), home));
        }
    }
}
