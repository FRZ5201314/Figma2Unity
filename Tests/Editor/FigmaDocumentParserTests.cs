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
        public void ParseFileReadsSingleStyleTextData()
        {
            string json = "{"
                + "\"document\":{"
                + "\"id\":\"0:0\","
                + "\"name\":\"Document\","
                + "\"type\":\"DOCUMENT\","
                + "\"children\":[{"
                + "\"id\":\"1:1\","
                + "\"name\":\"Title\","
                + "\"type\":\"TEXT\","
                + "\"characters\":\"Hello\","
                + "\"absoluteBoundingBox\":{\"x\":0,\"y\":0,\"width\":120,\"height\":40},"
                + "\"style\":{\"fontFamily\":\"Inter\",\"fontStyle\":\"Bold\",\"fontPostScriptName\":\"Inter-Bold\",\"fontWeight\":700,\"fontSize\":24,\"lineHeightPx\":30,\"letterSpacing\":1.5,\"textAlignHorizontal\":\"CENTER\",\"textAlignVertical\":\"TOP\"},"
                + "\"fills\":[{\"type\":\"SOLID\",\"color\":{\"r\":0.1,\"g\":0.2,\"b\":0.3},\"opacity\":0.8}],"
                + "\"characterStyleOverrides\":[0,0,0,0,0],"
                + "\"styleOverrideTable\":{}"
                + "}]"
                + "}"
                + "}";

            FigmaNodeInfo document = FigmaDocumentParser.ParseFile(json);
            FigmaNodeInfo textNode = document.Children[0];

            Assert.AreEqual("Hello", textNode.TextCharacters);
            Assert.IsNotNull(textNode.TextStyle);
            Assert.AreEqual("Inter", textNode.TextStyle.FontFamily);
            Assert.AreEqual("Bold", textNode.TextStyle.FontStyleName);
            Assert.AreEqual("Inter-Bold", textNode.TextStyle.FontPostScriptName);
            Assert.AreEqual(700, textNode.TextStyle.FontWeight);
            Assert.AreEqual(24f, textNode.TextStyle.FontSize);
            Assert.AreEqual(30f, textNode.TextStyle.LineHeightPx);
            Assert.AreEqual(1.5f, textNode.TextStyle.LetterSpacing);
            Assert.AreEqual("CENTER", textNode.TextStyle.HorizontalAlign);
            Assert.AreEqual(0.1f, textNode.TextStyle.Color.r, 0.001f);
            Assert.AreEqual(0.8f, textNode.TextStyle.Color.a, 0.001f);
            Assert.IsTrue(textNode.TextStyle.IsSmartImportSafe);
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
        public void DefaultSliceModeKeepsUnmarkedChildFrameVisualSelfBeforeChildren()
        {
            FigmaNodeInfo root = CreateNode("0:0", "Root", "FRAME", new Rect(0f, 0f, 300f, 200f));
            FigmaNodeInfo childFrame = CreateNode("1:1", "Panel", "FRAME", new Rect(10f, 10f, 200f, 120f));
            childFrame.HasVisibleFills = true;

            FigmaNodeInfo icon = CreateNode("2:1", "Icon", "RECTANGLE", new Rect(20f, 20f, 40f, 40f));
            icon.HasVisibleFills = true;
            AddChild(root, childFrame);
            AddChild(childFrame, icon);

            List<FigmaNodeInfo> nodes = FigmaDocumentParser.CollectImportableNodes(
                root,
                FigmaSliceMode.ExportSettingsThenAutoSlice);

            Assert.AreEqual(2, nodes.Count);
            Assert.AreEqual(childFrame.Id, nodes[0].Id);
            Assert.AreEqual(icon.Id, nodes[1].Id);
        }

        [Test]
        public void DefaultSliceModeKeepsUnmarkedRootVisualSelfBeforeChildren()
        {
            FigmaNodeInfo root = CreateNode("0:0", "Root Background", "FRAME", new Rect(0f, 0f, 300f, 200f));
            root.HasVisibleFills = true;

            FigmaNodeInfo button = CreateNode("1:1", "Button", "INSTANCE", new Rect(20f, 20f, 100f, 40f));
            AddChild(root, button);

            List<FigmaNodeInfo> nodes = FigmaDocumentParser.CollectImportableNodes(
                root,
                FigmaSliceMode.ExportSettingsThenAutoSlice);

            Assert.AreEqual(2, nodes.Count);
            Assert.AreEqual(root.Id, nodes[0].Id);
            Assert.AreEqual(button.Id, nodes[1].Id);
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

        [Test]
        public void MarkedOnlySliceModeKeepsMarkedRootFrameAsFirstLayer()
        {
            FigmaNodeInfo root = CreateNode("0:0", "Root Background", "FRAME", new Rect(0f, 0f, 300f, 200f));
            root.HasExportSettings = true;
            root.HasVisibleFills = true;

            FigmaNodeInfo button = CreateNode("1:1", "Button", "FRAME", new Rect(20f, 20f, 100f, 40f));
            button.HasExportSettings = true;
            AddChild(root, button);

            List<FigmaNodeInfo> nodes = FigmaDocumentParser.CollectImportableNodes(
                root,
                FigmaSliceMode.ExportMarkedNodesOnly);

            Assert.AreEqual(2, nodes.Count);
            Assert.AreEqual(root.Id, nodes[0].Id);
            Assert.AreEqual(button.Id, nodes[1].Id);
        }

        [Test]
        public void DefaultSliceModeKeepsMarkedRootFrameBeforeAutoChildren()
        {
            FigmaNodeInfo root = CreateNode("0:0", "Root Background", "FRAME", new Rect(0f, 0f, 300f, 200f));
            root.HasExportSettings = true;
            root.HasVisibleFills = true;

            FigmaNodeInfo instance = CreateNode("1:1", "Button Instance", "INSTANCE", new Rect(20f, 20f, 100f, 40f));
            AddChild(root, instance);

            List<FigmaNodeInfo> nodes = FigmaDocumentParser.CollectImportableNodes(
                root,
                FigmaSliceMode.ExportSettingsThenAutoSlice);

            Assert.AreEqual(2, nodes.Count);
            Assert.AreEqual(root.Id, nodes[0].Id);
            Assert.AreEqual(instance.Id, nodes[1].Id);
        }

        [Test]
        public void StrictContainerStrategyKeepsMarkedContainerInMixedMode()
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
                FigmaSliceMode.ExportSettingsThenAutoSlice,
                FigmaContainerSliceStrategy.StrictExportSettings);

            Assert.AreEqual(1, nodes.Count);
            Assert.AreEqual(autoLayout.Id, nodes[0].Id);
        }

        [Test]
        public void ExpandContainerStrategyExpandsMarkedNonAutoContainer()
        {
            FigmaNodeInfo root = CreateNode("0:0", "Root", "FRAME", new Rect(0f, 0f, 300f, 200f));
            FigmaNodeInfo group = CreateNode("1:1", "Marked Group", "GROUP", new Rect(10f, 10f, 200f, 120f));
            group.HasExportSettings = true;
            group.HasVisibleFills = true;

            FigmaNodeInfo instance = CreateNode("2:1", "Card Instance", "INSTANCE", new Rect(20f, 20f, 160f, 40f));
            AddChild(root, group);
            AddChild(group, instance);

            List<FigmaNodeInfo> nodes = FigmaDocumentParser.CollectImportableNodes(
                root,
                FigmaSliceMode.ExportSettingsThenAutoSlice,
                FigmaContainerSliceStrategy.ExpandContainers);

            Assert.AreEqual(2, nodes.Count);
            Assert.AreEqual(group.Id, nodes[0].Id);
            Assert.AreEqual(instance.Id, nodes[1].Id);
        }

        [Test]
        public void ImportPlanReportsExportedAndExpandedNodes()
        {
            FigmaNodeInfo root = CreateNode("0:0", "Root", "FRAME", new Rect(0f, 0f, 300f, 200f));
            FigmaNodeInfo autoLayout = CreateNode("1:1", "Auto Layout", "FRAME", new Rect(10f, 10f, 200f, 120f));
            autoLayout.LayoutMode = "VERTICAL";
            autoLayout.HasExportSettings = true;

            FigmaNodeInfo instance = CreateNode("2:1", "Button Instance", "INSTANCE", new Rect(20f, 20f, 160f, 40f));
            AddChild(root, autoLayout);
            AddChild(autoLayout, instance);

            FigmaImportPlan plan = FigmaDocumentParser.CreateImportPlan(
                root,
                FigmaSliceMode.ExportSettingsThenAutoSlice,
                FigmaContainerSliceStrategy.Auto);

            FigmaImportDiagnosticEntry autoLayoutEntry = FindDiagnostic(plan, autoLayout.Id);
            FigmaImportDiagnosticEntry instanceEntry = FindDiagnostic(plan, instance.Id);

            Assert.AreEqual("展开", autoLayoutEntry.Action);
            StringAssert.Contains("Auto Layout", autoLayoutEntry.Reason);
            Assert.AreEqual("导出", instanceEntry.Action);
            StringAssert.Contains("组件实例", instanceEntry.Reason);
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

        private static FigmaImportDiagnosticEntry FindDiagnostic(FigmaImportPlan plan, string nodeId)
        {
            for (int i = 0; i < plan.Diagnostics.Count; i++)
            {
                if (plan.Diagnostics[i].NodeId == nodeId)
                {
                    return plan.Diagnostics[i];
                }
            }

            Assert.Fail("未找到诊断项：" + nodeId);
            return null;
        }
    }
}
