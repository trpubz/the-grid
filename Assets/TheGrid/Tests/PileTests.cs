using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace TheGrid.Core.Tests
{
    public class PileTests
    {
        const long Now = 1794000200000L;
        string tmp, home, cfg, statePath, senderPath, recipientPath, desktopPath, alphaRepo, betaRepo;
        Settings settings;
        Pile pile;

        [SetUp]
        public void SetUp()
        {
            tmp = Path.Combine(Path.GetTempPath(), "grid-tests-" + Guid.NewGuid().ToString("N"));
            home = Path.Combine(tmp, "home");
            cfg = Path.Combine(home, ".claude");
            statePath = Path.Combine(tmp, "data", "pile.json");
            alphaRepo = Path.Combine(tmp, "repos", "alpha");
            betaRepo = Path.Combine(tmp, "repos", "beta");
            foreach (var repo in new[] { "alpha", "beta", "gamma" }) Directory.CreateDirectory(Path.Combine(tmp, "repos", repo, ".git"));
            senderPath = Place("sender.jsonl", "p1");
            recipientPath = Place("recipient.jsonl", "p2");
            desktopPath = Place("desktop.jsonl", "p3");
            settings = new Settings { configDirs = new List<string> { cfg } };
            pile = new Pile(settings, home, statePath);
        }

        [TearDown]
        public void TearDown()
        {
            pile.Dispose();
            Directory.Delete(tmp, true);
        }

        // A fixture copied into the temp config directory, with its working folders pointed at temp repos.
        string Place(string fixture, string project)
        {
            var dir = Path.Combine(cfg, "projects", project);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, fixture);
            File.WriteAllText(path, Fixture(fixture));
            return path;
        }

        string Fixture(string name) =>
            File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "fixtures", "claude-code", "messages", name)).Replace("/work/", Path.Combine(tmp, "repos") + "/");

        static string SentLine(string tool, string to, string message) =>
            "{\"type\":\"assistant\",\"uuid\":\"x-" + tool + "\",\"timestamp\":\"2026-01-02T09:00:00.000Z\",\"cwd\":\"/nowhere\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"" + tool
            + "\",\"name\":\"SendMessage\",\"input\":{\"to\":\"" + to + "\",\"message\":\"" + message + "\",\"recipient_kind\":\"name\"}}]}}\n";

        static string ResultLine(string tool, string msgId) =>
            "{\"type\":\"user\",\"uuid\":\"y-" + tool + "\",\"timestamp\":\"2026-01-02T09:00:00.000Z\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"" + tool
            + "\",\"content\":\"ok\"}]},\"toolUseResult\":{\"success\":true,\"msg_id\":\"" + msgId + "\"}}\n";

        // Every file under a folder: path, size, time last written and bytes. Equal before and after means nothing was touched.
        static string HashTree(string root)
        {
            var sb = new StringBuilder();
            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var f in files)
                sb.Append(f).Append('|').Append(new FileInfo(f).Length).Append('|').Append(File.GetLastWriteTimeUtc(f).Ticks).Append('|').Append(Convert.ToBase64String(File.ReadAllBytes(f))).Append('\n');
            foreach (var d in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderBy(d => d, StringComparer.Ordinal)) sb.Append(d).Append('\n');
            return sb.ToString();
        }

        [Test]
        public void EveryMessageAppearsOnce()
        {
            var rows = pile.Poll(Now).rows;
            Assert.AreEqual(4, rows.Count); // m-1001, m-1002, m-2001 and the desktop one; none for the two subagent sends or the subagent report
            var m1 = rows.Single(r => r.firstLine == "Synthetic message to a peer session.");
            Assert.AreEqual("alpha-02", m1.from);
            Assert.AreEqual("beta-01", m1.to);
            Assert.AreEqual("alpha", m1.fromRepoName);
            Assert.AreEqual("beta", m1.toRepoName);
            Assert.AreEqual("Synthetic handoff", m1.summary);
            Assert.AreEqual(1767268805000L, m1.at);
            Assert.AreEqual("claude", m1.account);
            Assert.IsFalse(m1.ticked);

            var m2 = rows.Single(r => r.summary == "Synthetic follow-up");   // sent to a socket address, received mid-turn
            Assert.AreEqual("beta-01", m2.to);

            var reply = rows.Single(r => r.summary == "Synthetic reply");
            Assert.AreEqual("beta-01", reply.from);
            Assert.AreEqual("alpha-02", reply.to);

            var desk = rows.Single(r => r.summary == "Synthetic desktop note"); // no shared id: paired by text
            Assert.AreEqual("Alpha desk", desk.from); // the name the desktop app gave the sender
            Assert.AreEqual("Gamma review", desk.to);
            Assert.AreEqual("gamma", desk.toRepoName);

            Assert.AreEqual(4, rows.Select(r => r.key).Distinct().Count());
            CollectionAssert.AreEqual(rows.OrderByDescending(r => r.at).Select(r => r.key).ToList(), rows.Select(r => r.key).ToList()); // newest first
        }

        [Test]
        public void ACopiedTranscriptAddsNothing() // Review Focus 2: a forked session repeats its parent's records
        {
            pile.Poll(Now);
            Place("sender.jsonl", "p4");
            Directory.CreateDirectory(Path.Combine(cfg, "projects", "p5"));
            File.Copy(recipientPath, Path.Combine(cfg, "projects", "p5", "fork.jsonl"));
            File.Copy(desktopPath, Path.Combine(cfg, "projects", "p5", "fork2.jsonl"));
            Assert.AreEqual(4, pile.Poll(Now + 2000).rows.Count);
        }

        [Test]
        public void ASendWithNoResultYetIsOneRowBeforeAndAfter() // Review Focus 1
        {
            var sent = new List<MessageEvent>();
            foreach (var line in File.ReadAllLines(senderPath)) Messages.Parse(line, sent);
            pile.Apply(sent.First(e => e.Kind == MessageEventKind.Sent), senderPath, cfg);
            pile.Refresh();
            Assert.AreEqual(1, pile.Latest.rows.Count);
            var key = pile.Latest.rows[0].key;
            Assert.AreEqual("beta-01", pile.Latest.rows[0].to);

            pile.Apply(sent.First(e => e.Kind == MessageEventKind.SendResult), senderPath, cfg);
            var got = new List<MessageEvent>();
            foreach (var line in File.ReadAllLines(recipientPath)) Messages.Parse(line, got);
            pile.Apply(got.First(e => e.Kind == MessageEventKind.Received), recipientPath, cfg);
            pile.Refresh();
            Assert.AreEqual(1, pile.Latest.rows.Count);
            Assert.AreEqual(key, pile.Latest.rows[0].key);
            Assert.AreEqual("beta", pile.Latest.rows[0].toRepoName);
        }

        [Test]
        public void AReceiptSeenBeforeItsSendIsStillOneRow()
        {
            var got = new List<MessageEvent>();
            foreach (var line in File.ReadAllLines(recipientPath)) Messages.Parse(line, got);
            pile.Apply(got.First(e => e.Kind == MessageEventKind.Received), recipientPath, cfg);
            pile.Refresh();
            var key = pile.Latest.rows.Single().key;
            Assert.AreEqual("alpha-02", pile.Latest.rows[0].from); // the name the recipient was told

            var sent = new List<MessageEvent>();
            foreach (var line in File.ReadAllLines(senderPath)) Messages.Parse(line, sent);
            pile.Apply(sent.First(e => e.Kind == MessageEventKind.Sent), senderPath, cfg);
            pile.Apply(sent.First(e => e.Kind == MessageEventKind.SendResult), senderPath, cfg);
            pile.Refresh();
            var row = pile.Latest.rows.Single();
            Assert.AreEqual(key, row.key);
            Assert.AreEqual("Synthetic handoff", row.summary);
            Assert.AreEqual("alpha", row.fromRepoName);
            Assert.AreEqual(1767268805000L, row.at); // the time it was sent, now that it is known
        }

        static string ReceivedLine(string uuid, string msgId, string senderName) =>
            "{\"type\":\"user\",\"uuid\":\"" + uuid + "\",\"timestamp\":\"2026-01-02T09:00:01.000Z\",\"cwd\":\"/nowhere\",\"message\":{\"role\":\"user\",\"content\":\"Hello.\"},\"origin\":{\"kind\":\"peer\",\"from\":\"uds:/tmp/demo-socks/9.sock\",\"name\":\""
            + senderName + "\",\"msg_id\":\"" + msgId + "\",\"body\":\"Hello.\"}}\n";

        // Many sessions have no name of their own, only a title the agent app generated. The names used in the message itself are better.
        [TestCase(true)]
        [TestCase(false)]
        public void NamesUsedInTheMessageBeatATranscriptsGeneratedTitle(bool receiptReadFirst)
        {
            foreach (var f in new[] { senderPath, recipientPath, desktopPath }) File.Delete(f);
            var sender = Path.Combine(cfg, "projects", "p1", "a.jsonl");
            var recipient = Path.Combine(cfg, "projects", "p2", "b.jsonl");
            File.WriteAllText(sender, "{\"type\":\"ai-title\",\"aiTitle\":\"Sender generated title\"}\n" + SentLine("toolu_n1", "written-name", "Hello.") + ResultLine("toolu_n1", "m-n1"));
            File.WriteAllText(recipient, "{\"type\":\"ai-title\",\"aiTitle\":\"Recipient generated title\"}\n" + ReceivedLine("n-u1", "m-n1", "sender-name"));
            File.SetLastWriteTimeUtc(receiptReadFirst ? sender : recipient, DateTime.UtcNow.AddMinutes(-5)); // the newer file is read first
            var row = pile.Poll(Now).rows.Single();
            Assert.AreEqual("sender-name", row.from);
            Assert.AreEqual("written-name", row.to);
        }

        [Test]
        public void ATranscriptsOwnLabelStandsInWhenTheMessageNamesNobody()
        {
            foreach (var f in new[] { senderPath, recipientPath, desktopPath }) File.Delete(f);
            var recipient = Path.Combine(cfg, "projects", "p2", "b.jsonl");
            File.WriteAllText(recipient, "{\"type\":\"ai-title\",\"aiTitle\":\"Recipient generated title\"}\n" + ReceivedLine("n-u1", "m-n1", ""));
            var row = pile.Poll(Now).rows.Single();
            Assert.AreEqual("uds:/tmp/demo-socks/9.sock", row.from); // no name given: the address is all there is
            Assert.AreEqual("Recipient generated title", row.to);
        }

        [Test]
        public void OneSidedMessagesAreListedAndNotFlagged() // Review Focus 5
        {
            File.Delete(senderPath);
            File.Delete(desktopPath);
            settings.workspaces = new List<string> { "Personal", "Work" };
            settings.repoWorkspaces.Add(new RepoWorkspace { repo = betaRepo, workspace = "Work" });
            var rows = pile.Poll(Now).rows;
            Assert.AreEqual(3, rows.Count); // two received with no sender record, one sent with no recipient record
            Assert.AreEqual(2, rows.Count(r => r.from == "alpha-02" && r.to == "beta-01"));
            Assert.IsTrue(rows.All(r => !r.crossWorkspace));
        }

        [Test]
        public void DifferentWorkspacesAreFlagged()
        {
            settings.workspaces = new List<string> { "Personal", "Work" };
            settings.repoWorkspaces.Add(new RepoWorkspace { repo = betaRepo, workspace = "Work" });
            var rows = pile.Poll(Now).rows;
            var crossing = rows.Where(r => r.fromRepoName == "alpha" && r.toRepoName == "beta").ToList();
            Assert.AreEqual(2, crossing.Count);
            Assert.IsTrue(crossing.All(r => r.crossWorkspace && r.fromWorkspace == "Personal" && r.toWorkspace == "Work"));
            Assert.IsTrue(rows.Single(r => r.fromRepoName == "beta").crossWorkspace);
            Assert.IsFalse(rows.Single(r => r.to == "Gamma review").crossWorkspace);

            settings.repoWorkspaces.Clear(); // the owner moves the repo back
            pile.Refresh();
            Assert.IsTrue(pile.Latest.rows.All(r => !r.crossWorkspace));
        }

        [Test]
        public void TickAndClearArchiveOnlyTickedRowsAndTouchNothingOfClaude()
        {
            pile.Poll(Now);
            var before = HashTree(cfg);
            var keys = pile.Latest.rows.Take(2).Select(r => r.key).ToList();
            pile.SetTicked(keys[0], true);
            pile.SetTicked(new[] { keys[1] }, true);
            Assert.AreEqual(2, pile.Latest.rows.Count(r => r.ticked));
            pile.SetTicked(keys[1], false);
            Assert.AreEqual(1, pile.Latest.rows.Count(r => r.ticked));
            pile.SetTicked(keys[1], true);

            Assert.AreEqual(2, pile.Clear());
            Assert.AreEqual(2, pile.Latest.rows.Count);
            Assert.AreEqual(2, pile.Latest.archived);
            Assert.IsFalse(pile.Latest.rows.Any(r => keys.Contains(r.key)));
            Assert.AreEqual(0, pile.Clear()); // nothing ticked: nothing archived
            Assert.AreEqual(before, HashTree(cfg));
            pile.Dispose();

            using (var again = new Pile(settings, home, statePath)) // review state survives a restart
            {
                Assert.AreEqual(2, again.Latest.rows.Count);
                Assert.AreEqual(2, again.Latest.archived);
                // Emptying and restoring each transcript makes the pile read all of it again.
                foreach (var f in new[] { senderPath, recipientPath, desktopPath })
                {
                    var text = File.ReadAllText(f);
                    File.WriteAllText(f, "");
                    again.Poll(Now + 2000);
                    File.WriteAllText(f, text);
                }
                Assert.AreEqual(2, again.Poll(Now + 4000).rows.Count); // archived rows do not come back
            }
            Assert.IsFalse(Directory.GetFiles(cfg, "*", SearchOption.AllDirectories).Any(f => f.Contains("pile")));
        }

        [Test]
        public void CorruptStateIsKeptAsideAndRebuilt() // Review Focus 4
        {
            Directory.CreateDirectory(Path.GetDirectoryName(statePath));
            File.WriteAllText(statePath, "{ half");
            using (var p = new Pile(settings, home, statePath))
            {
                Assert.AreEqual(0, p.Latest.rows.Count);
                Assert.IsTrue(p.Latest.reading);
                Assert.AreEqual("{ half", File.ReadAllText(statePath + ".bad"));
                Assert.AreEqual(4, p.Poll(Now).rows.Count);
            }
        }

        [Test]
        public void PollReadsEachFileOnceThenOnlyWhatIsAppended()
        {
            Assert.IsTrue(pile.Latest.reading);
            Assert.AreEqual(4, pile.Poll(Now).rows.Count);
            Assert.IsFalse(pile.Latest.reading);
            var same = pile.Latest;
            Assert.AreSame(same, pile.Poll(Now + 2000)); // nothing changed: nothing published
            File.AppendAllText(senderPath, SentLine("toolu_s9", "beta-01", "Later message.") + ResultLine("toolu_s9", "m-1009"));
            Assert.AreEqual(5, pile.Poll(Now + 4000).rows.Count);
            Assert.AreEqual("Later message.", pile.Latest.rows[0].firstLine);
            Assert.AreEqual(Now + 4000, pile.Latest.generatedAt);
        }

        [Test]
        public void AHalfWrittenSendIsPickedUpWhenItsLineCompletes() // Review Focus 3
        {
            var line = SentLine("toolu_s9", "beta-01", "Later message.");
            File.AppendAllText(senderPath, line.Substring(0, 40));
            Assert.AreEqual(4, pile.Poll(Now).rows.Count);
            File.AppendAllText(senderPath, line.Substring(40));
            Assert.AreEqual(5, pile.Poll(Now + 2000).rows.Count);
        }

        [Test]
        public void ATranscriptThatShrankIsReadAgainWithNoDuplicates() // Review Focus 3
        {
            pile.Poll(Now);
            var all = File.ReadAllText(recipientPath);
            File.WriteAllText(recipientPath, all.Substring(0, all.IndexOf('\n') + 1));
            Assert.AreEqual(4, pile.Poll(Now + 2000).rows.Count); // nothing lost
            File.WriteAllText(recipientPath, all);
            Assert.AreEqual(4, pile.Poll(Now + 4000).rows.Count); // nothing doubled
        }

        [Test]
        public void OffsetsSurviveARestartSoNothingIsReadTwice()
        {
            pile.Poll(Now);
            pile.Dispose();
            Assert.IsTrue(File.Exists(statePath));
            // Were the files read again from the start, these lines would show up as a fifth message.
            var state = File.ReadAllText(statePath);
            StringAssert.Contains("\"offset\":" + new FileInfo(senderPath).Length, state.Replace(" ", ""));
            using (var again = new Pile(settings, home, statePath))
            {
                Assert.IsFalse(again.Latest.reading);
                var before = again.Latest;
                Assert.AreSame(before, again.Poll(Now + 2000));
            }
        }

        [Test]
        public void PollNeverWritesUnderAConfigDirectory()
        {
            var before = HashTree(cfg);
            pile.Poll(Now);
            pile.SetTicked(pile.Latest.rows[0].key, true);
            pile.Clear();
            pile.Dispose();
            Assert.AreEqual(before, HashTree(cfg));
        }

        [Test]
        public void FullTextIsReadFromTheTranscriptOnDemand()
        {
            var rows = pile.Poll(Now).rows;
            Assert.AreEqual("Synthetic message to a peer session.\nSecond line of the handoff.", pile.ReadBody(rows.Single(r => r.summary == "Synthetic handoff").key));
            Assert.AreEqual("Synthetic note to a desktop session.\nIt has a second line.", pile.ReadBody(rows.Single(r => r.summary == "Synthetic desktop note").key));
            StringAssert.DoesNotContain("Second line of the handoff", File.ReadAllText(statePath)); // only the first line is kept in the pile's own file
            Assert.IsNull(pile.ReadBody("no-such-key"));

            var reply = rows.Single(r => r.summary == "Synthetic reply").key;
            File.Delete(recipientPath); // the transcript it was sent from is gone
            Assert.IsNull(pile.ReadBody(reply));
        }

        [Test]
        public void AMessageKnownOnlyFromItsReceiptStillHasFullText()
        {
            File.Delete(senderPath);
            var rows = pile.Poll(Now).rows;
            Assert.AreEqual("Synthetic follow-up sent while the peer was busy.", pile.ReadBody(rows.Single(r => r.firstLine.StartsWith("Synthetic follow-up")).key));
        }

        [Test]
        public void StartPollsOnItsOwnThread()
        {
            pile.Start(20);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (pile.Latest.reading && DateTime.UtcNow < deadline) System.Threading.Thread.Sleep(10);
            Assert.AreEqual(4, pile.Latest.rows.Count);
        }

        [TestCase(0L, 59000L, "0m")]
        [TestCase(0L, 3540000L, "59m")]
        [TestCase(0L, 3600000L, "1h")]
        [TestCase(0L, 169200000L, "47h")]
        [TestCase(0L, 172800000L, "2d")]
        [TestCase(5000L, 1000L, "0m")]
        public void Age(long since, long now, string expected) => Assert.AreEqual(expected, PileFormat.Age(since, now));

        [Test]
        public void FirstLine()
        {
            Assert.AreEqual("two", PileFormat.FirstLine("\n  \n two \nthree"));
            Assert.AreEqual("aaaa…", PileFormat.FirstLine("aaaaaaaa", 4));
            Assert.AreEqual("", PileFormat.FirstLine(null));
            Assert.AreEqual("", PileFormat.FirstLine(" \n"));
        }
    }
}
