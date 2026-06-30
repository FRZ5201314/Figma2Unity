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

            progress.Report(new FigmaImportProgress("离线重建完成。", 1f));
            return new FigmaImportResult
            {
                OutputFolder = data.OutputFolder,
                ManifestPath = data.ManifestPath,
                RootObject = rootObject,
                LayerCount = data.Layers.Count
            };
        }
    }
}
