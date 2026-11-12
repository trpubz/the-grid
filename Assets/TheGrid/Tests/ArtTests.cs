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
        [Serializable] sealed class Entry { public string name = ""; public int width, height, groundX, groundY; }

        static readonly string[] Palette2 =
        {
            "#0b0e22", "#05070f", "#0c1024", "#141732", "#1b1f41", "#242950", "#2e3463", "#3a4178", "#474e8c", "#525a9b", "#5f67aa", "#6e76b7",
            "#2c3360", "#353d70", "#3f4780", "#101a34", "#35f2ff", "#c9fbff", "#1a8f9c", "#3f5a96", "#4a7fb4", "#62b4d6",
            "#a070ff", "#d9c8ff", "#4b4696", "#6657b8", "#ffb23e", "#b8741f", "#6e4a1c", "#ff4d4d", "#ffffff",
            "#8791c4", "#e4ecff", "#ff7a1e", "#ffc27a", "#a8501a",   // what a white and an orange bot's light becomes
            "#a9c8ff", "#4d8dff", "#2a55b0", "#2f4a96", "#3a62c0", "#5b8be6",   // a building lit blue: line, dim line, window, glow far to near
            "#b6ffe6", "#3ef2b0", "#1a9c78", "#2f6f7a", "#3a9c8c", "#5cd6b0",   // and mint
        };

        // A bot's light is drawn in three marker colours, which the game swaps for its colourway.
        static readonly Color32 Light = new Color32(255, 255, 255, 255), Highlight = new Color32(0xc9, 0xfb, 0xff, 255), Dim = new Color32(0x1a, 0x8f, 0x9c, 255);
        static readonly Color32 Outline = new Color32(0x05, 0x07, 0x0f, 255);

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
            var onDisk = Directory.GetFiles(folder, "*.png").Select(Path.GetFileNameWithoutExtension).Where(n => n != "palette").OrderBy(n => n, StringComparer.Ordinal).ToList();
            var listed = Read().sprites.Select(s => s.name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            CollectionAssert.AreEqual(onDisk, listed);
            Assert.AreEqual(listed.Count, listed.Distinct().Count(), "a name is listed twice");
        }

        [Test]
        public void EveryPictureTheGameCanAskForIsInTheManifest()
        {
            // The game builds some names from parts (a pose and a facing). A sprite renamed in the art script would pass
            // every other test here and only be missed when the world came to draw it.
            var listed = new HashSet<string>(Read().sprites.Select(s => s.name));
            var asked = ArtNames.All().ToList();
            Assert.Greater(asked.Count, 50);
            Assert.AreEqual(asked.Count, asked.Distinct().Count(), "a name is asked for twice in the list");
            foreach (var name in asked) Assert.IsTrue(listed.Contains(name), "the game asks for '" + name + "', which is not in the manifest");
            // one of each family, spelled out, so a typo in ArtNames itself cannot hide
            foreach (var name in new[] { "b-server-tower", "fountain", "bot-walk-b-nw", "bot-signal-se", "bot-off-sw", "bot-small", "bike-ne", "badge-approve", "pulse-2", "tile-crossing-a", "line-b", "block-c" })
                Assert.Contains(name, asked);
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

            var palette = Colours("palette");
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

        [TestCaseSource(nameof(Names))]
        public void NothingIsCutOffAtTheEdgeOfItsPicture(string name)
        {
            if (IsTile(name)) Assert.Pass("ground tiles reach their edges by design");
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
            Assert.AreEqual(48, t.width);
            Assert.AreEqual(1, t.height);
            var got = t.GetPixels32().Select(c => "#" + c.r.ToString("x2") + c.g.ToString("x2") + c.b.ToString("x2")).ToArray();
            CollectionAssert.AreEqual(Palette2, got);
            Assert.IsTrue(t.GetPixels32().All(c => c.a == 255));
        }

        static Color32 Hex(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        [Test]
        public void EveryLightABuildingCanCarryIsInThePalette()
        {
            var palette = Colours("palette");
            for (int light = 0; light < Lights.Count; light++)
            {
                Assert.AreEqual(Lights.Parts, Lights.Family(light).Length);
                foreach (int rgb in Lights.Family(light)) Assert.IsTrue(palette.Contains(Hex(rgb)), "light " + light + " has #" + rgb.ToString("x6") + ", which is not in the palette");
            }
            Assert.AreEqual(Lights.Parts, Lights.Drawn.Distinct().Count(), "the colours a building is drawn in must all differ, or they cannot be swapped apart");
        }

        [Test]
        public void EveryBuildingIsDrawnInTheFirstLightSoTheGameCanSwapIt()
        {
            // Violet is the review hall's no longer: a building's light is its sector's choice. Only the fountain is violet as drawn.
            var violet = new[] { Hex(0xa070ff), Hex(0xd9c8ff), Hex(0x4b4696), Hex(0x6657b8) };
            var drawn = Lights.Drawn.Select(Hex).ToList();
            foreach (var name in ArtNames.Structures.Where(n => n.StartsWith("b-")))
            {
                var px = Load(name).texture.GetPixels32();
                Assert.AreEqual(0, px.Count(c => violet.Contains(c)), name + " has violet in it");
                Assert.Greater(px.Count(c => c.Equals(drawn[0])), 20, name + " has no bright line");
                Assert.Greater(px.Count(c => c.Equals(drawn[3])), 20, name + " has no glow at its foot");
            }
        }

        [Test]
        public void ABuildingIsNoTallerThanTheLayoutTakesItToBe()
        {
            // Layout.Wall is what the "no bot is hidden" test reckons with. A building redrawn taller must not slip past it.
            // A picture's body ends where its width has narrowed to a mast or a stack: under 24 pixels across.
            foreach (Structure s in Enum.GetValues(typeof(Structure)))
            {
                string name = ArtNames.Structures[(int)s];
                var t = Load(name).texture;
                var f = Layout.Of(0).Footprint(s);
                int ground = Find(name).groundY, back = (f.width + f.height) * Iso.TileH / 4;   // the back corner stands this far above the ground point
                int body = 0;
                for (int y = ground; y < t.height; y++)
                    if (Enumerable.Range(0, t.width).Count(x => t.GetPixel(x, y).a > 0) >= 24) body = y - ground;
                Assert.LessOrEqual(body - back, Layout.Wall(s) + 4, name + " stands " + (body - back) + " above its back corner");
            }
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

        // How many separate runs of solid pixels a row of a picture has.
        static int Runs(Texture2D t, int y)
        {
            int runs = 0;
            bool inside = false;
            for (int x = 0; x < t.width; x++)
            {
                bool solid = t.GetPixel(x, y).a > 0;
                if (solid && !inside) runs++;
                inside = solid;
            }
            return runs;
        }

        [Test]
        public void ABotIsStockyUnderALargeHelmet()
        {
            var t = Load("bot-stand-se").texture;
            // measured from where it stands, not from the bottom of its shadow
            int bottom = Find("bot-stand-se").groundY, top = Enumerable.Range(0, t.height).Last(y => Runs(t, y) > 0);
            int tall = top - bottom + 1;
            Assert.That(tall, Is.InRange(28, 38), "a bot is about 32 pixels tall");
            int Wide(int y) => Enumerable.Range(0, t.width).Count(x => t.GetPixel(x, y).a > 0);
            int helmet = Enumerable.Range(bottom + tall * 6 / 10, tall * 4 / 10).Max(Wide);
            Assert.GreaterOrEqual(helmet, 16, "the helmet is oversized: half as wide as the bot is tall");
        }

        static int Count(Texture2D t, Color32 c) => t.GetPixels32().Count(p => p.Equals(c));

        [Test]
        public void ABotsLightIsThreeMarkersAndABotThatIsOffShowsNone()
        {
            var bots = Read().sprites.Select(s => s.name).Where(n => n.StartsWith("bot-")).ToList();
            Assert.AreEqual(7 * 4 + 1, bots.Count, "seven poses in four facings, and the small one");
            foreach (var name in bots)
            {
                var t = Load(name).texture;
                if (name.StartsWith("bot-off-")) Assert.AreEqual(0, Count(t, Light) + Count(t, Highlight) + Count(t, Dim), name);
                else Assert.GreaterOrEqual(Count(t, Light), name == "bot-small" ? 4 : 12, name);
            }
            // seen from the front, the visor has its highlight
            Assert.GreaterOrEqual(Count(Load("bot-stand-se").texture, Highlight), 4);
            Assert.GreaterOrEqual(Count(Load("bot-stand-sw").texture, Highlight), 4);
        }

        // Rows of the body, between the boots and the helmet, in which light surrounds something that is not light: a ring.
        static int RingRows(string name)
        {
            var t = Load(name).texture;
            int g = Find(name).groundY, rows = 0;
            for (int y = g + 6; y <= g + 19; y++)
            {
                int state = 0;   // 0 nothing yet, 1 in light, 2 in the hole after light, 3 light again
                for (int x = 0; x < t.width && state < 3; x++)
                {
                    bool lit = ((Color32)t.GetPixel(x, y)).Equals(Light), solid = t.GetPixel(x, y).a > 0;
                    if (lit) state = state == 2 ? 3 : 1;
                    else if (state == 1 && solid) state = 2;
                    else if (!solid) state = 0;
                }
                if (state == 3) rows++;
            }
            return rows;
        }

        [Test]
        public void ABotCarriesARingOfLightOnItsBackAndHoldsItToInspect()
        {
            Assert.GreaterOrEqual(RingRows("bot-stand-ne"), 2, "seen from behind, the ring is on its back");
            Assert.GreaterOrEqual(RingRows("bot-stand-nw"), 2, "seen from behind, the ring is on its back");
            Assert.AreEqual(0, RingRows("bot-stand-se"), "seen from the front there is no ring");
            Assert.GreaterOrEqual(RingRows("bot-inspect-se"), 1, "the bot that inspects holds its disc in front of it");
        }

        // The pixels of a picture that touch nothing: its outline.
        static IEnumerable<Color32> Silhouette(Texture2D t)
        {
            for (int y = 0; y < t.height; y++)
                for (int x = 0; x < t.width; x++)
                {
                    if (t.GetPixel(x, y).a == 0) continue;
                    bool open = x == 0 || y == 0 || x == t.width - 1 || y == t.height - 1
                        || t.GetPixel(x - 1, y).a == 0 || t.GetPixel(x + 1, y).a == 0 || t.GetPixel(x, y - 1).a == 0 || t.GetPixel(x, y + 1).a == 0;
                    if (open) yield return t.GetPixel(x, y);
                }
        }

        [Test]
        public void WhatStandsIsOutlinedAndTheGroundIsNot()
        {
            foreach (var name in Read().sprites.Select(s => s.name))
            {
                var edge = Silhouette(Load(name).texture).ToList();
                int dark = edge.Count(c => c.Equals(Outline));
                if (IsTile(name)) Assert.AreEqual(0, dark, name + ": ground is flat, edge to edge");
                else if (name.StartsWith("b-")) Assert.GreaterOrEqual(dark, 80, name);
                else if (name.StartsWith("bot-") && name != "bot-small") Assert.GreaterOrEqual(dark * 2, edge.Count, name + ": at least half its edge");
            }
        }

        [Test]
        public void LargeFacesArePanelledRoofsIncluded()
        {
            // Across the middle of the assembly bay's roof, seams cross the row again and again.
            var t = Load("b-assembly-bay").texture;
            int y = Find("b-assembly-bay").groundY + 60, changes = 0;
            for (int x = 100; x < 188; x++)
                if (!((Color32)t.GetPixel(x, y)).Equals((Color32)t.GetPixel(x + 1, y))) changes++;
            Assert.GreaterOrEqual(changes, 8);
        }

        [Test]
        public void WhatStandsInTheWorkshopCastsAShadowOnItsFloor()
        {
            // The floor is the lightest paving; where a crate or the terminal shades it, it is one tone down.
            var shade = new Color32(0x35, 0x3d, 0x70, 255);
            Assert.GreaterOrEqual(Count(Load("b-test-workshop").texture, shade), 20);
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
