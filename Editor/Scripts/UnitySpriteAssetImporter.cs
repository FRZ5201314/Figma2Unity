using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FigmaUiImporter.Editor
{
    internal sealed class UnitySpriteAssetImporter
    {
        private const int MinMaxTextureSize = 2048;
        private const int MaxMaxTextureSize = 8192;

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

                // Unity 默认 maxTextureSize 通常为 2048；高倍率或大 Frame 会被降采样。
                int sourceWidth;
                int sourceHeight;
                if (TryGetPngSize(assetPath, out sourceWidth, out sourceHeight))
                {
                    importer.maxTextureSize = ResolveMaxTextureSize(Mathf.Max(sourceWidth, sourceHeight));
                }

                importer.SaveAndReimport();
            }
        }

        /// <summary>直接读取 PNG IHDR，避免依赖不同 Unity 版本的 TextureImporter API。</summary>
        private static bool TryGetPngSize(string assetPath, out int width, out int height)
        {
            width = 0;
            height = 0;
            string fullPath = FigmaPathUtility.ToFullPath(assetPath);
            if (!File.Exists(fullPath))
            {
                return false;
            }

            try
            {
                using (FileStream stream = File.OpenRead(fullPath))
                {
                    byte[] header = new byte[24];
                    int readTotal = 0;
                    while (readTotal < header.Length)
                    {
                        int read = stream.Read(header, readTotal, header.Length - readTotal);
                        if (read <= 0)
                        {
                            return false;
                        }

                        readTotal += read;
                    }

                    bool isPng = header[0] == 0x89
                        && header[1] == 0x50
                        && header[2] == 0x4E
                        && header[3] == 0x47
                        && header[4] == 0x0D
                        && header[5] == 0x0A
                        && header[6] == 0x1A
                        && header[7] == 0x0A
                        && header[12] == (byte)'I'
                        && header[13] == (byte)'H'
                        && header[14] == (byte)'D'
                        && header[15] == (byte)'R';
                    if (!isPng)
                    {
                        return false;
                    }

                    width = ReadBigEndianInt32(header, 16);
                    height = ReadBigEndianInt32(header, 20);
                    return width > 0 && height > 0;
                }
            }
            catch (Exception)
            {
                // 读取失败时保持 Unity 默认导入设置，不影响资源导入本身。
                return false;
            }
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24)
                | (bytes[offset + 1] << 16)
                | (bytes[offset + 2] << 8)
                | bytes[offset + 3];
        }

        private static int ResolveMaxTextureSize(int sourceDimension)
        {
            int size = MinMaxTextureSize;
            while (size < sourceDimension && size < MaxMaxTextureSize)
            {
                size *= 2;
            }

            return size;
        }
    }
}
