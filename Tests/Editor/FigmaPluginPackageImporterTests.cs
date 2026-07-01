using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace FigmaUiImporter.Editor.Tests
{
    public sealed class FigmaPluginPackageImporterTests
    {
        private const string TestFolder = "Assets/FigmaUiImporterPluginPackageTests";
        private const string PngBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=";

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
        public void ImportPackageWritesManifestAndImages()
        {
            string packagePath = WritePackage(CreatePackageJson("reference.png", "layers/3_4.png"));

            try
            {
                FigmaPluginPackageImporter importer = new FigmaPluginPackageImporter();
                string manifestPath = importer.ImportPackage(
                    packagePath,
                    new FigmaImportSettings { OutputFolder = TestFolder },
                    new NullProgress());

                string expectedFolder = "Assets/FigmaUiImporterPluginPackageTests/localFile/1_2";
                Assert.AreEqual(FigmaPathUtility.CombineAssetPath(expectedFolder, "manifest.json"), manifestPath);
                Assert.IsTrue(File.Exists(FigmaPathUtility.ToFullPath(FigmaPathUtility.CombineAssetPath(expectedFolder, "reference.png"))));
                Assert.IsTrue(File.Exists(FigmaPathUtility.ToFullPath(FigmaPathUtility.CombineAssetPath(expectedFolder, "layers", "3_4.png"))));

                FigmaManifestRebuildData data = new FigmaImportManifestService().LoadForRebuild(manifestPath);
                Assert.AreEqual("localFile", data.FileKey);
                Assert.AreEqual("1:2", data.RootNode.Id);
                Assert.AreEqual(1, data.Layers.Count);
            }
            finally
            {
                File.Delete(packagePath);
            }
        }

        [Test]
        public void ImportPackageRejectsUnsafeAssetPath()
        {
            string packagePath = WritePackage(CreatePackageJson("../reference.png", "layers/3_4.png"));

            try
            {
                FigmaPluginPackageImporter importer = new FigmaPluginPackageImporter();
                Assert.Throws<InvalidOperationException>(
                    () => importer.ImportPackage(
                        packagePath,
                        new FigmaImportSettings { OutputFolder = TestFolder },
                        new NullProgress()));
            }
            finally
            {
                File.Delete(packagePath);
            }
        }

        private static string CreatePackageJson(string referencePath, string layerPath)
        {
            return "{"
                + "\"schemaVersion\":1,"
                + "\"exportedAt\":\"2026-07-01T00:00:00Z\","
                + "\"fileKey\":\"localFile\","
                + "\"imageScale\":1,"
                + "\"manifest\":{"
                + "\"fileKey\":\"\","
                + "\"imageScale\":1,"
                + "\"selectedNodeId\":\"1:2\","
                + "\"selectedNodeName\":\"Root\","
                + "\"selectedNodeType\":\"FRAME\","
                + "\"importedAt\":\"2026-07-01T00:00:00Z\","
                + "\"rootBounds\":{\"x\":0,\"y\":0,\"width\":100,\"height\":80},"
                + "\"referenceImage\":\"reference.png\","
                + "\"layers\":[{"
                + "\"nodeId\":\"3:4\","
                + "\"nodeName\":\"Icon\","
                + "\"type\":\"RECTANGLE\","
                + "\"image\":\"layers/3_4.png\","
                + "\"bounds\":{\"x\":10,\"y\":12,\"width\":20,\"height\":24},"
                + "\"unity\":{\"anchoredPosition\":{\"x\":-30,\"y\":16},\"sizeDelta\":{\"x\":20,\"y\":24}},"
                + "\"siblingIndex\":0,"
                + "\"unityObjectPath\":\"\""
                + "}]"
                + "},"
                + "\"assets\":["
                + "{\"path\":\"" + referencePath + "\",\"mimeType\":\"image/png\",\"dataBase64\":\"" + PngBase64 + "\"},"
                + "{\"path\":\"" + layerPath + "\",\"mimeType\":\"image/png\",\"dataBase64\":\"" + PngBase64 + "\"}"
                + "]"
                + "}";
        }

        private static string WritePackage(string json)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "FigmaUiImporterTests_" + Guid.NewGuid().ToString("N") + ".figma2unity.json");
            File.WriteAllText(path, json);
            return path;
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

        private sealed class NullProgress : IProgress<FigmaImportProgress>
        {
            public void Report(FigmaImportProgress value)
            {
            }
        }
    }
}
