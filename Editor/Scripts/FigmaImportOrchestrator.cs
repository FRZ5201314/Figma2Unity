using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FigmaUiImporter.Editor
{
    internal sealed class FigmaImportOrchestrator
    {
        private readonly FigmaApiClient _client;
        private readonly FigmaImageExporter _imageExporter;
        private readonly FigmaSceneBuilder _sceneBuilder;
        private readonly FigmaImportManifestService _manifestService;
        private readonly FigmaPrefabSaver _prefabSaver;

        public FigmaImportOrchestrator(FigmaApiClient client)
        {
            _client = client;
            UnitySpriteAssetImporter spriteImporter = new UnitySpriteAssetImporter();
            _imageExporter = new FigmaImageExporter(client, spriteImporter);
            _sceneBuilder = new FigmaSceneBuilder();
            _manifestService = new FigmaImportManifestService();
            _prefabSaver = new FigmaPrefabSaver();
        }

        public async Task<FigmaImportResult> ImportAsync(
            FigmaImportSettings settings,
            FigmaNodeInfo selectedNode,
            IProgress<FigmaImportProgress> progress,
            CancellationToken cancellationToken)
        {
            progress.Report(new FigmaImportProgress("读取所选节点...", 0.08f));
            FigmaNodeInfo selectedRoot = null;
            if (settings.UseLoadedNodeTree && selectedNode != null)
            {
                selectedRoot = selectedNode;
                progress.Report(new FigmaImportProgress("使用已加载的节点结构，跳过节点 API 请求...", 0.1f));
            }
            else
            {
                selectedRoot = await _client.LoadNodeTreeAsync(settings.FileKey, selectedNode.Id, cancellationToken);
            }

            FigmaDocumentParser.EnsureBoundsFromChildren(selectedRoot);

            if (!selectedRoot.HasUsableBounds)
            {
                throw new System.InvalidOperationException("所选节点没有可用尺寸，无法导入。");
            }

            string outputFolder = FigmaPathUtility.BuildImportFolder(settings.OutputFolder, settings.FileKey, selectedRoot.Id);
            FigmaPathUtility.EnsureAssetFolder(outputFolder);

            string referenceAssetPath = string.Empty;
            if (settings.CreateReference)
            {
                referenceAssetPath = FigmaPathUtility.CombineAssetPath(outputFolder, "reference.png");
                await _imageExporter.ExportSingleImageAsync(
                    settings,
                    selectedRoot.Id,
                    referenceAssetPath,
                    progress,
                    cancellationToken);
            }

            progress.Report(new FigmaImportProgress("筛选可导入图层...", 0.38f));
            List<FigmaNodeInfo> importableNodes = FigmaDocumentParser.CollectImportableNodes(selectedRoot, settings.SliceMode);

            Dictionary<string, string> layerAssetPaths = new Dictionary<string, string>();
            if (settings.SliceMode != FigmaSliceMode.FlattenSelectedFrameAsSingleImage && importableNodes.Count > 0)
            {
                string layersFolder = FigmaPathUtility.CombineAssetPath(outputFolder, "layers");
                layerAssetPaths = await _imageExporter.ExportLayerImagesAsync(
                    settings,
                    importableNodes,
                    layersFolder,
                    progress,
                    cancellationToken);
            }

            progress.Report(new FigmaImportProgress("计算 Unity 布局...", 0.88f));
            List<ImportedLayerInfo> layers = BuildLayerInfos(selectedRoot, importableNodes, layerAssetPaths);

            progress.Report(new FigmaImportProgress("生成场景对象...", 0.92f));
            GameObject rootObject = _sceneBuilder.BuildSceneObjects(settings, selectedRoot, referenceAssetPath, layers);

            progress.Report(new FigmaImportProgress("保存 manifest...", 0.97f));
            string manifestPath = _manifestService.SaveManifest(settings, selectedRoot, outputFolder, referenceAssetPath, layers);

            string prefabPath = string.Empty;
            if (settings.SavePrefab)
            {
                progress.Report(new FigmaImportProgress("保存 Prefab...", 0.99f));
                prefabPath = _prefabSaver.SavePrefab(settings, selectedRoot, rootObject);
            }

            progress.Report(new FigmaImportProgress("导入完成。", 1f));
            return new FigmaImportResult
            {
                OutputFolder = outputFolder,
                ManifestPath = manifestPath,
                PrefabPath = prefabPath,
                RootObject = rootObject,
                LayerCount = layers.Count
            };
        }

        private static List<ImportedLayerInfo> BuildLayerInfos(
            FigmaNodeInfo selectedRoot,
            IList<FigmaNodeInfo> nodes,
            IDictionary<string, string> assetPaths)
        {
            List<ImportedLayerInfo> layers = new List<ImportedLayerInfo>();
            Rect rootBounds = selectedRoot.AbsoluteBounds;

            for (int i = 0; i < nodes.Count; i++)
            {
                FigmaNodeInfo node = nodes[i];
                string assetPath;
                if (!assetPaths.TryGetValue(node.Id, out assetPath))
                {
                    continue;
                }

                ImportedLayerInfo layer = new ImportedLayerInfo();
                layer.NodeId = node.Id;
                layer.NodeName = node.Name;
                layer.NodeType = node.Type;
                layer.AssetPath = assetPath;
                Rect sliceBounds = GetSliceLayoutBounds(node);
                layer.FigmaBounds = sliceBounds;
                layer.AnchoredPosition = FigmaCoordinateUtility.ToUnityAnchoredPosition(rootBounds, sliceBounds);
                layer.SizeDelta = new Vector2(sliceBounds.width, sliceBounds.height);
                layer.SiblingIndex = layers.Count;
                layers.Add(layer);
            }

            return layers;
        }

        private static Rect GetSliceLayoutBounds(FigmaNodeInfo node)
        {
            if (node == null)
            {
                return new Rect();
            }

            return node.HasAbsoluteRenderBounds ? node.AbsoluteRenderBounds : node.AbsoluteBounds;
        }
    }
}
