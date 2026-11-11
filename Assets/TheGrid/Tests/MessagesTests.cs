using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace TheGrid.Core.Tests
{
    public class MessagesTests
    {
        static List<MessageEvent> Events(string file)
        {
            var events = new List<MessageEvent>();
            foreach (var line in File.ReadAllLines(Path.Combine(Directory.GetCurrentDirectory(), "fixtures", "claude-code", "messages", file)))
                Messages.Parse(line, events);
            return events;
        }

        [Test]
        public void SenderTranscript()
        {
            var s = Events("sender.jsonl");
            Assert.AreEqual(5, s.Count(e => e.Kind == MessageEventKind.Sent));
            Assert.AreEqual(4, s.Count(e => e.Kind == MessageEventKind.SendResult)); // a result with neither msg_id nor pin is not an event
            Assert.AreEqual(1, s.Count(e => e.Kind == MessageEventKind.Received));   // the subagent report is not an event

            var s1 = s.First(e => e.Kind == MessageEventKind.Sent);
            Assert.AreEqual("toolu_s1", s1.Ref);
            Assert.AreEqual("beta-01", s1.Peer);
            Assert.AreEqual("/work/alpha", s1.Cwd);
            Assert.AreEqual("Synthetic handoff", s1.Summary);
            Assert.AreEqual("Synthetic message to a peer session.\nSecond line of the handoff.", s1.Body);
            Assert.AreEqual(1767268805000L, s1.At); // 2026-01-01T12:00:05Z
            Assert.IsFalse(s1.OwnSubagent);

            var r1 = s.First(e => e.Kind == MessageEventKind.SendResult);
            Assert.AreEqual("toolu_s1", r1.Ref);
            Assert.AreEqual("m-1001", r1.MsgId);
            Assert.IsTrue(s.Single(e => e.Kind == MessageEventKind.Sent && e.Ref == "toolu_s3").OwnSubagent);
            Assert.IsTrue(s.Single(e => e.Kind == MessageEventKind.SendResult && e.Ref == "toolu_s4").OwnSubagent);
            Assert.IsFalse(s.Any(e => e.Kind == MessageEventKind.SendResult && e.Ref == "toolu_s5"));

            var name = s.Single(e => e.Kind == MessageEventKind.SessionName);
            Assert.AreEqual("alpha-02", name.Name);
            Assert.IsFalse(name.IsTitle);

            var reply = s.Single(e => e.Kind == MessageEventKind.Received);
            Assert.AreEqual("s-u6", reply.Ref);
            Assert.AreEqual("m-2001", reply.MsgId);
            Assert.AreEqual("beta-01", reply.PeerName);
        }

        [Test]
        public void RecipientTranscriptHasBothReceivedShapes()
        {
            var r = Events("recipient.jsonl").Where(e => e.Kind == MessageEventKind.Received).ToList();
            CollectionAssert.AreEqual(new[] { "m-1001", "m-1002" }, r.Select(e => e.MsgId).ToList()); // a user record, then an attachment
            Assert.AreEqual("r-a1", r[1].Ref);
            Assert.AreEqual("alpha-02", r[1].PeerName);
            Assert.AreEqual("uds:/tmp/demo-socks/1011.sock", r[1].Peer);
            Assert.AreEqual("/work/beta", r[1].Cwd);
            Assert.AreEqual("Synthetic follow-up sent while the peer was busy.", r[1].Body);
            Assert.AreEqual(1767268862000L, r[1].At);
        }

        [Test]
        public void DesktopMessageIsUnwrapped()
        {
            var all = Events("desktop.jsonl");
            var d = all.Single(e => e.Kind == MessageEventKind.Received);
            Assert.AreEqual("", d.MsgId);
            Assert.AreEqual("d-u1", d.Ref);
            Assert.AreEqual("local_0002", d.Peer);
            Assert.AreEqual("Alpha desk", d.PeerName);
            Assert.AreEqual(Events("sender.jsonl").Single(e => e.Kind == MessageEventKind.Sent && e.Ref == "toolu_s5").Body, d.Body);

            var names = all.Where(e => e.Kind == MessageEventKind.SessionName).ToList();
            CollectionAssert.AreEqual(new[] { "Gamma first title", "Gamma review", "Gamma later title" }, names.Select(e => e.Name).ToList());
            CollectionAssert.AreEqual(new[] { true, false, true }, names.Select(e => e.IsTitle).ToList());
        }

        [Test]
        public void LinesThatAreNotMessageRecordsAddNothing()
        {
            var none = new List<MessageEvent>();
            Messages.Parse("{ half", none);
            Messages.Parse("", none);
            Messages.Parse("[1,2]", none);
            Messages.Parse("{\"type\":\"user\",\"message\":{\"content\":\"plain\"}}", none);
            Messages.Parse("{\"type\":\"user\",\"origin\":{\"kind\":\"task-notification\"},\"message\":{\"content\":\"x\"}}", none);
            Messages.Parse("{\"type\":\"attachment\",\"attachment\":{\"type\":\"other\",\"origin\":{\"kind\":\"peer\",\"msg_id\":\"m\"}}}", none);
            Assert.AreEqual(0, none.Count);
        }
    }
}
