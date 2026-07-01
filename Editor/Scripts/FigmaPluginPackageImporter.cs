using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

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
                throw new FileNotFoundException("未找到 Figma 插件导出包：" + packagePath);
            }

            progress.Report(new FigmaImportProgress("读取 Figma 插件包...", 0.06f));
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
            FigmaPathUtility.EnsureAssetFolder(outputFolder);

            progress.Report(new FigmaImportProgress("写入插件包图片资源...", 0.22f));
            WriteAssets(package.assets, outputFolder);

            progress.Report(new FigmaImportProgress("写入 manifest...", 0.32f));
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
                throw new FileNotFoundException("未找到 Figma 插件导出包：" + packagePath);
            }

            string json = File.ReadAllText(packagePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("Figma 插件包为空，无法导入。");
            }

            try
            {
                return JsonUtility.FromJson<FigmaPluginPackageDto>(json);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Figma 插件包 JSON 无法解析，请确认选择的是 .figma2unity.json 文件。", exception);
            }
        }

        private static void ValidatePackage(FigmaPluginPackageDto package)
        {
            if (package == null)
            {
                throw new InvalidOperationException("Figma 插件包内容无效，无法解析 JSON。");
            }

            if (package.schemaVersion != SupportedSchemaVersion)
            {
                throw new InvalidOperationException(
                    "不支持的 Figma 插件包版本：" + package.schemaVersion + "。当前支持版本：" + SupportedSchemaVersion + "。");
            }

            if (package.manifest == null)
            {
                throw new InvalidOperationException("Figma 插件包缺少 manifest。");
            }

            if (string.IsNullOrWhiteSpace(package.manifest.selectedNodeId))
            {
                throw new InvalidOperationException("Figma 插件包 manifest 缺少 selectedNodeId。");
            }

            if (package.manifest.rootBounds == null
                || package.manifest.rootBounds.width <= 0.01f
                || package.manifest.rootBounds.height <= 0.01f)
            {
                throw new InvalidOperationException("Figma 插件包 manifest 中的根节点尺寸无效。");
            }

            if (package.assets == null || package.assets.Count == 0)
            {
                throw new InvalidOperationException("Figma 插件包没有包含任何图片资源。");
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
                        throw new InvalidOperationException("Figma 插件包 manifest 中的图层缺少 image：" + GetLayerLabel(layer));
                    }

                    if (layer.bounds == null || layer.bounds.width <= 0.01f || layer.bounds.height <= 0.01f)
                    {
                        throw new InvalidOperationException("Figma 插件包 manifest 中的图层尺寸无效：" + GetLayerLabel(layer));
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
                    throw new InvalidOperationException("插件包图片缺少 base64 数据：" + relativePath);
                }

                assetPaths.Add(relativePath);
            }

            if (!string.IsNullOrWhiteSpace(package.manifest.referenceImage))
            {
                string referencePath = NormalizePackageRelativePath(package.manifest.referenceImage);
                if (!assetPaths.Contains(referencePath))
                {
                    throw new InvalidOperationException("Figma 插件包 manifest 引用的参考图资源不存在：" + referencePath);
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
                    throw new InvalidOperationException("Figma 插件包 manifest 引用的切图资源不存在：" + layerPath + "（" + GetLayerLabel(layer) + "）");
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
                    throw new InvalidOperationException("插件包图片缺少 base64 数据：" + relativePath);
                }

                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(StripDataUrlPrefix(asset.dataBase64));
                }
                catch (FormatException exception)
                {
                    throw new InvalidOperationException("插件包图片 base64 数据损坏：" + relativePath, exception);
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
                return "(空图层)";
            }

            if (!string.IsNullOrEmpty(layer.nodeName) && !string.IsNullOrEmpty(layer.nodeId))
            {
                return layer.nodeName + " [" + layer.nodeId + "]";
            }

            if (!string.IsNullOrEmpty(layer.nodeName))
            {
                return layer.nodeName;
            }

            return string.IsNullOrEmpty(layer.nodeId) ? "(未命名图层)" : layer.nodeId;
        }

        private static string NormalizePackageRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("插件包图片路径为空。");
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
                throw new InvalidOperationException("插件包图片路径无效：" + path);
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
