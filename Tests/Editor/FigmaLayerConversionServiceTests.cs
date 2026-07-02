using System.IO;
using FigmaUiImporter;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FigmaUiImporter.Editor.Tests
{
    public sealed class FigmaLayerConversionServiceTests
    {
        private const string TestFolder = "Assets/FigmaUiImporterLayerConversionTests";

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
        public void TryFindManifestPathWalksUpFromLayerPng()
        {
            string manifestPath = FigmaPathUtility.CombineAssetPath(TestFolder, "manifest.json");
            WriteTextAsset(manifestPath, "{\"layers\":[]}");

            string foundPath;
            bool found = FigmaLayerConversionService.TryFindManifestPath(
                FigmaPathUtility.CombineAssetPath(TestFolder, "layers", "3_4.png"),
                out foundPath);

            Assert.IsTrue(found);
            Assert.AreEqual(manifestPath, foundPath);
        }

        [Test]
        public void TryLoadTextStyleUsesManifestNodeId()
        {
            string manifestPath = FigmaPathUtility.CombineAssetPath(TestFolder, "manifest.json");
            WriteTextAsset(
                manifestPath,
                "{"
                + "\"layers\":["
                + "{\"nodeId\":\"5:6\",\"type\":\"TEXT\","
                + "\"text\":{\"characters\":\"Hello\",\"fontFamily\":\"Inter\",\"fontStyleName\":\"Regular\","
                + "\"fontPostScriptName\":\"Inter-Regular\",\"fontWeight\":400,\"fontSize\":18,"
                + "\"color\":{\"r\":1,\"g\":1,\"b\":1,\"a\":1}}}"
                + "]}");

            GameObject gameObject = new GameObject("Title");
            try
            {
                FigmaLayerBinding binding = gameObject.AddComponent<FigmaLayerBinding>();
                binding.SetLayerInfo(
                    "5:6",
                    "Title",
                    "TEXT",
                    FigmaPathUtility.CombineAssetPath(TestFolder, "layers", "5_6.png"),
                    0);

                FigmaTextStyleInfo style;
                string reason;
                bool found = FigmaLayerConversionService.TryLoadTextStyle(binding, out style, out reason);

                Assert.IsTrue(found, reason);
                Assert.AreEqual("Hello", style.Characters);
                Assert.AreEqual("Inter-Regular", style.FontPostScriptName);
                Assert.AreEqual(18f, style.FontSize);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        private static void WriteTextAsset(string assetPath, string content)
        {
            string fullPath = FigmaPathUtility.ToFullPath(assetPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, content);
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
