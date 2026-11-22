using System;
using System.IO;
using NUnit.Framework;

namespace TheGrid.Core.Tests
{
    public class NeedsTests
    {
        const string Blocked = "{\"state\":\"blocked\",\"needs\":\"approve the made-up plan\",\"detail\":\"waiting on a made-up answer\"}";
        string root, one, two;   // two config folders under the temp folder

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "grid-tests-" + Guid.NewGuid().ToString("N"));
            one = Path.Combine(root, "one");
            two = Path.Combine(root, "two");
            Directory.CreateDirectory(one);
            Directory.CreateDirectory(two);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(root, true);

        void Job(string dir, string id, string json) { Directory.CreateDirectory(Path.Combine(dir, "jobs", id)); File.WriteAllText(Path.Combine(dir, "jobs", id, "state.json"), json); }

        [Test] public void ANeedIsOneLine() => Assert.AreEqual("merge the pull request", Needs.Clean("  merge\tthe\n pull   request "));
        [Test] public void NothingGivesNothing() => Assert.AreEqual("", Needs.Clean(null));
        [Test] public void ALongNeedIsCutWithAnEllipsis()
        {
            var got = Needs.Clean(new string('a', 5000));
            Assert.AreEqual(300, got.Length);
            Assert.IsTrue(got.EndsWith("…"));
        }
        [Test] public void ANeedIsNeverCutInsideAnEmoji()
        {
            var got = Needs.Clean(new string('a', 298) + "😀bbb");
            Assert.IsFalse(char.IsHighSurrogate(got[got.Length - 2]), "half an emoji is left before the ellipsis");
            Assert.LessOrEqual(got.Length, 300);
        }
        [Test] public void ABlockedJobSaysWhatItNeedsAndWhy()
        {
            Job(one, "ab12cd34", Blocked);
            Assert.IsTrue(Needs.TryOfJob(new[] { one, two }, "ab12cd34", out var needs, out var detail));
            Assert.AreEqual("approve the made-up plan", needs);
            Assert.AreEqual("waiting on a made-up answer", detail);
        }
        [Test] public void TheJobMayLiveInAnotherConfigFolder()
        {
            Job(one, "ab12cd34", "{\"state\":\"blocked\"}");   // listed here, with nothing asked
            Job(two, "ab12cd34", Blocked);
            Assert.IsTrue(Needs.TryOfJob(new[] { one, two }, "ab12cd34", out var needs, out _));
            Assert.AreEqual("approve the made-up plan", needs);
        }
        [TestCase("")] [TestCase("not json")] [TestCase("{\"needs\": 5}")] [TestCase("{\"needs\": {\"a\": 1}}")] [TestCase("{\"needs\": \"\"}")]
        public void AFileThatSaysNothingUsableIsNoNeed(string json)
        {
            Job(one, "ab12cd34", json);
            Assert.IsFalse(Needs.TryOfJob(new[] { one }, "ab12cd34", out var needs, out var detail));
            Assert.AreEqual("", needs); Assert.AreEqual("", detail);
        }
        [Test] public void NoFileIsNoNeed() => Assert.IsFalse(Needs.TryOfJob(new[] { one, two }, "ab12cd34", out _, out _));
        [Test] public void AFileOverAMegabyteIsNotRead()
        {
            Job(one, "ab12cd34", "{\"needs\":\"x\",\"pad\":\"" + new string('p', 1 << 20) + "\"}");
            Assert.IsFalse(Needs.TryOfJob(new[] { one }, "ab12cd34", out _, out _));
        }
        [TestCase("../outside")] [TestCase("a/b")] [TestCase("a\\b")] [TestCase("..")] [TestCase("")]
        public void AnIdThatIsNotAPlainNameReadsNothing(string id)
        {
            Directory.CreateDirectory(Path.Combine(one, "outside"));
            File.WriteAllText(Path.Combine(one, "outside", "state.json"), Blocked);   // where "../outside" would land
            Assert.IsFalse(Needs.TryOfJob(new[] { one }, id, out _, out _));
        }
    }
}
