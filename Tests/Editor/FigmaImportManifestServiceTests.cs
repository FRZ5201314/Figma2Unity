using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FigmaUiImporter.Editor.Tests
{
    public sealed class FigmaImportManifestServiceTests
    {
        private const string TestFolder = "Assets/FigmaUiImporterManifestTests";

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
        public void SaveAndLoadManifestPreservesRootAndLayerData()
        {
            string referencePath = FigmaPathUtility.CombineAssetPath(TestFolder, "reference.png");
            string layerPath = FigmaPathUtility.CombineAssetPath(TestFolder, "layers", "icon.png");
            WriteEmptyAsset(referencePath);
            WriteEmptyAsset(layerPath);

            FigmaNodeInfo root = new FigmaNodeInfo();
            root.Id = "1:2";
            root.Name = "Root";
            root.Type = "FRAME";
            root.AbsoluteBounds = new Rect(10f, 20f, 300f, 200f);
            root.HasAbsoluteBounds = true;

            ImportedLayerInfo layer = new ImportedLayerInfo();
            layer.NodeId = "3:4";
            layer.NodeName = "Icon";
            layer.NodeType = "RECTANGLE";
            layer.AssetPath = layerPath;
            layer.FigmaBounds = new Rect(30f, 40f, 50f, 60f);
            layer.AnchoredPosition = new Vector2(-95f, 40f);
            layer.SizeDelta = new Vector2(50f, 60f);
            layer.SiblingIndex = 0;
            layer.UnityObjectPath = "Canvas/FigmaImport/Layer";

            FigmaImportSettings settings = new FigmaImportSettings();
            settings.FileKey = "fileKey";
            settings.ImageScale = 2f;
            settings.OutputFolder = TestFolder;

            FigmaImportManifestService service = new FigmaImportManifestService();
            string manifestPath = service.SaveManifest(
                settings,
                root,
                TestFolder,
                referencePath,
                new List<ImportedLayerInfo> { layer });

            FigmaManifestRebuildData data = service.LoadForRebuild(manifestPath);

            Assert.AreEqual("fileKey", data.FileKey);
            Assert.AreEqual(2f, data.ImageScale);
            Assert.AreEqual(root.Id, data.RootNode.Id);
            Assert.AreEqual(root.AbsoluteBounds, data.RootNode.AbsoluteBounds);
            Assert.AreEqual(referencePath, data.ReferenceAssetPath);
            Assert.AreEqual(1, data.Layers.Count);
            Assert.AreEqual(layer.AssetPath, data.Layers[0].AssetPath);
            Assert.AreEqual(layer.AnchoredPosition, data.Layers[0].AnchoredPosition);
            Assert.AreEqual(layer.SizeDelta, data.Layers[0].SizeDelta);
        }

        private static void WriteEmptyAsset(string assetPath)
        {
            string fullPath = FigmaPathUtility.ToFullPath(assetPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(fullPath, new byte[] { 0 });
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
