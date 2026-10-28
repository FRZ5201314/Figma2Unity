using System;
using System.Collections.Generic;
using UnityEngine;
using static FigmaUiImporter.Editor.FigmaLocalization;

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

            progress.Report(new FigmaImportProgress(L("读取 manifest...", "Reading manifest..."), 0.12f));
            FigmaManifestRebuildData data = _manifestService.LoadForRebuild(manifestPath);
            if (data.RootNode == null || !data.RootNode.HasUsableBounds)
            {
                throw new InvalidOperationException(L("manifest 中的根节点尺寸无效，无法重建场景。", "The root size in the manifest is invalid; the scene cannot be rebuilt."));
            }

            settings.FileKey = data.FileKey;
            settings.ImageScale = data.ImageScale;
            settings.OutputFolder = data.OutputFolder;

            progress.Report(new FigmaImportProgress(L("更新全局字体映射...", "Updating global font mapping..."), 0.24f));
            FigmaFontMappingAsset fontMapping = FigmaFontMappingService.EnsureSettingsUseGlobalMapping(settings);
            FontMappingUpdateResult fontMappingResult = FigmaFontMappingService.UpdateFromLayers(fontMapping, data.Layers);

            progress.Report(new FigmaImportProgress(L("导入 Sprite 资源...", "Importing Sprite assets..."), 0.38f));
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

            progress.Report(new FigmaImportProgress(L("生成场景对象...", "Building scene objects..."), 0.78f));
            GameObject rootObject = _sceneBuilder.BuildSceneObjects(
                settings,
                data.RootNode,
                referenceAssetPath,
                data.Layers);

            progress.Report(new FigmaImportProgress(L("保存导入诊断报告...", "Saving diagnostics..."), 0.88f));
            string diagnosticReportPath = _diagnosticService.SaveOfflineReport(settings, data);

            string prefabPath = string.Empty;
            if (settings.SavePrefab)
            {
                progress.Report(new FigmaImportProgress(L("保存 Prefab...", "Saving Prefab..."), 0.92f));
                prefabPath = _prefabSaver.SavePrefab(settings, data.RootNode, rootObject);
            }

            progress.Report(new FigmaImportProgress(L("离线重建完成。", "Offline rebuild complete."), 1f));
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
