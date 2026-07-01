using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FigmaUiImporter.Editor.Tests
{
    public sealed class FigmaLayerImagePostprocessorTests
    {
        private const string TestFolder = "Assets/FigmaUiImporterPostprocessorTests";

        [SetUp]
        public void SetUp()
        {
            Cleanup();
        }

        [TearDown]
        public void TearDown()
        {
            Cleanup();
        }

        [Test]
        public void RemoveClearChildPixelsClearsChildImageFromParent()
        {
            string parentPath = FigmaPathUtility.CombineAssetPath(TestFolder, "layers", "parent.png");
            string childPath = FigmaPathUtility.CombineAssetPath(TestFolder, "layers", "child.png");
            WriteTexture(parentPath, CreateParentTexture());
            WriteTexture(childPath, CreateSolidTexture(2, 2, new Color32(0, 0, 255, 255)));

            ImportedLayerInfo parent = new ImportedLayerInfo();
            parent.NodeId = "parent";
            parent.AssetPath = parentPath;
            parent.FigmaBounds = new Rect(0f, 0f, 4f, 4f);
            parent.SiblingIndex = 0;
            parent.ClearChildNodeIds.Add("child");

            ImportedLayerInfo child = new ImportedLayerInfo();
            child.NodeId = "child";
            child.AssetPath = childPath;
            child.FigmaBounds = new Rect(1f, 1f, 2f, 2f);
            child.SiblingIndex = 1;

            new FigmaLayerImagePostprocessor().RemoveClearChildPixels(
                new List<ImportedLayerInfo> { parent, child });

            Texture2D result = LoadTexture(parentPath);
            try
            {
                Color32[] pixels = result.GetPixels32();
                Assert.AreEqual(0, pixels[1 * result.width + 1].a);
                Assert.AreEqual(0, pixels[2 * result.width + 2].a);
                Assert.AreEqual(255, pixels[0].a);
                Assert.AreEqual(255, pixels[0].r);
                Assert.AreEqual(0, pixels[0].g);
                Assert.AreEqual(0, pixels[0].b);
            }
            finally
            {
                Object.DestroyImmediate(result);
            }
        }

        private static Texture2D CreateParentTexture()
        {
            Texture2D texture = CreateSolidTexture(4, 4, new Color32(255, 0, 0, 255));
            Color32[] pixels = texture.GetPixels32();
            for (int y = 1; y <= 2; y++)
            {
                for (int x = 1; x <= 2; x++)
                {
                    pixels[y * texture.width + x] = new Color32(0, 0, 255, 255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static Texture2D CreateSolidTexture(int width, int height, Color32 color)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static void WriteTexture(string assetPath, Texture2D texture)
        {
            string fullPath = FigmaPathUtility.ToFullPath(assetPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(fullPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        private static Texture2D LoadTexture(string assetPath)
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            ImageConversion.LoadImage(texture, File.ReadAllBytes(FigmaPathUtility.ToFullPath(assetPath)), false);
            return texture;
        }

        private static void Cleanup()
        {
            AssetDatabase.DeleteAsset(TestFolder);
            string fullPath = FigmaPathUtility.ToFullPath(TestFolder);
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, true);
            }

            AssetDatabase.Refresh();
        }
    }
}
