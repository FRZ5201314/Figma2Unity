using System;
using System.Collections.Generic;
using UnityEngine;

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

        public static FigmaNodeInfo ParseFile(string json)
        {
            Dictionary<string, object> root = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("document"))
            {
                throw new InvalidOperationException("Figma 返回内容缺少 document。");
            }

            return ParseNode(root["document"] as Dictionary<string, object>, null);
        }

        public static FigmaNodeInfo ParseNodeResponse(string json, string nodeId)
        {
            Dictionary<string, object> root = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("nodes"))
            {
                throw new InvalidOperationException("Figma 返回内容缺少 nodes。");
            }

            Dictionary<string, object> nodes = root["nodes"] as Dictionary<string, object>;
            if (nodes == null || nodes.Count == 0)
            {
                throw new InvalidOperationException("未读取到所选节点。");
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

            throw new InvalidOperationException("所选节点没有 document 数据。");
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
            List<FigmaNodeInfo> results = new List<FigmaNodeInfo>();
            if (selectedRoot == null || sliceMode == FigmaSliceMode.FlattenSelectedFrameAsSingleImage)
            {
                return results;
            }

            if (sliceMode == FigmaSliceMode.ExportMarkedNodesOnly || sliceMode == FigmaSliceMode.ExportSettingsThenAutoSlice)
            {
                CollectMarkedNodes(selectedRoot, selectedRoot, results);
            }

            if (sliceMode == FigmaSliceMode.ExportMarkedNodesOnly)
            {
                return results;
            }

            List<FigmaNodeInfo> autoSliceNodes = new List<FigmaNodeInfo>();
            CollectAutoSliceNodes(selectedRoot, selectedRoot, autoSliceNodes);

            if (sliceMode == FigmaSliceMode.ExportSettingsThenAutoSlice)
            {
                return MergeMarkedAndAutoSliceNodes(selectedRoot, results, autoSliceNodes);
            }

            if (sliceMode == FigmaSliceMode.AutoSliceVisibleLayers)
            {
                return autoSliceNodes;
            }

            return results;
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

            if (node != root && node.HasExportSettings && IsNodeInsideRoot(root, node) && node.HasUsableBounds)
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

            bool canRenderSelf = node != root && IsRenderableNode(node);
            bool isContainer = IsContainerNode(node);

            if (canRenderSelf && !isContainer && HasRenderableSelf(node))
            {
                results.Add(node);
                return true;
            }

            bool hasCollectedChild = false;
            for (int i = 0; i < node.Children.Count; i++)
            {
                hasCollectedChild |= CollectAutoSliceNodes(root, node.Children[i], results);
            }

            if (node == root)
            {
                return hasCollectedChild;
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
            IList<FigmaNodeInfo> autoSliceNodes)
        {
            Dictionary<string, FigmaNodeInfo> markedMap = ToNodeMap(markedNodes);
            Dictionary<string, FigmaNodeInfo> autoSliceMap = ToNodeMap(autoSliceNodes);
            List<FigmaNodeInfo> merged = new List<FigmaNodeInfo>();
            CollectMergedNodesInDrawOrder(root, markedMap, autoSliceMap, merged);
            return merged;
        }

        private static void CollectMergedNodesInDrawOrder(
            FigmaNodeInfo node,
            IDictionary<string, FigmaNodeInfo> markedMap,
            IDictionary<string, FigmaNodeInfo> autoSliceMap,
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
                    results.Add(markedNode);
                    continue;
                }

                FigmaNodeInfo autoSliceNode;
                if (autoSliceMap.TryGetValue(child.Id, out autoSliceNode) && !HasMappedDescendant(child, markedMap))
                {
                    results.Add(autoSliceNode);
                    continue;
                }

                CollectMergedNodesInDrawOrder(child, markedMap, autoSliceMap, results);
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
            node.Visible = !data.ContainsKey("visible") || GetBool(data, "visible", true);
            node.HasAbsoluteBounds = TryGetBounds(data, "absoluteBoundingBox", out node.AbsoluteBounds);
            if (!node.HasAbsoluteBounds)
            {
                node.HasAbsoluteBounds = TryGetBounds(data, "absoluteRenderBounds", out node.AbsoluteBounds);
            }
            NormalizeThinBounds(node);

            node.HasExportSettings = HasNonEmptyList(data, "exportSettings");
            node.HasVisibleFills = HasVisiblePaints(data, "fills");
            node.HasVisibleStrokes = HasVisiblePaints(data, "strokes");
            node.HasEffects = HasNonEmptyList(data, "effects");
            node.HasTextCharacters = !string.IsNullOrEmpty(GetString(data, "characters"));

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
