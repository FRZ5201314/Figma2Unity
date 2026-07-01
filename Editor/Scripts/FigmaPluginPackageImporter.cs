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
            FigmaPluginPackageDto package = JsonUtility.FromJson<FigmaPluginPackageDto>(File.ReadAllText(packagePath));
            ValidatePackage(package);

            string fileKey = string.IsNullOrWhiteSpace(package.manifest.fileKey)
                ? package.fileKey
                : package.manifest.fileKey;
            if (string.IsNullOrWhiteSpace(fileKey))
            {
                fileKey = "figma-plugin";
            }

            package.manifest.fileKey = fileKey;
            package.manifest.imageScale = package.manifest.imageScale > 0.01f
                ? package.manifest.imageScale
                : Mathf.Max(0.01f, package.imageScale);
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

                byte[] bytes = Convert.FromBase64String(StripDataUrlPrefix(asset.dataBase64));
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
            public string image;
            public BoundsDto bounds;
            public UnityRectDto unity;
            public int siblingIndex;
            public string unityObjectPath;
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
