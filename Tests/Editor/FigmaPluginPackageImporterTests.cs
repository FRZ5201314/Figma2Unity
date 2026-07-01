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
            string packagePath = WritePackage(CreatePackageJson());

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
        public void ImportPackagePreservesTextLayerManifestData()
        {
            string packagePath = WritePackage(CreateTextPackageJson());

            try
            {
                FigmaPluginPackageImporter importer = new FigmaPluginPackageImporter();
                string manifestPath = importer.ImportPackage(
                    packagePath,
                    new FigmaImportSettings { OutputFolder = TestFolder },
                    new NullProgress());

                FigmaManifestRebuildData data = new FigmaImportManifestService().LoadForRebuild(manifestPath);
                Assert.AreEqual(1, data.Layers.Count);
                Assert.AreEqual(ImportedLayerKind.Image, data.Layers[0].Kind);
                Assert.AreEqual("Hello", data.Layers[0].TextStyle.Characters);
                Assert.AreEqual("Inter", data.Layers[0].TextStyle.FontFamily);
                Assert.AreEqual("Regular", data.Layers[0].TextStyle.FontStyleName);
                Assert.AreEqual(20f, data.Layers[0].TextStyle.FontSize);
                Assert.AreEqual(0.25f, data.Layers[0].TextStyle.Color.r, 0.001f);
            }
            finally
            {
                File.Delete(packagePath);
            }
        }

        [Test]
        public void ReadPackageInfoReturnsImportSummary()
        {
            string packagePath = WritePackage(CreatePackageJson());

            try
            {
                FigmaPluginPackageInfo info = new FigmaPluginPackageImporter().ReadPackageInfo(packagePath);

                Assert.AreEqual("localFile", info.FileKey);
                Assert.AreEqual("1:2", info.SelectedNodeId);
                Assert.AreEqual("Root", info.SelectedNodeName);
                Assert.AreEqual("FRAME", info.SelectedNodeType);
                Assert.AreEqual(1f, info.ImageScale);
                Assert.AreEqual(100f, info.RootWidth);
                Assert.AreEqual(80f, info.RootHeight);
                Assert.AreEqual(1, info.LayerCount);
                Assert.AreEqual(2, info.AssetCount);
                Assert.IsTrue(info.HasReferenceImage);
                Assert.Greater(info.PackageSizeBytes, 0);
            }
            finally
            {
                File.Delete(packagePath);
            }
        }

        [Test]
        public void ImportPackageRejectsUnsafeAssetPath()
        {
            string packagePath = WritePackage(CreatePackageJson(referenceAssetPath: "../reference.png"));

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

        [Test]
        public void ImportPackageRejectsMissingReferencedAsset()
        {
            string packagePath = WritePackage(CreatePackageJson(referenceManifestPath: "missing-reference.png"));

            try
            {
                FigmaPluginPackageImporter importer = new FigmaPluginPackageImporter();
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                    () => importer.ImportPackage(
                        packagePath,
                        new FigmaImportSettings { OutputFolder = TestFolder },
                        new NullProgress()));
                StringAssert.Contains("参考图资源不存在", exception.Message);
            }
            finally
            {
                File.Delete(packagePath);
            }
        }

        [Test]
        public void ImportPackageReportsCorruptBase64()
        {
            string packagePath = WritePackage(CreatePackageJson(referenceBase64: "not-base64"));

            try
            {
                FigmaPluginPackageImporter importer = new FigmaPluginPackageImporter();
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                    () => importer.ImportPackage(
                        packagePath,
                        new FigmaImportSettings { OutputFolder = TestFolder },
                        new NullProgress()));
                StringAssert.Contains("base64 数据损坏", exception.Message);
            }
            finally
            {
                File.Delete(packagePath);
            }
        }

        private static string CreatePackageJson(
            string referenceManifestPath = "reference.png",
            string referenceAssetPath = "reference.png",
            string layerManifestPath = "layers/3_4.png",
            string layerAssetPath = "layers/3_4.png",
            string referenceBase64 = PngBase64,
            string layerBase64 = PngBase64)
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
                + "\"referenceImage\":\"" + referenceManifestPath + "\","
                + "\"layers\":[{"
                + "\"nodeId\":\"3:4\","
                + "\"nodeName\":\"Icon\","
                + "\"type\":\"RECTANGLE\","
                + "\"image\":\"" + layerManifestPath + "\","
                + "\"bounds\":{\"x\":10,\"y\":12,\"width\":20,\"height\":24},"
                + "\"unity\":{\"anchoredPosition\":{\"x\":-30,\"y\":16},\"sizeDelta\":{\"x\":20,\"y\":24}},"
                + "\"siblingIndex\":0,"
                + "\"unityObjectPath\":\"\""
                + "}]"
                + "},"
                + "\"assets\":["
                + "{\"path\":\"" + referenceAssetPath + "\",\"mimeType\":\"image/png\",\"dataBase64\":\"" + referenceBase64 + "\"},"
                + "{\"path\":\"" + layerAssetPath + "\",\"mimeType\":\"image/png\",\"dataBase64\":\"" + layerBase64 + "\"}"
                + "]"
                + "}";
        }

        private static string CreateTextPackageJson()
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
                + "\"textImportMode\":\"metadata\","
                + "\"importedAt\":\"2026-07-01T00:00:00Z\","
                + "\"rootBounds\":{\"x\":0,\"y\":0,\"width\":100,\"height\":80},"
                + "\"referenceImage\":\"reference.png\","
                + "\"layers\":[{"
                + "\"nodeId\":\"3:4\","
                + "\"nodeName\":\"Title\","
                + "\"type\":\"TEXT\","
                + "\"kind\":\"image\","
                + "\"image\":\"layers/3_4.png\","
                + "\"text\":{\"characters\":\"Hello\",\"fontFamily\":\"Inter\",\"fontStyleName\":\"Regular\",\"fontPostScriptName\":\"Inter-Regular\",\"fontWeight\":400,\"fontSize\":20,\"lineHeightPx\":24,\"letterSpacing\":0,\"color\":{\"r\":0.25,\"g\":0.5,\"b\":0.75,\"a\":1},\"horizontalAlign\":\"LEFT\",\"verticalAlign\":\"TOP\",\"autoResize\":\"NONE\",\"hasMixedStyles\":false,\"hasUnsupportedVisuals\":false},"
                + "\"bounds\":{\"x\":10,\"y\":12,\"width\":60,\"height\":24},"
                + "\"unity\":{\"anchoredPosition\":{\"x\":-20,\"y\":16},\"sizeDelta\":{\"x\":60,\"y\":24}},"
                + "\"siblingIndex\":0,"
                + "\"unityObjectPath\":\"\""
                + "}]"
                + "},"
                + "\"assets\":["
                + "{\"path\":\"reference.png\",\"mimeType\":\"image/png\",\"dataBase64\":\"" + PngBase64 + "\"},"
                + "{\"path\":\"layers/3_4.png\",\"mimeType\":\"image/png\",\"dataBase64\":\"" + PngBase64 + "\"}"
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
