using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TheGrid.EditorTools
{
    // Every picture under Resources/Art is pixel art made by tools/art/make_sprites.py. This sets how Unity imports it,
    // so nobody sets import options by hand: a sprite, one unit per pixel, crisp, uncompressed, readable by tests.
    public sealed class ArtImport : AssetPostprocessor
    {
        const string Folder = "Assets/TheGrid/Resources/Art/";
        const string ManifestPath = Folder + "sprites.json";

        public override uint GetVersion() => 4;

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 1;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.isReadable = true;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = Pivot(Path.GetFileNameWithoutExtension(assetPath));
            importer.SetTextureSettings(settings);
        }

        [Serializable] sealed class Manifest { public List<Entry> sprites = new List<Entry>(); }
        [Serializable] sealed class Entry { public string name = ""; public int width, height, groundX, groundY; }

        // Where the ground point is in each picture: sprites.json, written by the script beside the pictures, says.
        Vector2 Pivot(string name)
        {
            var centre = new Vector2(.5f, .5f);
            if (name == "palette" || name == "palette-1") return centre;
            context.DependsOnSourceAsset(ManifestPath);   // a changed manifest imports the pictures again
            Entry e = null;
            if (File.Exists(ManifestPath)) e = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath)).sprites.Find(s => s.name == name);
            if (e == null)
            {
                Debug.LogWarning("[art] " + name + " is not in " + ManifestPath + "; run tools/art/make_sprites.py. Imported with its ground point at the centre.");
                return centre;
            }
            return new Vector2((float)e.groundX / e.width, (float)e.groundY / e.height);
        }
    }
}
