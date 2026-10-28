using System;
using System.Collections.Generic;
using UnityEngine;
using static FigmaUiImporter.Editor.FigmaLocalization;

namespace FigmaUiImporter.Editor
{
    internal static class FigmaDocumentParser
    {
        private static readonly HashSet<string> RenderableTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RECTANGLE",
            "ELLIPSE",
            "POLYGON",
            "STAR",
            "VECTOR",
            "BOOLEAN_OPERATION",
            "LINE",
            "TEXT",
            "FRAME",
            "GROUP",
            "COMPONENT",
            "INSTANCE"
        };

        private static readonly HashSet<string> ContainerTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DOCUMENT",
            "CANVAS",
            "FRAME",
            "GROUP",
            "COMPONENT",
            "INSTANCE"
        };

        private static readonly HashSet<string> AtomicContainerTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "INSTANCE"
        };

        public static FigmaNodeInfo ParseFile(string json)
        {
            Dictionary<string, object> root = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("document"))
            {
                throw new InvalidOperationException(L("Figma 返回内容缺少 document。", "Figma response is missing document."));
            }

            return ParseNode(root["document"] as Dictionary<string, object>, null);
        }

        public static FigmaNodeInfo ParseNodeResponse(string json, string nodeId)
        {
            Dictionary<string, object> root = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("nodes"))
            {
                throw new InvalidOperationException(L("Figma 返回内容缺少 nodes。", "Figma response is missing nodes."));
            }

            Dictionary<string, object> nodes = root["nodes"] as Dictionary<string, object>;
            if (nodes == null || nodes.Count == 0)
            {
                throw new InvalidOperationException(L("未读取到所选节点。", "The selected node could not be read."));
            }

            object wrapperObject;
            if (!string.IsNullOrEmpty(nodeId) && nodes.TryGetValue(nodeId, out wrapperObject))
            {
                Dictionary<string, object> wrapper = wrapperObject as Dictionary<string, object>;
                if (wrapper != null && wrapper.ContainsKey("document"))
                {
                    return ParseNode(wrapper["document"] as Dictionary<string, object>, null);
                }
            }

            foreach (KeyValuePair<string, object> pair in nodes)
            {
                Dictionary<string, object> wrapper = pair.Value as Dictionary<string, object>;
                if (wrapper != null && wrapper.ContainsKey("document"))
                {
                    return ParseNode(wrapper["document"] as Dictionary<string, object>, null);
                }
            }

            throw new InvalidOperationException(L("所选节点没有 document 数据。", "The selected node has no document data."));
        }

        public static List<FigmaNodeInfo> GetPages(FigmaNodeInfo documentRoot)
        {
            List<FigmaNodeInfo> pages = new List<FigmaNodeInfo>();
            if (documentRoot == null)
            {
                return pages;
            }

            for (int i = 0; i < documentRoot.Children.Count; i++)
            {
                FigmaNodeInfo child = documentRoot.Children[i];
                if (child.IsPage)
                {
                    pages.Add(child);
                }
            }

            return pages;
        }

        public static void CollectSelectableFrames(FigmaNodeInfo node, List<FigmaNodeInfo> results)
        {
            if (node == null)
            {
                return;
            }

            if (node.IsFrameLike)
            {
                results.Add(node);
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                CollectSelectableFrames(node.Children[i], results);
            }
        }

        public static List<FigmaNodeInfo> CollectImportableNodes(FigmaNodeInfo selectedRoot, FigmaSliceMode sliceMode)
        {
            return CollectImportableNodes(selectedRoot, sliceMode, FigmaContainerSliceStrategy.Auto);
        }

        public static List<FigmaNodeInfo> CollectImportableNodes(
            FigmaNodeInfo selectedRoot,
            FigmaSliceMode sliceMode,
            FigmaContainerSliceStrategy containerSliceStrategy)
        {
            return CreateImportPlan(selectedRoot, sliceMode, containerSliceStrategy).Nodes;
        }

        public static FigmaImportPlan CreateImportPlan(
            FigmaNodeInfo selectedRoot,
            FigmaSliceMode sliceMode,
            FigmaContainerSliceStrategy containerSliceStrategy)
        {
            FigmaImportPlan plan = new FigmaImportPlan();
            if (selectedRoot == null || sliceMode == FigmaSliceMode.FlattenSelectedFrameAsSingleImage)
            {
                BuildDiagnostics(
                    selectedRoot,
                    sliceMode,
                    containerSliceStrategy,
                    new Dictionary<string, FigmaNodeInfo>(),
                    new Dictionary<string, FigmaNodeInfo>(),
                    new Dictionary<string, FigmaNodeInfo>(),
                    plan.Diagnostics);
                return plan;
            }

            List<FigmaNodeInfo> markedNodes = new List<FigmaNodeInfo>();
            if (sliceMode == FigmaSliceMode.ExportMarkedNodesOnly || sliceMode == FigmaSliceMode.ExportSettingsThenAutoSlice)
            {
                CollectMarkedNodes(selectedRoot, selectedRoot, markedNodes);
            }

            if (sliceMode == FigmaSliceMode.ExportMarkedNodesOnly)
            {
                plan.Nodes.AddRange(markedNodes);
                BuildDiagnostics(
                    selectedRoot,
                    sliceMode,
                    containerSliceStrategy,
                    ToNodeMap(markedNodes),
                    new Dictionary<string, FigmaNodeInfo>(),
                    ToNodeMap(plan.Nodes),
                    plan.Diagnostics);
                return plan;
            }

            List<FigmaNodeInfo> autoSliceNodes = new List<FigmaNodeInfo>();
            CollectAutoSliceNodes(selectedRoot, selectedRoot, autoSliceNodes);

            if (sliceMode == FigmaSliceMode.ExportSettingsThenAutoSlice)
            {
                plan.Nodes.AddRange(MergeMarkedAndAutoSliceNodes(selectedRoot, markedNodes, autoSliceNodes, containerSliceStrategy));
            }
            else if (sliceMode == FigmaSliceMode.AutoSliceVisibleLayers)
            {
                plan.Nodes.AddRange(autoSliceNodes);
            }

            BuildDiagnostics(
                selectedRoot,
                sliceMode,
                containerSliceStrategy,
                ToNodeMap(markedNodes),
                ToNodeMap(autoSliceNodes),
                ToNodeMap(plan.Nodes),
                plan.Diagnostics);
            return plan;
        }

        /// <summary>
        /// 统计导出计划中“祖先也已被选中”的节点数量。REST 图片接口无法隐藏子级，
        /// 这类节点会与祖先的整张 PNG 重复叠加，窗口可据此提示用户。
        /// </summary>
        public static int CountNodesWithSelectedAncestor(IList<FigmaNodeInfo> nodes)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return 0;
            }

            HashSet<string> selectedIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] != null && !string.IsNullOrEmpty(nodes[i].Id))
                {
                    selectedIds.Add(nodes[i].Id);
                }
            }

            int overlapCount = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                FigmaNodeInfo current = nodes[i] == null ? null : nodes[i].Parent;
                while (current != null)
                {
                    if (!string.IsNullOrEmpty(current.Id) && selectedIds.Contains(current.Id))
                    {
                        overlapCount++;
                        break;
                    }

                    current = current.Parent;
                }
            }

            return overlapCount;
        }

        public static bool EnsureBoundsFromChildren(FigmaNodeInfo node)
        {
            if (node == null)
            {
                return false;
            }

            if (node.HasUsableBounds)
            {
                return true;
            }

            bool hasBounds = false;
            float minX = 0f;
            float minY = 0f;
            float maxX = 0f;
            float maxY = 0f;

            for (int i = 0; i < node.Children.Count; i++)
            {
                FigmaNodeInfo child = node.Children[i];
                if (!EnsureBoundsFromChildren(child))
                {
                    continue;
                }

                Rect bounds = child.AbsoluteBounds;
                if (!hasBounds)
                {
                    minX = bounds.xMin;
                    minY = bounds.yMin;
                    maxX = bounds.xMax;
                    maxY = bounds.yMax;
                    hasBounds = true;
                }
                else
                {
                    minX = Mathf.Min(minX, bounds.xMin);
                    minY = Mathf.Min(minY, bounds.yMin);
                    maxX = Mathf.Max(maxX, bounds.xMax);
                    maxY = Mathf.Max(maxY, bounds.yMax);
                }
            }

            if (hasBounds)
            {
                node.AbsoluteBounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
                node.HasAbsoluteBounds = true;
            }

            return hasBounds;
        }

        private static void CollectMarkedNodes(FigmaNodeInfo root, FigmaNodeInfo node, List<FigmaNodeInfo> results)
        {
            if (node == null || !node.Visible)
            {
                return;
            }

            if (node.HasExportSettings && IsNodeInsideRoot(root, node) && node.HasUsableBounds)
            {
                results.Add(node);
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                CollectMarkedNodes(root, node.Children[i], results);
            }
        }

        private static bool CollectAutoSliceNodes(FigmaNodeInfo root, FigmaNodeInfo node, List<FigmaNodeInfo> results)
        {
            if (node == null || !node.Visible || !node.HasUsableBounds || !IsNodeInsideRoot(root, node))
            {
                return false;
            }

            bool isRoot = node == root;
            bool hasOwnRootVisual = isRoot && IsRenderableNode(node) && HasVisualSelf(node);
            if (hasOwnRootVisual)
            {
                results.Add(node);
            }

            bool canRenderSelf = !isRoot && IsRenderableNode(node);
            if (canRenderSelf && IsAtomicContainerNode(node))
            {
                results.Add(node);
                return true;
            }

            bool isContainer = IsContainerNode(node);

            if (canRenderSelf && !isContainer && HasRenderableSelf(node))
            {
                results.Add(node);
                return true;
            }

            bool hasOwnContainerVisual = canRenderSelf && isContainer && HasVisualSelf(node);
            if (hasOwnContainerVisual)
            {
                results.Add(node);
            }

            bool hasCollectedChild = false;
            for (int i = 0; i < node.Children.Count; i++)
            {
                hasCollectedChild |= CollectAutoSliceNodes(root, node.Children[i], results);
            }

            if (isRoot)
            {
                return hasOwnRootVisual || hasCollectedChild;
            }

            if (hasOwnContainerVisual)
            {
                return true;
            }

            if (canRenderSelf && !hasCollectedChild && (node.HasExportSettings || node.Children.Count == 0 || HasRenderableSelf(node)))
            {
                results.Add(node);
                return true;
            }

            return hasCollectedChild;
        }

        private static List<FigmaNodeInfo> MergeMarkedAndAutoSliceNodes(
            FigmaNodeInfo root,
            IList<FigmaNodeInfo> markedNodes,
            IList<FigmaNodeInfo> autoSliceNodes,
            FigmaContainerSliceStrategy containerSliceStrategy)
        {
            Dictionary<string, FigmaNodeInfo> markedMap = ToNodeMap(markedNodes);
            Dictionary<string, FigmaNodeInfo> autoSliceMap = ToNodeMap(autoSliceNodes);
            List<FigmaNodeInfo> merged = new List<FigmaNodeInfo>();
            FigmaNodeInfo markedRoot;
            if (root != null && !string.IsNullOrEmpty(root.Id) && markedMap.TryGetValue(root.Id, out markedRoot))
            {
                merged.Add(markedRoot);
            }
            else if (root != null && !string.IsNullOrEmpty(root.Id))
            {
                FigmaNodeInfo autoRoot;
                if (autoSliceMap.TryGetValue(root.Id, out autoRoot))
                {
                    merged.Add(autoRoot);
                }
            }

            CollectMergedNodesInDrawOrder(root, markedMap, autoSliceMap, containerSliceStrategy, merged);
            return merged;
        }

        private static void CollectMergedNodesInDrawOrder(
            FigmaNodeInfo node,
            IDictionary<string, FigmaNodeInfo> markedMap,
            IDictionary<string, FigmaNodeInfo> autoSliceMap,
            FigmaContainerSliceStrategy containerSliceStrategy,
            IList<FigmaNodeInfo> results)
        {
            if (node == null)
            {
                return;
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                FigmaNodeInfo child = node.Children[i];
                FigmaNodeInfo markedNode;
                if (markedMap.TryGetValue(child.Id, out markedNode))
                {
                    if (ShouldExpandMarkedContainer(child, autoSliceMap, containerSliceStrategy))
                    {
                        if (HasVisualSelf(child))
                        {
                            results.Add(markedNode);
                        }

                        CollectMergedNodesInDrawOrder(child, markedMap, autoSliceMap, containerSliceStrategy, results);
                        continue;
                    }

                    results.Add(markedNode);
                    continue;
                }

                FigmaNodeInfo autoSliceNode;
                if (autoSliceMap.TryGetValue(child.Id, out autoSliceNode)
                    && (IsAtomicContainerNode(child) || !HasMappedDescendant(child, markedMap)))
                {
                    if (!IsAtomicContainerNode(child)
                        && HasVisualSelf(child)
                        && HasMappedDescendant(child, autoSliceMap))
                    {
                        results.Add(autoSliceNode);
                        CollectMergedNodesInDrawOrder(child, markedMap, autoSliceMap, containerSliceStrategy, results);
                        continue;
                    }

                    results.Add(autoSliceNode);
                    continue;
                }

                CollectMergedNodesInDrawOrder(child, markedMap, autoSliceMap, containerSliceStrategy, results);
            }
        }

        private static bool HasMappedDescendant(FigmaNodeInfo node, IDictionary<string, FigmaNodeInfo> map)
        {
            if (node == null || map == null || map.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                FigmaNodeInfo child = node.Children[i];
                if (map.ContainsKey(child.Id) || HasMappedDescendant(child, map))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldExpandMarkedContainer(
            FigmaNodeInfo node,
            IDictionary<string, FigmaNodeInfo> autoSliceMap,
            FigmaContainerSliceStrategy containerSliceStrategy)
        {
            if (node == null || !HasMappedDescendant(node, autoSliceMap))
            {
                return false;
            }

            if (containerSliceStrategy == FigmaContainerSliceStrategy.StrictExportSettings)
            {
                return false;
            }

            if (containerSliceStrategy == FigmaContainerSliceStrategy.ExpandContainers)
            {
                return IsContainerNode(node);
            }

            return node.IsAutoLayout && HasAtomicDescendant(node);
        }

        private static bool HasAtomicDescendant(FigmaNodeInfo node)
        {
            if (node == null)
            {
                return false;
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                FigmaNodeInfo child = node.Children[i];
                if (IsAtomicContainerNode(child) || HasAtomicDescendant(child))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasVisualSelf(FigmaNodeInfo node)
        {
            return node != null
                && (node.HasVisibleFills
                    || node.HasVisibleStrokes
                    || node.HasEffects
                    || node.HasTextCharacters);
        }

        private static void BuildDiagnostics(
            FigmaNodeInfo selectedRoot,
            FigmaSliceMode sliceMode,
            FigmaContainerSliceStrategy containerSliceStrategy,
            IDictionary<string, FigmaNodeInfo> markedMap,
            IDictionary<string, FigmaNodeInfo> autoSliceMap,
            IDictionary<string, FigmaNodeInfo> selectedMap,
            IList<FigmaImportDiagnosticEntry> diagnostics)
        {
            if (selectedRoot == null || diagnostics == null)
            {
                return;
            }

            for (int i = 0; i < selectedRoot.Children.Count; i++)
            {
                AppendDiagnostic(
                    selectedRoot.Children[i],
                    sliceMode,
                    containerSliceStrategy,
                    markedMap,
                    autoSliceMap,
                    selectedMap,
                    diagnostics);
            }
        }

        private static void AppendDiagnostic(
            FigmaNodeInfo node,
            FigmaSliceMode sliceMode,
            FigmaContainerSliceStrategy containerSliceStrategy,
            IDictionary<string, FigmaNodeInfo> markedMap,
            IDictionary<string, FigmaNodeInfo> autoSliceMap,
            IDictionary<string, FigmaNodeInfo> selectedMap,
            IList<FigmaImportDiagnosticEntry> diagnostics)
        {
            if (node == null)
            {
                return;
            }

            FigmaImportDiagnosticEntry entry = new FigmaImportDiagnosticEntry();
            entry.NodeId = node.Id;
            entry.NodeName = node.Name;
            entry.NodeType = node.Type;
            entry.LayoutMode = node.LayoutMode;

            bool selected = !string.IsNullOrEmpty(node.Id) && selectedMap.ContainsKey(node.Id);
            if (selected)
            {
                entry.Action = L("导出", "Export");
                entry.Reason = GetExportReason(node, markedMap, autoSliceMap, selectedMap, containerSliceStrategy);
            }
            else if (sliceMode == FigmaSliceMode.FlattenSelectedFrameAsSingleImage)
            {
                entry.Action = L("跳过", "Skip");
                entry.Reason = L("当前切图模式为仅导入整张参考图。", "The slice mode is reference-image-only.");
            }
            else if (!node.Visible)
            {
                entry.Action = L("跳过", "Skip");
                entry.Reason = L("节点不可见。", "Node is not visible.");
            }
            else if (!node.HasUsableBounds)
            {
                entry.Action = L("跳过", "Skip");
                entry.Reason = L("节点没有可用尺寸。", "Node has no usable size.");
            }
            else if (HasSelectedAncestor(node, selectedMap))
            {
                entry.Action = L("合并", "Merge");
                entry.Reason = F("节点已包含在导出的祖先容器中：{0}", "Node is already included in an exported ancestor: {0}", GetSelectedAncestorLabel(node, selectedMap));
            }
            else if (HasMappedDescendant(node, selectedMap))
            {
                entry.Action = L("展开", "Expand");
                entry.Reason = GetExpandReason(node, containerSliceStrategy);
            }
            else
            {
                entry.Action = L("跳过", "Skip");
                entry.Reason = GetSkipReason(node, markedMap, autoSliceMap);
            }

            diagnostics.Add(entry);

            for (int i = 0; i < node.Children.Count; i++)
            {
                AppendDiagnostic(
                    node.Children[i],
                    sliceMode,
                    containerSliceStrategy,
                    markedMap,
                    autoSliceMap,
                    selectedMap,
                    diagnostics);
            }
        }

        private static string GetExportReason(
            FigmaNodeInfo node,
            IDictionary<string, FigmaNodeInfo> markedMap,
            IDictionary<string, FigmaNodeInfo> autoSliceMap,
            IDictionary<string, FigmaNodeInfo> selectedMap,
            FigmaContainerSliceStrategy containerSliceStrategy)
        {
            bool marked = !string.IsNullOrEmpty(node.Id) && markedMap.ContainsKey(node.Id);
            bool auto = !string.IsNullOrEmpty(node.Id) && autoSliceMap.ContainsKey(node.Id);
            bool expandedContainerSelf = marked
                && IsContainerNode(node)
                && HasMappedDescendant(node, selectedMap)
                && containerSliceStrategy != FigmaContainerSliceStrategy.StrictExportSettings;

            if (expandedContainerSelf && HasVisualSelf(node))
            {
                return L("容器自身有可见内容，作为背景层导出，同时展开子级。", "The container has its own visible content; exported as a background layer and its children are expanded.");
            }

            if (marked && auto)
            {
                return L("节点设置了 ExportSettings，且符合自动切片条件。", "Node has export settings and meets auto-slice criteria.");
            }

            if (marked)
            {
                return L("节点设置了 ExportSettings。", "Node has export settings.");
            }

            if (IsAtomicContainerNode(node))
            {
                return L("组件实例按容器策略独立导出。", "Component instance exported separately by the container strategy.");
            }

            if (auto)
            {
                return L("节点符合自动切片条件。", "Node meets auto-slice criteria.");
            }

            return L("节点被选入导出列表。", "Node was selected for export.");
        }

        private static string GetExpandReason(FigmaNodeInfo node, FigmaContainerSliceStrategy containerSliceStrategy)
        {
            if (containerSliceStrategy == FigmaContainerSliceStrategy.ExpandContainers)
            {
                return L("容器处理策略为强制展开，已展开到子级切片。", "Container strategy forced expansion; exported child slices.");
            }

            if (node.IsAutoLayout)
            {
                return L("Auto Layout 容器包含组件实例，已展开到子级切片。", "Auto Layout container contains component instances; expanded to child slices.");
            }

            return L("节点本身未导出，但子级包含可导出的切片。", "Node itself is not exported, but its children contain exportable slices.");
        }

        private static string GetSkipReason(
            FigmaNodeInfo node,
            IDictionary<string, FigmaNodeInfo> markedMap,
            IDictionary<string, FigmaNodeInfo> autoSliceMap)
        {
            bool marked = !string.IsNullOrEmpty(node.Id) && markedMap.ContainsKey(node.Id);
            bool auto = !string.IsNullOrEmpty(node.Id) && autoSliceMap.ContainsKey(node.Id);
            if (marked)
            {
                return L("节点有 ExportSettings，但被容器策略或合并策略排除。", "Node has export settings but was excluded by the container or merge strategy.");
            }

            if (auto)
            {
                return L("节点符合自动切片条件，但绘制顺序中已有更合适的切片覆盖。", "Node meets auto-slice criteria, but a better slice covers it in draw order.");
            }

            if (IsContainerNode(node))
            {
                return L("容器没有可单独导出的视觉内容或子级已处理。", "Container has no separately exportable visual content, or its children were handled.");
            }

            if (!IsRenderableNode(node))
            {
                return L("节点类型不在当前可渲染类型列表中。", "Node type is not in the renderable type list.");
            }

            return L("节点不符合 ExportSettings 或自动切片条件。", "Node does not meet ExportSettings or auto-slice criteria.");
        }

        private static bool HasSelectedAncestor(FigmaNodeInfo node, IDictionary<string, FigmaNodeInfo> selectedMap)
        {
            FigmaNodeInfo current = node == null ? null : node.Parent;
            while (current != null)
            {
                if (!string.IsNullOrEmpty(current.Id) && selectedMap.ContainsKey(current.Id))
                {
                    return true;
                }

                current = current.Parent;
            }

            return false;
        }

        private static string GetSelectedAncestorLabel(FigmaNodeInfo node, IDictionary<string, FigmaNodeInfo> selectedMap)
        {
            FigmaNodeInfo current = node == null ? null : node.Parent;
            while (current != null)
            {
                if (!string.IsNullOrEmpty(current.Id) && selectedMap.ContainsKey(current.Id))
                {
                    return string.IsNullOrEmpty(current.Name) ? current.Id : current.Name + " [" + current.Id + "]";
                }

                current = current.Parent;
            }

            return L("(未知)", "(Unknown)");
        }

        private static Dictionary<string, FigmaNodeInfo> ToNodeMap(IList<FigmaNodeInfo> nodes)
        {
            Dictionary<string, FigmaNodeInfo> map = new Dictionary<string, FigmaNodeInfo>();
            if (nodes == null)
            {
                return map;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                FigmaNodeInfo node = nodes[i];
                if (node != null && !string.IsNullOrEmpty(node.Id) && !map.ContainsKey(node.Id))
                {
                    map.Add(node.Id, node);
                }
            }

            return map;
        }

        private static bool IsRenderableNode(FigmaNodeInfo node)
        {
            return node != null && RenderableTypes.Contains(node.Type);
        }

        private static bool IsContainerNode(FigmaNodeInfo node)
        {
            return node != null && ContainerTypes.Contains(node.Type);
        }

        private static bool IsAtomicContainerNode(FigmaNodeInfo node)
        {
            return node != null && AtomicContainerTypes.Contains(node.Type);
        }

        private static bool HasRenderableSelf(FigmaNodeInfo node)
        {
            return node != null
                && (node.HasExportSettings
                    || node.HasVisibleFills
                    || node.HasVisibleStrokes
                    || node.HasEffects
                    || node.HasTextCharacters
                    || node.Children.Count == 0);
        }

        private static bool IsNodeInsideRoot(FigmaNodeInfo root, FigmaNodeInfo node)
        {
            if (root == null || node == null || !root.HasUsableBounds || !node.HasUsableBounds)
            {
                return false;
            }

            Rect rootBounds = root.AbsoluteBounds;
            Rect nodeBounds = node.AbsoluteBounds;
            return nodeBounds.xMax >= rootBounds.xMin
                && nodeBounds.xMin <= rootBounds.xMax
                && nodeBounds.yMax >= rootBounds.yMin
                && nodeBounds.yMin <= rootBounds.yMax;
        }

        private static FigmaNodeInfo ParseNode(Dictionary<string, object> data, FigmaNodeInfo parent)
        {
            if (data == null)
            {
                return null;
            }

            FigmaNodeInfo node = new FigmaNodeInfo();
            node.Parent = parent;
            node.Id = GetString(data, "id");
            node.Name = GetString(data, "name");
            node.Type = GetString(data, "type");
            node.LayoutMode = GetString(data, "layoutMode");
            node.Visible = !data.ContainsKey("visible") || GetBool(data, "visible", true);
            // GROUP / TEXT 等类型没有 clipsContent 字段，默认不裁剪；Frame 类节点默认裁剪。
            node.ClipsContent = GetBool(data, "clipsContent", node.IsFrameLike);
            node.HasAbsoluteBounds = TryGetBounds(data, "absoluteBoundingBox", out node.AbsoluteBounds);
            node.HasAbsoluteRenderBounds = TryGetBounds(data, "absoluteRenderBounds", out node.AbsoluteRenderBounds);
            if ((!node.HasAbsoluteBounds || !IsUsableRect(node.AbsoluteBounds)) && node.HasAbsoluteRenderBounds)
            {
                node.AbsoluteBounds = node.AbsoluteRenderBounds;
                node.HasAbsoluteBounds = true;
            }
            NormalizeThinBounds(node);

            node.HasExportSettings = HasNonEmptyList(data, "exportSettings");
            node.HasVisibleFills = HasVisiblePaints(data, "fills");
            node.HasVisibleStrokes = HasVisiblePaints(data, "strokes");
            node.HasEffects = HasNonEmptyList(data, "effects");
            node.TextCharacters = GetString(data, "characters");
            node.HasTextCharacters = !string.IsNullOrEmpty(node.TextCharacters);
            if (string.Equals(node.Type, "TEXT", StringComparison.OrdinalIgnoreCase))
            {
                node.TextStyle = ParseTextStyle(data, node);
                node.HasMixedTextStyles = node.TextStyle != null && node.TextStyle.HasMixedStyles;
                node.HasUnsupportedTextVisuals = node.TextStyle != null && node.TextStyle.HasUnsupportedVisuals;
            }

            List<object> children = GetList(data, "children");
            if (children != null)
            {
                for (int i = 0; i < children.Count; i++)
                {
                    FigmaNodeInfo child = ParseNode(children[i] as Dictionary<string, object>, node);
                    if (child != null)
                    {
                        node.Children.Add(child);
                    }
                }
            }

            return node;
        }

        private static FigmaTextStyleInfo ParseTextStyle(Dictionary<string, object> data, FigmaNodeInfo node)
        {
            Dictionary<string, object> style = GetDictionary(data, "style");
            if (style == null || node == null || !node.HasTextCharacters)
            {
                return null;
            }

            FigmaTextStyleInfo text = new FigmaTextStyleInfo();
            text.Characters = node.TextCharacters;
            text.FontFamily = GetString(style, "fontFamily");
            text.FontStyleName = GetString(style, "fontStyle");
            text.FontPostScriptName = GetString(style, "fontPostScriptName");
            text.FontWeight = Mathf.RoundToInt(GetFloat(style, "fontWeight"));
            text.FontSize = GetFloat(style, "fontSize");
            text.LineHeightPx = GetFloat(style, "lineHeightPx");
            text.LetterSpacing = GetFloat(style, "letterSpacing");
            text.HorizontalAlign = GetString(style, "textAlignHorizontal");
            text.VerticalAlign = GetString(style, "textAlignVertical");
            text.AutoResize = GetString(data, "textAutoResize");
            text.HasMixedStyles = HasMixedTextStyleOverrides(data);

            Color color;
            bool hasUnsupportedPaints;
            bool hasSolidPaint = TryGetFirstSolidPaintColor(data, "fills", GetFloat(data, "opacity"), out color, out hasUnsupportedPaints);
            if (hasSolidPaint)
            {
                text.Color = color;
            }

            text.HasUnsupportedVisuals = !hasSolidPaint
                || hasUnsupportedPaints
                || HasVisiblePaints(data, "strokes")
                || HasNonEmptyList(data, "effects");
            return text;
        }

        private static bool HasMixedTextStyleOverrides(Dictionary<string, object> data)
        {
            List<object> overrides = GetList(data, "characterStyleOverrides");
            if (overrides != null)
            {
                for (int i = 0; i < overrides.Count; i++)
                {
                    if (Convert.ToInt32(overrides[i], System.Globalization.CultureInfo.InvariantCulture) != 0)
                    {
                        return true;
                    }
                }
            }

            Dictionary<string, object> overrideTable = GetDictionary(data, "styleOverrideTable");
            return overrideTable != null && overrideTable.Count > 0;
        }

        private static bool TryGetFirstSolidPaintColor(
            Dictionary<string, object> data,
            string key,
            float nodeOpacity,
            out Color color,
            out bool hasUnsupportedPaints)
        {
            color = Color.white;
            hasUnsupportedPaints = false;
            List<object> paints = GetList(data, key);
            if (paints == null)
            {
                return false;
            }

            bool foundColor = false;
            int visiblePaintCount = 0;
            for (int i = 0; i < paints.Count; i++)
            {
                Dictionary<string, object> paint = paints[i] as Dictionary<string, object>;
                if (paint == null || !GetBool(paint, "visible", true))
                {
                    continue;
                }

                visiblePaintCount++;
                if (!string.Equals(GetString(paint, "type"), "SOLID", StringComparison.OrdinalIgnoreCase))
                {
                    hasUnsupportedPaints = true;
                    continue;
                }

                Dictionary<string, object> paintColor = GetDictionary(paint, "color");
                if (paintColor == null)
                {
                    hasUnsupportedPaints = true;
                    continue;
                }

                if (!foundColor)
                {
                    float paintOpacity = GetFloat(paint, "opacity");
                    if (paintOpacity <= 0f)
                    {
                        paintOpacity = 1f;
                    }

                    float alpha = Mathf.Clamp01(paintOpacity * (nodeOpacity > 0f ? nodeOpacity : 1f));
                    color = new Color(
                        Mathf.Clamp01(GetFloat(paintColor, "r")),
                        Mathf.Clamp01(GetFloat(paintColor, "g")),
                        Mathf.Clamp01(GetFloat(paintColor, "b")),
                        alpha);
                    foundColor = true;
                }
            }

            if (visiblePaintCount > 1)
            {
                hasUnsupportedPaints = true;
            }

            return foundColor;
        }

        private static bool TryGetBounds(Dictionary<string, object> data, string key, out Rect rect)
        {
            rect = new Rect();
            Dictionary<string, object> bounds = GetDictionary(data, key);
            if (bounds == null)
            {
                return false;
            }

            rect = new Rect(
                GetFloat(bounds, "x"),
                GetFloat(bounds, "y"),
                GetFloat(bounds, "width"),
                GetFloat(bounds, "height"));
            return rect.width > 0.01f || rect.height > 0.01f;
        }

        private static bool IsUsableRect(Rect rect)
        {
            return rect.width > 0.01f && rect.height > 0.01f;
        }

        private static void NormalizeThinBounds(FigmaNodeInfo node)
        {
            if (node == null || !node.HasAbsoluteBounds)
            {
                return;
            }

            if (!string.Equals(node.Type, "LINE", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Rect bounds = node.AbsoluteBounds;
            bounds.width = Mathf.Max(1f, bounds.width);
            bounds.height = Mathf.Max(1f, bounds.height);
            node.AbsoluteBounds = bounds;

            if (node.HasAbsoluteRenderBounds)
            {
                Rect renderBounds = node.AbsoluteRenderBounds;
                renderBounds.width = Mathf.Max(1f, renderBounds.width);
                renderBounds.height = Mathf.Max(1f, renderBounds.height);
                node.AbsoluteRenderBounds = renderBounds;
            }
        }

        private static bool HasVisiblePaints(Dictionary<string, object> data, string key)
        {
            List<object> paints = GetList(data, key);
            if (paints == null)
            {
                return false;
            }

            for (int i = 0; i < paints.Count; i++)
            {
                Dictionary<string, object> paint = paints[i] as Dictionary<string, object>;
                if (paint == null)
                {
                    continue;
                }

                if (GetBool(paint, "visible", true))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasNonEmptyList(Dictionary<string, object> data, string key)
        {
            List<object> list = GetList(data, key);
            return list != null && list.Count > 0;
        }

        private static Dictionary<string, object> GetDictionary(Dictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        private static List<object> GetList(Dictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) ? value as List<object> : null;
        }

        private static string GetString(Dictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : string.Empty;
        }

        private static bool GetBool(Dictionary<string, object> data, string key, bool defaultValue)
        {
            object value;
            if (data == null || !data.TryGetValue(key, out value) || value == null)
            {
                return defaultValue;
            }

            if (value is bool)
            {
                return (bool)value;
            }

            bool parsed;
            return bool.TryParse(Convert.ToString(value), out parsed) ? parsed : defaultValue;
        }

        private static float GetFloat(Dictionary<string, object> data, string key)
        {
            object value;
            if (data == null || !data.TryGetValue(key, out value) || value == null)
            {
                return 0f;
            }

            return Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
