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

        public override uint GetVersion() => 1;

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

        // Where the ground point is in each picture, as tools/art/README.md lists it.
        static Vector2 Pivot(string name)
        {
            if (name.StartsWith("b-")) return new Vector2(.5f, 32f / 160f);
            if (name == "rider-small") return new Vector2(.5f, 9f / 32f);
            if (name.StartsWith("rider-")) return new Vector2(.5f, 18f / 64f);
            if (name == "marker") return new Vector2(.5f, 0f);
            return new Vector2(.5f, .5f);
        }
    }
}
