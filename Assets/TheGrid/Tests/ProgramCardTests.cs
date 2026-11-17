using NUnit.Framework;
using UnityEngine;

namespace TheGrid.Core.Tests
{
    public class ProgramCardTests
    {
        [Test]
        public void APictureIsEnlargedInWholePixelsTopRowFirstAndClearPixelsCarryNoColour()
        {
            Color32 a = new Color32(1, 2, 3, 255), b = new Color32(9, 9, 9, 0), c = new Color32(4, 5, 6, 255), d = new Color32(7, 8, 9, 255);
            var got = ProgramCard.Scaled(new[] { a, b, c, d }, 2, 2, 2);   // Unity order: bottom row a b, top row c d
            var clear = new Color32(0, 0, 0, 0);
            CollectionAssert.AreEqual(new[] { c, c, d, d, c, c, d, d, a, a, clear, clear, a, a, clear, clear }, got);
        }
    }
}
