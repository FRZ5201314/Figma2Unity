using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using static FigmaUiImporter.Editor.FigmaLocalization;

namespace FigmaUiImporter.Editor
{
    internal sealed class FigmaPluginPackageImporter
    {
        private const int SupportedSchemaVersion = 1;

        public FigmaPluginPackageInfo ReadPackageInfo(string packagePath)
        {
            FigmaPluginPackageDto package = LoadPackage(packagePath);
            ValidatePackage(package);
            ValidateAssetReferences(package);
            return BuildPackageInfo(packagePath, package);
        }

        public string ImportPackage(
            string packagePath,
            FigmaImportSettings settings,
            IProgress<FigmaImportProgress> progress)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            {
                throw new FileNotFoundException(F("未找到 Figma 插件导出包：{0}", "Figma plugin package not found: {0}", packagePath));
            }

            progress.Report(new FigmaImportProgress(L("读取 Figma 插件包...", "Reading Figma plugin package..."), 0.06f));
            FigmaPluginPackageDto package = LoadPackage(packagePath);
            ValidatePackage(package);
            ValidateAssetReferences(package);

            string fileKey = ResolveFileKey(package);

            package.manifest.fileKey = fileKey;
            package.manifest.imageScale = ResolveImageScale(package);
            package.manifest.importedAt = DateTime.UtcNow.ToString("o");

            string outputFolder = FigmaPathUtility.BuildImportFolder(
                settings.OutputFolder,
                fileKey,
                package.manifest.selectedNodeId);
            if (!settings.OverwriteExisting && FigmaPathUtility.AssetFolderHasContents(outputFolder))
            {
                throw new InvalidOperationException(F("输出目录已存在导入内容：{0}。请勾选“覆盖同名场景根节点 / Prefab”，或更换输出资源目录后再导入。", "The output folder already contains imported content: {0}. Enable \"Overwrite scene root / Prefab\" or choose another output folder.", outputFolder));
            }

            FigmaPathUtility.EnsureAssetFolder(outputFolder);

            progress.Report(new FigmaImportProgress(L("写入插件包图片资源...", "Writing package image assets..."), 0.22f));
            WriteAssets(package.assets, outputFolder);

            progress.Report(new FigmaImportProgress(L("写入 manifest...", "Writing manifest..."), 0.32f));
            string manifestPath = FigmaPathUtility.CombineAssetPath(outputFolder, "manifest.json");
            string manifestFullPath = FigmaPathUtility.ToFullPath(manifestPath);
            string manifestDirectory = Path.GetDirectoryName(manifestFullPath);
            if (!Directory.Exists(manifestDirectory))
            {
                Directory.CreateDirectory(manifestDirectory);
            }

            File.WriteAllText(manifestFullPath, JsonUtility.ToJson(package.manifest, true));
            AssetDatabase.Refresh();
            return manifestPath;
        }

        private static FigmaPluginPackageDto LoadPackage(string packagePath)
        {
            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            {
                throw new FileNotFoundException(F("未找到 Figma 插件导出包：{0}", "Figma plugin package not found: {0}", packagePath));
            }

            string json = File.ReadAllText(packagePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException(L("Figma 插件包为空，无法导入。", "The Figma plugin package is empty and cannot be imported."));
            }

            try
            {
                return JsonUtility.FromJson<FigmaPluginPackageDto>(json);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(L("Figma 插件包 JSON 无法解析，请确认选择的是 .figma2unity.json 文件。", "The Figma plugin package JSON could not be parsed. Make sure you selected a .figma2unity.json file."), exception);
            }
        }

        private static void ValidatePackage(FigmaPluginPackageDto package)
        {
            if (package == null)
            {
                throw new InvalidOperationException(L("Figma 插件包内容无效，无法解析 JSON。", "The Figma plugin package is invalid; JSON parsing failed."));
            }

            if (package.schemaVersion != SupportedSchemaVersion)
            {
                throw new InvalidOperationException(
                    F("不支持的 Figma 插件包版本：{0}。当前支持版本：{1}。", "Unsupported Figma plugin package version: {0}. Supported version: {1}.", package.schemaVersion, SupportedSchemaVersion));
            }

            if (package.manifest == null)
            {
                throw new InvalidOperationException(L("Figma 插件包缺少 manifest。", "The Figma plugin package is missing its manifest."));
            }

            if (string.IsNullOrWhiteSpace(package.manifest.selectedNodeId))
            {
                throw new InvalidOperationException(L("Figma 插件包 manifest 缺少 selectedNodeId。", "The Figma plugin package manifest is missing selectedNodeId."));
            }

            if (package.manifest.rootBounds == null
                || package.manifest.rootBounds.width <= 0.01f
                || package.manifest.rootBounds.height <= 0.01f)
            {
                throw new InvalidOperationException(L("Figma 插件包 manifest 中的根节点尺寸无效。", "The Figma plugin package manifest has an invalid root size."));
            }

            if (package.assets == null || package.assets.Count == 0)
            {
                throw new InvalidOperationException(L("Figma 插件包没有包含任何图片资源。", "The Figma plugin package contains no image assets."));
            }

            if (package.manifest.layers != null)
            {
                for (int i = 0; i < package.manifest.layers.Count; i++)
                {
                    LayerDto layer = package.manifest.layers[i];
                    if (layer == null)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(layer.image))
                    {
                        throw new InvalidOperationException(F("Figma 插件包 manifest 中的图层缺少 image：{0}", "Figma plugin package layer is missing its image: {0}", GetLayerLabel(layer)));
                    }

                    if (layer.bounds == null || layer.bounds.width <= 0.01f || layer.bounds.height <= 0.01f)
                    {
                        throw new InvalidOperationException(F("Figma 插件包 manifest 中的图层尺寸无效：{0}", "Figma plugin package layer has an invalid size: {0}", GetLayerLabel(layer)));
                    }
                }
            }
        }

        private static void ValidateAssetReferences(FigmaPluginPackageDto package)
        {
            HashSet<string> assetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < package.assets.Count; i++)
            {
                FigmaPluginAssetDto asset = package.assets[i];
                if (asset == null)
                {
                    continue;
                }

                string relativePath = NormalizePackageRelativePath(asset.path);
                if (string.IsNullOrWhiteSpace(asset.dataBase64))
                {
                    throw new InvalidOperationException(F("插件包图片缺少 base64 数据：{0}", "Package image is missing base64 data: {0}", relativePath));
                }

                assetPaths.Add(relativePath);
            }

            if (!string.IsNullOrWhiteSpace(package.manifest.referenceImage))
            {
                string referencePath = NormalizePackageRelativePath(package.manifest.referenceImage);
                if (!assetPaths.Contains(referencePath))
                {
                    throw new InvalidOperationException(F("Figma 插件包 manifest 引用的参考图资源不存在：{0}", "The reference image referenced by the Figma plugin package manifest does not exist: {0}", referencePath));
                }
            }

            if (package.manifest.layers == null)
            {
                return;
            }

            for (int i = 0; i < package.manifest.layers.Count; i++)
            {
                LayerDto layer = package.manifest.layers[i];
                if (layer == null)
                {
                    continue;
                }

                string layerPath = NormalizePackageRelativePath(layer.image);
                if (!assetPaths.Contains(layerPath))
                {
                    throw new InvalidOperationException(F("Figma 插件包 manifest 引用的切图资源不存在：{0}（{1}）", "The slice asset referenced by the Figma plugin package manifest does not exist: {0} ({1})", layerPath, GetLayerLabel(layer)));
                }
            }
        }

        private static void WriteAssets(IList<FigmaPluginAssetDto> assets, string outputFolder)
        {
            HashSet<string> writtenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < assets.Count; i++)
            {
                FigmaPluginAssetDto asset = assets[i];
                if (asset == null)
                {
                    continue;
                }

                string relativePath = NormalizePackageRelativePath(asset.path);
                if (!writtenPaths.Add(relativePath))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(asset.dataBase64))
                {
                    throw new InvalidOperationException(F("插件包图片缺少 base64 数据：{0}", "Package image is missing base64 data: {0}", relativePath));
                }

                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(StripDataUrlPrefix(asset.dataBase64));
                }
                catch (FormatException exception)
                {
                    throw new InvalidOperationException(F("插件包图片 base64 数据损坏：{0}", "Package image has corrupted base64 data: {0}", relativePath), exception);
                }

                string assetPath = FigmaPathUtility.CombineAssetPath(outputFolder, relativePath);
                string fullPath = FigmaPathUtility.ToFullPath(assetPath);
                string directory = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(fullPath, bytes);
            }
        }

        private static string StripDataUrlPrefix(string base64)
        {
            string trimmed = base64.Trim();
            if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int commaIndex = trimmed.IndexOf(",", StringComparison.Ordinal);
                if (commaIndex >= 0 && commaIndex < trimmed.Length - 1)
                {
                    return trimmed.Substring(commaIndex + 1);
                }
            }

            return trimmed;
        }

        private static FigmaPluginPackageInfo BuildPackageInfo(string packagePath, FigmaPluginPackageDto package)
        {
            FileInfo fileInfo = new FileInfo(packagePath);
            FigmaPluginPackageInfo info = new FigmaPluginPackageInfo();
            info.PackagePath = packagePath;
            info.PackageFileName = fileInfo.Name;
            info.PackageSizeBytes = fileInfo.Length;
            info.SchemaVersion = package.schemaVersion;
            info.ExportedAt = package.exportedAt;
            info.FileKey = ResolveFileKey(package);
            info.ImageScale = ResolveImageScale(package);
            info.SelectedNodeId = package.manifest.selectedNodeId;
            info.SelectedNodeName = package.manifest.selectedNodeName;
            info.SelectedNodeType = string.IsNullOrWhiteSpace(package.manifest.selectedNodeType)
                ? "FRAME"
                : package.manifest.selectedNodeType;
            info.ContainerSliceStrategy = package.manifest.containerSliceStrategy;
            info.TextImportMode = package.manifest.textImportMode;
            info.RootWidth = package.manifest.rootBounds.width;
            info.RootHeight = package.manifest.rootBounds.height;
            info.LayerCount = package.manifest.layers == null ? 0 : package.manifest.layers.Count;
            info.AssetCount = package.assets == null ? 0 : package.assets.Count;
            info.HasReferenceImage = !string.IsNullOrWhiteSpace(package.manifest.referenceImage);
            return info;
        }

        private static string ResolveFileKey(FigmaPluginPackageDto package)
        {
            string fileKey = string.IsNullOrWhiteSpace(package.manifest.fileKey)
                ? package.fileKey
                : package.manifest.fileKey;
            return string.IsNullOrWhiteSpace(fileKey) ? "figma-plugin" : fileKey;
        }

        private static float ResolveImageScale(FigmaPluginPackageDto package)
        {
            return package.manifest.imageScale > 0.01f
                ? package.manifest.imageScale
                : Mathf.Max(0.01f, package.imageScale);
        }

        private static string GetLayerLabel(LayerDto layer)
        {
            if (layer == null)
            {
                return L("(空图层)", "(empty layer)");
            }

            if (!string.IsNullOrEmpty(layer.nodeName) && !string.IsNullOrEmpty(layer.nodeId))
            {
                return layer.nodeName + " [" + layer.nodeId + "]";
            }

            if (!string.IsNullOrEmpty(layer.nodeName))
            {
                return layer.nodeName;
            }

            return string.IsNullOrEmpty(layer.nodeId) ? L("(未命名图层)", "(unnamed layer)") : layer.nodeId;
        }

        private static string NormalizePackageRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException(L("插件包图片路径为空。", "The package image path is empty."));
            }

            string normalized = path.Trim().Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(normalized)
                || normalized.IndexOf(":", StringComparison.Ordinal) >= 0
                || normalized.StartsWith("/", StringComparison.Ordinal)
                || normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                || normalized.IndexOf("../", StringComparison.Ordinal) >= 0
                || normalized.IndexOf("/..", StringComparison.Ordinal) >= 0
                || string.Equals(normalized, "..", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(F("插件包图片路径无效：{0}", "Invalid package image path: {0}", path));
            }

            return normalized;
        }

        [Serializable]
        private sealed class FigmaPluginPackageDto
        {
            public int schemaVersion;
            public string exportedAt;
            public string fileKey;
            public float imageScale;
            public ManifestDto manifest;
            public List<FigmaPluginAssetDto> assets;
        }

        [Serializable]
        private sealed class FigmaPluginAssetDto
        {
            public string path;
            public string mimeType;
            public string dataBase64;
        }

        [Serializable]
        private sealed class ManifestDto
        {
            public string fileKey;
            public float imageScale;
            public string selectedNodeId;
            public string selectedNodeName;
            public string selectedNodeType;
            public bool clipsContent;
            public int clipsContentVersion;
            public string containerSliceStrategy;
            public string textImportMode;
            public string importedAt;
            public BoundsDto rootBounds;
            public string referenceImage;
            public List<LayerDto> layers;
        }

        [Serializable]
        private sealed class LayerDto
        {
            public string nodeId;
            public string nodeName;
            public string type;
            public string kind;
            public string image;
            public TextDto text;
            public BoundsDto bounds;
            public UnityRectDto unity;
            public int siblingIndex;
            public string unityObjectPath;
        }

        [Serializable]
        private sealed class TextDto
        {
            public string characters;
            public string fontFamily;
            public string fontStyleName;
            public string fontPostScriptName;
            public int fontWeight;
            public float fontSize;
            public float lineHeightPx;
            public float letterSpacing;
            public ColorDto color;
            public string horizontalAlign;
            public string verticalAlign;
            public string autoResize;
            public bool hasMixedStyles;
            public bool hasUnsupportedVisuals;
        }

        [Serializable]
        private sealed class ColorDto
        {
            public float r;
            public float g;
            public float b;
            public float a;
        }

        [Serializable]
        private sealed class BoundsDto
        {
            public float x;
            public float y;
            public float width;
            public float height;
        }

        [Serializable]
        private sealed class UnityRectDto
        {
            public VectorDto anchoredPosition;
            public VectorDto sizeDelta;
        }

        [Serializable]
        private sealed class VectorDto
        {
            public float x;
            public float y;
        }
    }
}
