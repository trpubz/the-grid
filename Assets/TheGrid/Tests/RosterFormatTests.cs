using NUnit.Framework;

namespace TheGrid.Core.Tests
{
    public class RosterFormatTests
    {
        [TestCase(0L, 59000L, "0m")]
        [TestCase(0L, 240000L, "4m")]
        [TestCase(0L, 3600000L, "1h 0m")]
        [TestCase(0L, 4320000L, "1h 12m")]
        [TestCase(5000L, 1000L, "0m")]
        public void Elapsed(long since, long now, string expected) => Assert.AreEqual(expected, RosterFormat.Elapsed(since, now));
    }
}
