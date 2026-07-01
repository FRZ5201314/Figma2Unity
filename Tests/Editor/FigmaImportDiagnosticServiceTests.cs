using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FigmaUiImporter.Editor.Tests
{
    public sealed class FigmaImportDiagnosticServiceTests
    {
        private const string TestFolder = "Assets/FigmaUiImporterDiagnosticTests";

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
        public void SaveReportWritesDiagnosticsJson()
        {
            FigmaNodeInfo root = CreateNode("0:0", "Root", "FRAME", new Rect(0f, 0f, 100f, 80f));
            FigmaNodeInfo child = CreateNode("1:1", "Icon", "RECTANGLE", new Rect(10f, 10f, 20f, 20f));
            child.HasVisibleFills = true;
            AddChild(root, child);

            FigmaImportPlan plan = FigmaDocumentParser.CreateImportPlan(
                root,
                FigmaSliceMode.AutoSliceVisibleLayers,
                FigmaContainerSliceStrategy.Auto);

            ImportedLayerInfo layer = new ImportedLayerInfo();
            layer.NodeId = child.Id;
            layer.NodeName = child.Name;
            layer.NodeType = child.Type;
            layer.AssetPath = FigmaPathUtility.CombineAssetPath(TestFolder, "layers", "icon.png");
            layer.UnityObjectPath = "Canvas/Root/Icon";

            FigmaImportSettings settings = new FigmaImportSettings();
            settings.FileKey = "fileKey";
            settings.OutputFolder = TestFolder;
            settings.SliceMode = FigmaSliceMode.AutoSliceVisibleLayers;

            string reportPath = new FigmaImportDiagnosticService().SaveReport(
                settings,
                root,
                TestFolder,
                plan,
                new List<ImportedLayerInfo> { layer });

            string json = File.ReadAllText(FigmaPathUtility.ToFullPath(reportPath));
            StringAssert.Contains("Figma API", json);
            StringAssert.Contains("导出", json);
            StringAssert.Contains("Assets/FigmaUiImporterDiagnosticTests/layers/icon.png", json);
        }

        private static FigmaNodeInfo CreateNode(string id, string name, string type, Rect bounds)
        {
            FigmaNodeInfo node = new FigmaNodeInfo();
            node.Id = id;
            node.Name = name;
            node.Type = type;
            node.Visible = true;
            node.AbsoluteBounds = bounds;
            node.HasAbsoluteBounds = true;
            return node;
        }

        private static void AddChild(FigmaNodeInfo parent, FigmaNodeInfo child)
        {
            child.Parent = parent;
            parent.Children.Add(child);
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
