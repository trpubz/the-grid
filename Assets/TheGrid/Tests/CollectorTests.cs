using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace TheGrid.Core.Tests
{
    public class CollectorTests
    {
        const long Now = 1794000200000L;
        string tmp, home, agentsJson;
        Settings settings;

        [SetUp]
        public void SetUp()
        {
            tmp = Path.Combine(Path.GetTempPath(), "grid-tests-" + Guid.NewGuid().ToString("N"));
            home = Path.Combine(tmp, "home");
            Directory.CreateDirectory(home);
            agentsJson = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "fixtures", "claude-code", "agents.json"));
            settings = new Settings { configDirs = new List<string> { Path.Combine(home, ".claude") } };
        }

        [TearDown]
        public void TearDown() => Directory.Delete(tmp, true);

        [Test]
        public void LatestIsEmptyUntilTheFirstPoll()
        {
            using (var c = new Collector(settings, home, dir => agentsJson))
            {
                Assert.AreEqual(0, c.Latest.agents.Count);
                Assert.AreEqual(5, c.Poll(Now).agents.Count);
                Assert.AreEqual(5, c.Latest.agents.Count);
            }
        }

        [Test]
        public void EveryConfigDirectoryIsAskedAndLabelled()
        {
            settings.configDirs.Add(Path.Combine(home, ".claude-two"));
            var asked = new List<string>();
            using (var c = new Collector(settings, home, dir => { asked.Add(dir); return agentsJson; }))
            {
                var snap = c.Poll(Now);
                CollectionAssert.AreEqual(settings.configDirs, asked);
                Assert.AreEqual(10, snap.agents.Count);
                CollectionAssert.AreEquivalent(new[] { "claude", "two" }, snap.agents.Select(a => a.account).Distinct().ToArray());
            }
        }

        [Test]
        public void AFailingCommandKeepsTheLastRowsAndReportsOneError()
        {
            int calls = 0;
            using (var c = new Collector(settings, home, dir => ++calls == 1 ? agentsJson : throw new InvalidOperationException("claude not found")))
            {
                c.Poll(Now);
                var second = c.Poll(Now + 2000);
                Assert.AreEqual(5, second.agents.Count);
                Assert.IsTrue(second.errors.Single().Contains("claude not found"), second.errors.Single());
                Assert.IsTrue(second.agents.All(a => a.state != "Offline"));
            }
        }

        [Test]
        public void UnreadableOutputKeepsTheLastRowsAndReportsOneError()
        {
            int calls = 0;
            using (var c = new Collector(settings, home, dir => ++calls == 1 ? agentsJson : "<html>not json</html>"))
            {
                c.Poll(Now);
                var second = c.Poll(Now + 2000);
                Assert.AreEqual(5, second.agents.Count);
                Assert.AreEqual(1, second.errors.Count);
                Assert.IsTrue(second.agents.All(a => a.state != "Offline"));
            }
        }

        [Test]
        public void AFailureOnTheVeryFirstPollGivesAnErrorAndNoRows()
        {
            using (var c = new Collector(settings, home, dir => throw new InvalidOperationException("claude not found")))
            {
                var snap = c.Poll(Now);
                Assert.AreEqual(0, snap.agents.Count);
                Assert.AreEqual(1, snap.errors.Count);
            }
        }

        [Test]
        public void StartPollsInTheBackgroundAndDisposeStopsIt()
        {
            int calls = 0;
            var c = new Collector(settings, home, dir => { Interlocked.Increment(ref calls); return agentsJson; });
            c.Start(20);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (c.Latest.agents.Count == 0 && DateTime.UtcNow < deadline) Thread.Sleep(10);
            Assert.AreEqual(5, c.Latest.agents.Count);
            c.Dispose();
            int after = Volatile.Read(ref calls);
            Thread.Sleep(150);
            Assert.AreEqual(after, Volatile.Read(ref calls));
        }

        [Test]
        public void ResolvePathPrefersTheConfiguredFileThenTheUsualPlaces()
        {
            var usual = Path.Combine(home, ".local", "bin", "claude");
            CollectionAssert.AreEqual(new[] { usual, "/opt/homebrew/bin/claude", "/usr/local/bin/claude" }, ClaudeCli.UsualPlaces(home));
            var places = new[] { usual, Path.Combine(tmp, "elsewhere", "claude") };
            Assert.AreEqual("", ClaudeCli.ResolvePath("", places));
            var fake = Path.Combine(tmp, "claude");
            File.WriteAllText(fake, "");
            Assert.AreEqual(fake, ClaudeCli.ResolvePath(fake, places));
            Assert.AreEqual("", ClaudeCli.ResolvePath(Path.Combine(tmp, "gone"), places)); // a configured path that is wrong is not silently replaced
            Directory.CreateDirectory(Path.GetDirectoryName(usual));
            File.WriteAllText(usual, "");
            Assert.AreEqual(usual, ClaudeCli.ResolvePath("", places));
        }
    }
}
