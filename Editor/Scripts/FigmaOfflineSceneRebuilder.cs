using System;
using System.Collections.Generic;
using UnityEngine;

namespace FigmaUiImporter.Editor
{
    internal sealed class FigmaOfflineSceneRebuilder
    {
        private readonly FigmaImportManifestService _manifestService = new FigmaImportManifestService();
        private readonly UnitySpriteAssetImporter _spriteImporter = new UnitySpriteAssetImporter();
        private readonly FigmaSceneBuilder _sceneBuilder = new FigmaSceneBuilder();
        private readonly FigmaImportDiagnosticService _diagnosticService = new FigmaImportDiagnosticService();
        private readonly FigmaPrefabSaver _prefabSaver = new FigmaPrefabSaver();

        public FigmaImportResult Rebuild(
            FigmaImportSettings settings,
            string manifestPath,
            IProgress<FigmaImportProgress> progress)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            progress.Report(new FigmaImportProgress("读取 manifest...", 0.12f));
            FigmaManifestRebuildData data = _manifestService.LoadForRebuild(manifestPath);
            if (data.RootNode == null || !data.RootNode.HasUsableBounds)
            {
                throw new InvalidOperationException("manifest 中的根节点尺寸无效，无法重建场景。");
            }

            settings.FileKey = data.FileKey;
            settings.ImageScale = data.ImageScale;
            settings.OutputFolder = data.OutputFolder;

            progress.Report(new FigmaImportProgress("更新全局字体映射...", 0.24f));
            FigmaFontMappingAsset fontMapping = FigmaFontMappingService.EnsureSettingsUseGlobalMapping(settings);
            FontMappingUpdateResult fontMappingResult = FigmaFontMappingService.UpdateFromLayers(fontMapping, data.Layers);

            progress.Report(new FigmaImportProgress("导入 Sprite 资源...", 0.38f));
            string referenceAssetPath = settings.CreateReference ? data.ReferenceAssetPath : string.Empty;
            List<string> spritePaths = new List<string>();
            if (!string.IsNullOrEmpty(referenceAssetPath))
            {
                spritePaths.Add(referenceAssetPath);
            }

            for (int i = 0; i < data.Layers.Count; i++)
            {
                if (!string.IsNullOrEmpty(data.Layers[i].AssetPath))
                {
                    spritePaths.Add(data.Layers[i].AssetPath);
                }
            }

            _spriteImporter.ImportSprites(spritePaths, settings.CompressionMode);

            progress.Report(new FigmaImportProgress("生成场景对象...", 0.78f));
            GameObject rootObject = _sceneBuilder.BuildSceneObjects(
                settings,
                data.RootNode,
                referenceAssetPath,
                data.Layers);

            progress.Report(new FigmaImportProgress("保存导入诊断报告...", 0.88f));
            string diagnosticReportPath = _diagnosticService.SaveOfflineReport(settings, data);

            string prefabPath = string.Empty;
            if (settings.SavePrefab)
            {
                progress.Report(new FigmaImportProgress("保存 Prefab...", 0.92f));
                prefabPath = _prefabSaver.SavePrefab(settings, data.RootNode, rootObject);
            }

            progress.Report(new FigmaImportProgress("离线重建完成。", 1f));
            return new FigmaImportResult
            {
                OutputFolder = data.OutputFolder,
                ManifestPath = data.ManifestPath,
                DiagnosticReportPath = diagnosticReportPath,
                PrefabPath = prefabPath,
                FontMappingPath = fontMappingResult.MappingAssetPath,
                FontMappingTextStyleCount = fontMappingResult.TextStyleCount,
                FontMappingAddedEntries = fontMappingResult.AddedEntries,
                FontMappingUpdatedEntries = fontMappingResult.UpdatedEntries,
                RootObject = rootObject,
                LayerCount = data.Layers.Count
            };
        }
    }
}
