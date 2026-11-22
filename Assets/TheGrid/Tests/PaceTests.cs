using System.IO;
using System.Linq;
using NUnit.Framework;

namespace TheGrid.Core.Tests
{
    public class PaceTests
    {
        [TestCase(60, 10, 6)]
        [TestCase(120, 10, 12)]
        [TestCase(60, 60, 1)]
        [TestCase(120, 60, 2)]
        [TestCase(59.94, 10, 6)]
        [TestCase(50, 60, 1)]      // a screen slower than what is asked: every frame, never none
        [TestCase(0, 10, 6)]       // a screen that does not say how fast it is counts as 60
        [TestCase(-1, 10, 6)]
        [TestCase(double.NaN, 10, 6)]                 // a refresh rate given as 0 over 0
        [TestCase(double.PositiveInfinity, 10, 6)]    // or as something over 0: still a frame every so often, never none
        [TestCase(100000, 10, 6)]                     // or a number no screen has
        [TestCase(60, 0, 1)]       // nothing asked for: every frame
        public void AFrameIsDrawnEverySoManyScreenRefreshes(double refresh, int wanted, int every) =>
            Assert.AreEqual(every, Pace.Interval(refresh, wanted));

        // Presenting frames without display sync made macOS's window server leak memory until a restart (GRID-51).
        // Nothing a test can run shows that, so the line itself is watched for.
        [Test]
        public void NothingTurnsDisplaySyncOff()
        {
            var root = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "TheGrid");
            var guilty = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith("PaceTests.cs"))
                .Where(f => File.ReadAllLines(f).Any(l => l.Contains("vSyncCount") && !l.Contains("vSyncCount = 1") || l.Contains("targetFrameRate =")))
                .Select(Path.GetFileName).ToArray();
            CollectionAssert.IsEmpty(guilty, "display sync must stay on, and frames are paced with Pace.Interval, not targetFrameRate");
        }
    }
}
