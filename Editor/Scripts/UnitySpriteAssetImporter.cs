using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FigmaUiImporter.Editor
{
    internal sealed class UnitySpriteAssetImporter
    {
        public void ImportSprites(IEnumerable<string> assetPaths, TextureImporterCompressionMode compressionMode)
        {
            AssetDatabase.Refresh();

            foreach (string assetPath in assetPaths)
            {
                if (string.IsNullOrEmpty(assetPath))
                {
                    continue;
                }

                TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null)
                {
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                    importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                }

                if (importer == null)
                {
                    Debug.LogWarning("无法配置图片导入设置：" + assetPath);
                    continue;
                }

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.sRGBTexture = true;
                importer.textureCompression = compressionMode == TextureImporterCompressionMode.None
                    ? TextureImporterCompression.Uncompressed
                    : TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
        }
    }
}
