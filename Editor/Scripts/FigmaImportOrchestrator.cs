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
        private readonly FigmaImportDiagnosticService _diagnosticService;
        private readonly FigmaPrefabSaver _prefabSaver;

        public FigmaImportOrchestrator(FigmaApiClient client)
        {
            _client = client;
            UnitySpriteAssetImporter spriteImporter = new UnitySpriteAssetImporter();
            _imageExporter = new FigmaImageExporter(client, spriteImporter);
            _sceneBuilder = new FigmaSceneBuilder();
            _manifestService = new FigmaImportManifestService();
            _diagnosticService = new FigmaImportDiagnosticService();
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
            if (settings.CreateReference && selectedRoot.IsPage)
            {
                progress.Report(new FigmaImportProgress("Page 导入跳过整页参考图，避免下载超大页面图片...", 0.24f));
            }
            else if (settings.CreateReference)
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
            FigmaImportPlan importPlan = FigmaDocumentParser.CreateImportPlan(
                selectedRoot,
                settings.SliceMode,
                settings.ContainerSliceStrategy);
            List<FigmaNodeInfo> importableNodes = importPlan.Nodes;

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
            List<ImportedLayerInfo> layers = BuildLayerInfos(settings, selectedRoot, importableNodes, layerAssetPaths);

            progress.Report(new FigmaImportProgress("更新全局字体映射...", 0.9f));
            FigmaFontMappingAsset fontMapping = FigmaFontMappingService.EnsureSettingsUseGlobalMapping(settings);
            FontMappingUpdateResult fontMappingResult = FigmaFontMappingService.UpdateFromLayers(fontMapping, layers);

            progress.Report(new FigmaImportProgress("生成场景对象...", 0.92f));
            GameObject rootObject = _sceneBuilder.BuildSceneObjects(settings, selectedRoot, referenceAssetPath, layers);

            progress.Report(new FigmaImportProgress("保存 manifest...", 0.97f));
            string manifestPath = _manifestService.SaveManifest(settings, selectedRoot, outputFolder, referenceAssetPath, layers);

            progress.Report(new FigmaImportProgress("保存导入诊断报告...", 0.98f));
            string diagnosticReportPath = _diagnosticService.SaveReport(settings, selectedRoot, outputFolder, importPlan, layers);

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
                DiagnosticReportPath = diagnosticReportPath,
                PrefabPath = prefabPath,
                FontMappingPath = fontMappingResult.MappingAssetPath,
                FontMappingTextStyleCount = fontMappingResult.TextStyleCount,
                FontMappingAddedEntries = fontMappingResult.AddedEntries,
                FontMappingUpdatedEntries = fontMappingResult.UpdatedEntries,
                RootObject = rootObject,
                LayerCount = layers.Count
            };
        }

        private static List<ImportedLayerInfo> BuildLayerInfos(
            FigmaImportSettings settings,
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
                bool useTextLayer = ShouldCreateTextLayer(settings, node);
                layer.Kind = useTextLayer ? ImportedLayerKind.Text : ImportedLayerKind.Image;
                layer.TextStyle = IsTextNode(node) ? CopyTextStyle(node.TextStyle) : null;

                Rect sliceBounds = useTextLayer && node.HasUsableBounds
                    ? node.AbsoluteBounds
                    : GetSliceLayoutBounds(node);
                layer.FigmaBounds = sliceBounds;
                layer.AnchoredPosition = FigmaCoordinateUtility.ToUnityAnchoredPosition(rootBounds, sliceBounds);
                layer.SizeDelta = new Vector2(sliceBounds.width, sliceBounds.height);
                layer.SiblingIndex = layers.Count;
                layers.Add(layer);
            }

            return layers;
        }

        private static bool ShouldCreateTextLayer(FigmaImportSettings settings, FigmaNodeInfo node)
        {
            if (settings == null
                || node == null
                || !string.Equals(node.Type, "TEXT", StringComparison.OrdinalIgnoreCase)
                || node.TextStyle == null
                || settings.TextImportMode == FigmaTextImportMode.Image)
            {
                return false;
            }

            if (settings.TextImportMode == FigmaTextImportMode.TextMeshPro)
            {
                return node.TextStyle.HasUsableStyle;
            }

            return node.TextStyle.IsSmartImportSafe;
        }

        private static FigmaTextStyleInfo CopyTextStyle(FigmaTextStyleInfo source)
        {
            if (source == null)
            {
                return null;
            }

            return new FigmaTextStyleInfo
            {
                Characters = source.Characters,
                FontFamily = source.FontFamily,
                FontStyleName = source.FontStyleName,
                FontPostScriptName = source.FontPostScriptName,
                FontWeight = source.FontWeight,
                FontSize = source.FontSize,
                LineHeightPx = source.LineHeightPx,
                LetterSpacing = source.LetterSpacing,
                Color = source.Color,
                HorizontalAlign = source.HorizontalAlign,
                VerticalAlign = source.VerticalAlign,
                AutoResize = source.AutoResize,
                HasMixedStyles = source.HasMixedStyles,
                HasUnsupportedVisuals = source.HasUnsupportedVisuals
            };
        }

        private static Rect GetSliceLayoutBounds(FigmaNodeInfo node)
        {
            if (node == null)
            {
                return new Rect();
            }

            return node.HasAbsoluteRenderBounds ? node.AbsoluteRenderBounds : node.AbsoluteBounds;
        }

        private static bool IsTextNode(FigmaNodeInfo node)
        {
            return node != null && string.Equals(node.Type, "TEXT", StringComparison.OrdinalIgnoreCase);
        }
    }
}
