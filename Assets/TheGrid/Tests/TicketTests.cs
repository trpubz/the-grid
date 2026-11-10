using NUnit.Framework;

namespace TheGrid.Core.Tests
{
    public class TicketTests
    {
        [TestCase("someone/abc-848-custom-range", "ABC-848")]
        [TestCase("feature/grid-4-slice-1-local-roster", "GRID-4")]
        [TestCase("main", "")]
        [TestCase("", "")]
        [TestCase(null, "")]
        public void FromBranch(string branch, string expected) => Assert.AreEqual(expected, Ticket.FromBranch(branch));
    }
}
