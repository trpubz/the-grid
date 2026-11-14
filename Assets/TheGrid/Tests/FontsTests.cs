using NUnit.Framework;

namespace TheGrid.Core.Tests
{
    public class FontsTests
    {
        static readonly string[] Installed = { "Arial", "Menlo", "MonaspiceKr NF Bold", "MonaspiceKr NF", "Hack Nerd Font Mono", "INFO Sans", "SomeNF" };

        // With nothing asked for: the first family, by name, that says it is a Nerd Font, in full or by the short "NF".
        [Test]
        public void ANerdFontIsPickedWhenNoneIsNamed() => Assert.AreEqual("Hack Nerd Font Mono", Fonts.Pick("", Installed));

        // A family before its own bold and italic cuts.
        [Test]
        public void TheFamilyNotOneOfItsCuts() => Assert.AreEqual("MonaspiceKr NF", Fonts.Pick("", new[] { "MonaspiceKr NF Bold", "Menlo", "MonaspiceKr NF Italic", "MonaspiceKr NF" }));

        // "NF" counts as a word of its own only.
        [Test]
        public void NoNerdFontNoFont() => Assert.AreEqual("", Fonts.Pick("", new[] { "Arial", "Menlo", "INFO Sans", "SomeNF" }));

        // The one named in the settings, as the Mac spells it, whatever kind of font it is.
        [TestCase("Menlo", "Menlo")]
        [TestCase("menlo", "Menlo")]
        [TestCase(" MonaspiceKr NF Bold ", "MonaspiceKr NF Bold")]
        public void TheFontThatWasNamed(string named, string expected) => Assert.AreEqual(expected, Fonts.Pick(named, Installed));

        // Named but not on this Mac: as if none was named.
        [Test]
        public void ANamedFontThatIsNotInstalled() => Assert.AreEqual("Hack Nerd Font Mono", Fonts.Pick("Missing Sans", Installed));

        [Test]
        public void NothingInstalledNothingPicked()
        {
            Assert.AreEqual("", Fonts.Pick(null, null));
            Assert.AreEqual("", Fonts.Pick("Menlo", new string[0]));
        }
    }
}
