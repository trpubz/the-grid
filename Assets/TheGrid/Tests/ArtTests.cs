using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TheGrid.Core.Tests
{
    // The sprite set made by tools/art/make_sprites.py, as Unity imports it.
    public class ArtTests
    {
        static readonly string[] Tiles = { "tile-ground", "tile-plot", "tile-kerb", "tile-alley", "tile-road", "tile-road-kerb", "tile-road-alley", "tile-water" };
        static readonly string[] Outlands = { "o-stones", "o-outcrop", "o-spire" };
        static readonly string[] Buildings = { "b-garage", "b-library", "b-lab", "b-review", "b-workshop", "b-booth", "b-gate", "b-yard", "b-lot", "b-depot" };
        static readonly string[] Riders = { "rider-se", "rider-ne", "rider-nw", "rider-sw" };

        static IEnumerable<TestCaseData> All()
        {
            foreach (var n in Tiles) yield return new TestCaseData(n, 64, 32, 32f, 16f);
            foreach (var n in Buildings) yield return new TestCaseData(n, 128, 192, 64f, 32f);
            foreach (var n in Outlands) yield return new TestCaseData(n, 128, 192, 64f, 32f);
            foreach (var n in Riders) yield return new TestCaseData(n, 64, 64, 32f, 18f);
            yield return new TestCaseData("rider-small", 32, 32, 16f, 9f);
            yield return new TestCaseData("marker", 12, 16, 6f, 0f);
        }

        static Sprite Load(string name)
        {
            var s = Resources.Load<Sprite>("Art/" + name);
            Assert.IsNotNull(s, name + " is missing or was not imported as a sprite");
            return s;
        }

        static HashSet<Color32> Palette() => new HashSet<Color32>(Load("palette").texture.GetPixels32());

        [TestCaseSource(nameof(All))]
        public void SpriteIsCrispPixelArtAtTheExpectedSize(string name, int width, int height, float pivotX, float pivotY)
        {
            var s = Load(name);
            Assert.AreEqual(width, (int)s.rect.width);
            Assert.AreEqual(height, (int)s.rect.height);
            Assert.AreEqual(FilterMode.Point, s.texture.filterMode);
            Assert.AreEqual(1f, s.pixelsPerUnit);
            Assert.AreEqual(pivotX, s.pivot.x, .01f);
            Assert.AreEqual(pivotY, s.pivot.y, .01f);
            Assert.AreEqual(1, s.texture.mipmapCount);

            var palette = Palette();
            int solid = 0;
            foreach (var c in s.texture.GetPixels32())
            {
                if (c.a == 0) continue;
                Assert.AreEqual(255, c.a, name + " has a half-transparent pixel");
                Assert.IsTrue(palette.Contains(c), name + " has a colour outside the palette: " + c);
                solid++;
            }
            Assert.Greater(solid, 20, name + " is nearly empty");
        }

        [Test]
        public void ThePaletteHasTwentyFourOpaqueColours()
        {
            var p = Load("palette").texture.GetPixels32();
            Assert.AreEqual(24, p.Length);
            Assert.AreEqual(24, p.Distinct().Count());
            Assert.IsTrue(p.All(c => c.a == 255));
        }

        [Test]
        public void ATileFillsItsDiamondAndNothingElse()
        {
            var t = Load("tile-plot").texture;
            Assert.AreEqual(0, t.GetPixel(0, 0).a);           // corners of the picture are outside the diamond
            Assert.AreEqual(0, t.GetPixel(63, 31).a);
            Assert.AreEqual(1f, t.GetPixel(32, 16).a);        // the centre is inside
            Assert.AreEqual(1f, t.GetPixel(4, 16).a);         // so are the left and right points
            Assert.AreEqual(1f, t.GetPixel(59, 16).a);
        }

        [Test]
        public void BuildingsAndRiderFacingsAreDifferentPictures()
        {
            string Key(string name) => string.Join(",", Load(name).texture.GetPixels32().Select(c => c.r * 65536 + c.g * 256 + c.b + c.a));
            Assert.AreEqual(Buildings.Length, Buildings.Select(Key).Distinct().Count());
            Assert.AreEqual(Riders.Length, Riders.Select(Key).Distinct().Count());
        }
    }
}
