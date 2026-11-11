using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TheGrid.Core.Tests
{
    // The sprite set made by tools/art/make_sprites.py, as Unity imports it. What each sprite should be is read from the
    // manifest the script writes beside the pictures, so adding a sprite needs no change here.
    public class ArtTests
    {
        [Serializable] sealed class Manifest { public List<Entry> sprites = new List<Entry>(); }
        [Serializable] sealed class Entry { public string name = ""; public int width, height, groundX, groundY, palette; }

        static readonly string[] Palette2 =
        {
            "#0b0e22", "#05070f", "#0c1024", "#141732", "#1b1f41", "#242950", "#2e3463", "#3a4178", "#474e8c", "#525a9b", "#5f67aa", "#6e76b7",
            "#2c3360", "#353d70", "#3f4780", "#101a34", "#35f2ff", "#c9fbff", "#1a8f9c", "#3f5a96", "#4a7fb4", "#62b4d6",
            "#a070ff", "#d9c8ff", "#4b4696", "#6657b8", "#ffb23e", "#b8741f", "#6e4a1c", "#ff4d4d", "#ffffff",
        };

        static Manifest Read()
        {
            var text = Resources.Load<TextAsset>("Art/sprites");
            Assert.IsNotNull(text, "Art/sprites.json is missing: run tools/art/make_sprites.py");
            return JsonUtility.FromJson<Manifest>(text.text);
        }

        static IEnumerable<string> Names()
        {
            var text = Resources.Load<TextAsset>("Art/sprites");
            return text == null ? new[] { "(no manifest)" } : JsonUtility.FromJson<Manifest>(text.text).sprites.Select(s => s.name);
        }

        static Entry Find(string name)
        {
            var e = Read().sprites.Find(s => s.name == name);
            Assert.IsNotNull(e, name + " is not in the manifest");
            return e;
        }

        static Sprite Load(string name)
        {
            var s = Resources.Load<Sprite>("Art/" + name);
            Assert.IsNotNull(s, name + " is missing or was not imported as a sprite");
            return s;
        }

        static HashSet<Color32> Colours(string palette) => new HashSet<Color32>(Load(palette).texture.GetPixels32());

        static bool IsTile(string name) => name.StartsWith("tile-") || name.StartsWith("line-");

        [Test]
        public void EveryPictureInTheFolderIsInTheManifestAndTheOtherWayRound()
        {
            var folder = Path.Combine(Application.dataPath, "TheGrid", "Resources", "Art");
            var onDisk = Directory.GetFiles(folder, "*.png").Select(Path.GetFileNameWithoutExtension).Where(n => n != "palette" && n != "palette-1").OrderBy(n => n, StringComparer.Ordinal).ToList();
            var listed = Read().sprites.Select(s => s.name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            CollectionAssert.AreEqual(onDisk, listed);
            Assert.AreEqual(listed.Count, listed.Distinct().Count(), "a name is listed twice");
        }

        [TestCaseSource(nameof(Names))]
        public void SpriteIsAsTheManifestSays(string name)
        {
            var e = Find(name);
            var s = Load(name);
            Assert.AreEqual(e.width, (int)s.rect.width);
            Assert.AreEqual(e.height, (int)s.rect.height);
            Assert.AreEqual(e.groundX, s.pivot.x, .01f);
            Assert.AreEqual(e.groundY, s.pivot.y, .01f);
            Assert.AreEqual(FilterMode.Point, s.texture.filterMode);
            Assert.AreEqual(1f, s.pixelsPerUnit);
            Assert.AreEqual(1, s.texture.mipmapCount);

            var palette = Colours(e.palette == 1 ? "palette-1" : "palette");
            int solid = 0;
            foreach (var c in s.texture.GetPixels32())
            {
                if (c.a == 0) continue;
                Assert.AreEqual(255, c.a, name + " has a half-transparent pixel");
                Assert.IsTrue(palette.Contains(c), name + " has a colour outside its palette: " + c);
                solid++;
            }
            Assert.Greater(solid, 20, name + " is nearly empty");
        }

        [TestCaseSource(nameof(Names))]
        public void NothingIsCutOffAtTheEdgeOfItsPicture(string name)
        {
            var e = Find(name);
            if (e.palette == 1 || IsTile(name)) Assert.Pass("the first sprite set and ground tiles reach their edges by design");
            var t = Load(name).texture;
            for (int x = 0; x < t.width; x++)
                Assert.IsTrue(t.GetPixel(x, 0).a == 0 && t.GetPixel(x, t.height - 1).a == 0, name + " touches its top or bottom edge at column " + x);
            for (int y = 0; y < t.height; y++)
                Assert.IsTrue(t.GetPixel(0, y).a == 0 && t.GetPixel(t.width - 1, y).a == 0, name + " touches its left or right edge at row " + y);
        }

        [Test]
        public void ThePaletteIsTheOneTheArtDirectionPlanLists()
        {
            var t = Load("palette").texture;
            Assert.AreEqual(31, t.width);
            Assert.AreEqual(1, t.height);
            var got = t.GetPixels32().Select(c => "#" + c.r.ToString("x2") + c.g.ToString("x2") + c.b.ToString("x2")).ToArray();
            CollectionAssert.AreEqual(Palette2, got);
            Assert.IsTrue(t.GetPixels32().All(c => c.a == 255));
        }

        [Test]
        public void ATileFillsItsDiamondAndNothingElse()
        {
            foreach (var name in Read().sprites.Select(s => s.name).Where(n => n.StartsWith("tile-")))
            {
                var t = Load(name).texture;
                Assert.AreEqual(0, t.GetPixel(0, 0).a, name);          // corners of the picture are outside the diamond
                Assert.AreEqual(0, t.GetPixel(63, 31).a, name);
                Assert.AreEqual(1f, t.GetPixel(32, 16).a, name);       // the centre is inside
                Assert.AreEqual(1f, t.GetPixel(4, 16).a, name);        // so are the left and right points
                Assert.AreEqual(1f, t.GetPixel(59, 16).a, name);
            }
        }

        [Test]
        public void NoTwoBuildingsFiguresOrFacingsAreTheSamePicture()
        {
            string Key(string name) => string.Join(",", Load(name).texture.GetPixels32().Select(c => c.r * 65536 + c.g * 256 + c.b + c.a));
            var names = Read().sprites.Select(s => s.name).Where(n => n.StartsWith("b-") || n.StartsWith("rider-") || n.StartsWith("bot-") || n.StartsWith("bike-")).ToList();
            Assert.AreEqual(names.Count, names.Select(Key).Distinct().Count());
        }
    }
}
