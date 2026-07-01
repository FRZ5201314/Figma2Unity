using System;
using System.Collections.Generic;

namespace FigmaUiImporter.Editor
{
    internal static class FigmaLayerMaskUtility
    {
        public static Dictionary<string, FigmaNodeInfo> ToNodeMap(IList<FigmaNodeInfo> nodes)
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

        public static List<string> CollectNearestSelectedDescendantIds(
            FigmaNodeInfo node,
            IDictionary<string, FigmaNodeInfo> selectedMap)
        {
            List<string> results = new List<string>();
            CollectNearestSelectedDescendantIds(node, selectedMap, results);
            return results;
        }

        private static void CollectNearestSelectedDescendantIds(
            FigmaNodeInfo node,
            IDictionary<string, FigmaNodeInfo> selectedMap,
            IList<string> results)
        {
            if (node == null || selectedMap == null || selectedMap.Count == 0)
            {
                return;
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                FigmaNodeInfo child = node.Children[i];
                if (child == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(child.Id) && selectedMap.ContainsKey(child.Id))
                {
                    results.Add(child.Id);
                    continue;
                }

                CollectNearestSelectedDescendantIds(child, selectedMap, results);
            }
        }
    }
}
