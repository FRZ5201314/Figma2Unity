using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FigmaUiImporter.Editor
{
    internal sealed class FigmaImportDiagnosticService
    {
        public string SaveReport(
            FigmaImportSettings settings,
            FigmaNodeInfo selectedRoot,
            string outputFolder,
            FigmaImportPlan plan,
            IList<ImportedLayerInfo> layers)
        {
            if (settings == null || selectedRoot == null || plan == null)
            {
                return string.Empty;
            }

            ReportDto report = CreateBaseReport(settings, selectedRoot);
            report.source = "Figma API";
            report.exportedLayerCount = layers == null ? 0 : layers.Count;
            Dictionary<string, ImportedLayerInfo> layerMap = ToLayerMap(layers);

            for (int i = 0; i < plan.Diagnostics.Count; i++)
            {
                FigmaImportDiagnosticEntry diagnostic = plan.Diagnostics[i];
                NodeDto node = new NodeDto();
                node.nodeId = diagnostic.NodeId;
                node.nodeName = diagnostic.NodeName;
                node.type = diagnostic.NodeType;
                node.layoutMode = diagnostic.LayoutMode;
                node.action = diagnostic.Action;
                node.reason = diagnostic.Reason;

                ImportedLayerInfo layer;
                if (!string.IsNullOrEmpty(diagnostic.NodeId) && layerMap.TryGetValue(diagnostic.NodeId, out layer))
                {
                    node.kind = layer.IsTextLayer ? "text" : "image";
                    node.assetPath = layer.AssetPath;
                    node.unityObjectPath = layer.UnityObjectPath;
                }

                report.nodes.Add(node);
            }

            return WriteReport(outputFolder, report);
        }

        public string SaveOfflineReport(FigmaImportSettings settings, FigmaManifestRebuildData data)
        {
            if (settings == null || data == null || data.RootNode == null)
            {
                return string.Empty;
            }

            ReportDto report = CreateBaseReport(settings, data.RootNode);
            report.source = "Manifest";
            if (!string.IsNullOrEmpty(data.ContainerSliceStrategy))
            {
                report.containerSliceStrategy = data.ContainerSliceStrategy;
            }

            report.exportedLayerCount = data.Layers.Count;

            for (int i = 0; i < data.Layers.Count; i++)
            {
                ImportedLayerInfo layer = data.Layers[i];
                NodeDto node = new NodeDto();
                node.nodeId = layer.NodeId;
                node.nodeName = layer.NodeName;
                node.type = layer.NodeType;
                node.action = "导出";
                node.reason = "来自 manifest 的切图记录。";
                node.kind = layer.IsTextLayer ? "text" : "image";
                node.assetPath = layer.AssetPath;
                node.unityObjectPath = layer.UnityObjectPath;
                report.nodes.Add(node);
            }

            return WriteReport(data.OutputFolder, report);
        }

        private static ReportDto CreateBaseReport(FigmaImportSettings settings, FigmaNodeInfo selectedRoot)
        {
            ReportDto report = new ReportDto();
            report.generatedAt = DateTime.UtcNow.ToString("o");
            report.fileKey = settings.FileKey;
            report.selectedNodeId = selectedRoot.Id;
            report.selectedNodeName = selectedRoot.Name;
            report.selectedNodeType = selectedRoot.Type;
            report.sliceMode = settings.SliceMode.ToString();
            report.containerSliceStrategy = settings.ContainerSliceStrategy.ToString();
            report.textImportMode = settings.TextImportMode.ToString();
            return report;
        }

        private static string WriteReport(string outputFolder, ReportDto report)
        {
            string reportPath = FigmaPathUtility.CombineAssetPath(outputFolder, "diagnostics.json");
            string fullPath = FigmaPathUtility.ToFullPath(reportPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, JsonUtility.ToJson(report, true));
            AssetDatabase.Refresh();
            return reportPath;
        }

        private static Dictionary<string, ImportedLayerInfo> ToLayerMap(IList<ImportedLayerInfo> layers)
        {
            Dictionary<string, ImportedLayerInfo> map = new Dictionary<string, ImportedLayerInfo>();
            if (layers == null)
            {
                return map;
            }

            for (int i = 0; i < layers.Count; i++)
            {
                ImportedLayerInfo layer = layers[i];
                if (layer != null && !string.IsNullOrEmpty(layer.NodeId) && !map.ContainsKey(layer.NodeId))
                {
                    map.Add(layer.NodeId, layer);
                }
            }

            return map;
        }

        [Serializable]
        private sealed class ReportDto
        {
            public string generatedAt;
            public string source;
            public string fileKey;
            public string selectedNodeId;
            public string selectedNodeName;
            public string selectedNodeType;
            public string sliceMode;
            public string containerSliceStrategy;
            public string textImportMode;
            public int exportedLayerCount;
            public List<NodeDto> nodes = new List<NodeDto>();
        }

        [Serializable]
        private sealed class NodeDto
        {
            public string nodeId;
            public string nodeName;
            public string type;
            public string layoutMode;
            public string action;
            public string reason;
            public string kind;
            public string assetPath;
            public string unityObjectPath;
        }
    }
}
