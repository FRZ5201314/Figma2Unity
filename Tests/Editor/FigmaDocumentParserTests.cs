using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace FigmaUiImporter.Editor.Tests
{
    public sealed class FigmaDocumentParserTests
    {
        [Test]
        public void ParseFileReadsLayoutMode()
        {
            string json = "{"
                + "\"document\":{"
                + "\"id\":\"0:0\","
                + "\"name\":\"Document\","
                + "\"type\":\"DOCUMENT\","
                + "\"children\":[{"
                + "\"id\":\"1:1\","
                + "\"name\":\"Auto Layout\","
                + "\"type\":\"FRAME\","
                + "\"layoutMode\":\"VERTICAL\","
                + "\"absoluteBoundingBox\":{\"x\":0,\"y\":0,\"width\":100,\"height\":80}"
                + "}]"
                + "}"
                + "}";

            FigmaNodeInfo document = FigmaDocumentParser.ParseFile(json);

            Assert.AreEqual(1, document.Children.Count);
            Assert.AreEqual("VERTICAL", document.Children[0].LayoutMode);
            Assert.IsTrue(document.Children[0].IsAutoLayout);
        }

        [Test]
        public void DefaultSliceModeKeepsMarkedAutoLayoutVisualSelfBeforeInstanceChild()
        {
            FigmaNodeInfo root = CreateNode("0:0", "Root", "FRAME", new Rect(0f, 0f, 300f, 200f));
            FigmaNodeInfo autoLayout = CreateNode("1:1", "Auto Layout", "FRAME", new Rect(10f, 10f, 200f, 120f));
            autoLayout.LayoutMode = "VERTICAL";
            autoLayout.HasExportSettings = true;
            autoLayout.HasVisibleFills = true;

            FigmaNodeInfo instance = CreateNode("2:1", "Button Instance", "INSTANCE", new Rect(20f, 20f, 160f, 40f));
            AddChild(root, autoLayout);
            AddChild(autoLayout, instance);

            List<FigmaNodeInfo> nodes = FigmaDocumentParser.CollectImportableNodes(
                root,
                FigmaSliceMode.ExportSettingsThenAutoSlice);

            Assert.AreEqual(2, nodes.Count);
            Assert.AreEqual(autoLayout.Id, nodes[0].Id);
            Assert.AreEqual(instance.Id, nodes[1].Id);
        }

        [Test]
        public void DefaultSliceModeSkipsMarkedAutoLayoutShellWithoutVisualSelf()
        {
            FigmaNodeInfo root = CreateNode("0:0", "Root", "FRAME", new Rect(0f, 0f, 300f, 200f));
            FigmaNodeInfo autoLayout = CreateNode("1:1", "Auto Layout", "FRAME", new Rect(10f, 10f, 200f, 120f));
            autoLayout.LayoutMode = "VERTICAL";
            autoLayout.HasExportSettings = true;

            FigmaNodeInfo instance = CreateNode("2:1", "Button Instance", "INSTANCE", new Rect(20f, 20f, 160f, 40f));
            AddChild(root, autoLayout);
            AddChild(autoLayout, instance);

            List<FigmaNodeInfo> nodes = FigmaDocumentParser.CollectImportableNodes(
                root,
                FigmaSliceMode.ExportSettingsThenAutoSlice);

            Assert.AreEqual(1, nodes.Count);
            Assert.AreEqual(instance.Id, nodes[0].Id);
        }

        [Test]
        public void MarkedOnlySliceModeKeepsMarkedAutoLayoutContainer()
        {
            FigmaNodeInfo root = CreateNode("0:0", "Root", "FRAME", new Rect(0f, 0f, 300f, 200f));
            FigmaNodeInfo autoLayout = CreateNode("1:1", "Auto Layout", "FRAME", new Rect(10f, 10f, 200f, 120f));
            autoLayout.LayoutMode = "VERTICAL";
            autoLayout.HasExportSettings = true;

            FigmaNodeInfo instance = CreateNode("2:1", "Button Instance", "INSTANCE", new Rect(20f, 20f, 160f, 40f));
            AddChild(root, autoLayout);
            AddChild(autoLayout, instance);

            List<FigmaNodeInfo> nodes = FigmaDocumentParser.CollectImportableNodes(
                root,
                FigmaSliceMode.ExportMarkedNodesOnly);

            Assert.AreEqual(1, nodes.Count);
            Assert.AreEqual(autoLayout.Id, nodes[0].Id);
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
    }
}
