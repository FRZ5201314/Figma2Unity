using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FigmaUiImporter.Editor
{
    internal sealed class FigmaImportManifestService
    {
        public string SaveManifest(
            FigmaImportSettings settings,
            FigmaNodeInfo selectedRoot,
            string outputFolder,
            string referenceAssetPath,
            IList<ImportedLayerInfo> layers)
        {
            ManifestDto manifest = new ManifestDto();
            manifest.fileKey = settings.FileKey;
            manifest.imageScale = settings.ImageScale;
            manifest.selectedNodeId = selectedRoot.Id;
            manifest.selectedNodeName = selectedRoot.Name;
            manifest.selectedNodeType = selectedRoot.Type;
            manifest.containerSliceStrategy = settings.ContainerSliceStrategy.ToString();
            manifest.importedAt = DateTime.UtcNow.ToString("o");
            manifest.rootBounds = ToBoundsDto(selectedRoot.AbsoluteBounds);
            manifest.referenceImage = string.IsNullOrEmpty(referenceAssetPath)
                ? string.Empty
                : MakeRelativeAssetPath(outputFolder, referenceAssetPath);
            manifest.layers = new List<LayerDto>();

            for (int i = 0; i < layers.Count; i++)
            {
                ImportedLayerInfo layer = layers[i];
                LayerDto dto = new LayerDto();
                dto.nodeId = layer.NodeId;
                dto.nodeName = layer.NodeName;
                dto.type = layer.NodeType;
                dto.image = MakeRelativeAssetPath(outputFolder, layer.AssetPath);
                dto.bounds = ToBoundsDto(layer.FigmaBounds);
                dto.unity = new UnityRectDto();
                dto.unity.anchoredPosition = ToVectorDto(layer.AnchoredPosition);
                dto.unity.sizeDelta = ToVectorDto(layer.SizeDelta);
                dto.siblingIndex = layer.SiblingIndex;
                dto.unityObjectPath = layer.UnityObjectPath;
                if (layer.ClearChildNodeIds.Count > 0)
                {
                    dto.clearChildNodeIds = new List<string>(layer.ClearChildNodeIds);
                }

                manifest.layers.Add(dto);
            }

            string json = JsonUtility.ToJson(manifest, true);
            string manifestPath = FigmaPathUtility.CombineAssetPath(outputFolder, "manifest.json");
            string fullPath = FigmaPathUtility.ToFullPath(manifestPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, json);
            UnityEditor.AssetDatabase.Refresh();
            return manifestPath;
        }

        public FigmaManifestRebuildData LoadForRebuild(string manifestPath)
        {
            string normalizedManifestPath = NormalizeManifestAssetPath(manifestPath);
            string fullPath = FigmaPathUtility.ToFullPath(normalizedManifestPath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("未找到 manifest 文件：" + normalizedManifestPath);
            }

            ManifestDto manifest = JsonUtility.FromJson<ManifestDto>(File.ReadAllText(fullPath));
            if (manifest == null || string.IsNullOrEmpty(manifest.selectedNodeId))
            {
                throw new InvalidOperationException("manifest 内容无效，缺少 selectedNodeId。");
            }

            string outputFolder = GetAssetDirectory(normalizedManifestPath);
            FigmaManifestRebuildData data = new FigmaManifestRebuildData();
            data.FileKey = manifest.fileKey;
            data.ImageScale = manifest.imageScale > 0.01f ? manifest.imageScale : 1f;
            data.ContainerSliceStrategy = manifest.containerSliceStrategy;
            data.ManifestPath = normalizedManifestPath;
            data.OutputFolder = outputFolder;
            data.RootNode = CreateRootNode(manifest);
            data.ReferenceAssetPath = ResolveAssetPath(outputFolder, manifest.referenceImage);

            if (!string.IsNullOrEmpty(data.ReferenceAssetPath))
            {
                EnsureAssetFileExists(data.ReferenceAssetPath, "参考图");
            }

            if (manifest.layers != null)
            {
                for (int i = 0; i < manifest.layers.Count; i++)
                {
                    LayerDto dto = manifest.layers[i];
                    if (dto == null)
                    {
                        continue;
                    }

                    ImportedLayerInfo layer = CreateLayerInfo(outputFolder, data.RootNode.AbsoluteBounds, dto, i);
                    EnsureAssetFileExists(layer.AssetPath, "切图");
                    data.Layers.Add(layer);
                }
            }

            return data;
        }

        private static BoundsDto ToBoundsDto(Rect rect)
        {
            BoundsDto dto = new BoundsDto();
            dto.x = rect.x;
            dto.y = rect.y;
            dto.width = rect.width;
            dto.height = rect.height;
            return dto;
        }

        private static VectorDto ToVectorDto(Vector2 vector)
        {
            VectorDto dto = new VectorDto();
            dto.x = vector.x;
            dto.y = vector.y;
            return dto;
        }

        private static string MakeRelativeAssetPath(string outputFolder, string assetPath)
        {
            string normalizedOutput = outputFolder.Replace('\\', '/').TrimEnd('/');
            string normalizedAsset = assetPath.Replace('\\', '/');
            if (normalizedAsset.StartsWith(normalizedOutput + "/", StringComparison.Ordinal))
            {
                return normalizedAsset.Substring(normalizedOutput.Length + 1);
            }

            return normalizedAsset;
        }

        private static FigmaNodeInfo CreateRootNode(ManifestDto manifest)
        {
            FigmaNodeInfo root = new FigmaNodeInfo();
            root.Id = manifest.selectedNodeId;
            root.Name = manifest.selectedNodeName;
            root.Type = string.IsNullOrEmpty(manifest.selectedNodeType) ? "FRAME" : manifest.selectedNodeType;
            root.Visible = true;
            root.AbsoluteBounds = ToRect(manifest.rootBounds);
            root.HasAbsoluteBounds = true;
            return root;
        }

        private static ImportedLayerInfo CreateLayerInfo(
            string outputFolder,
            Rect rootBounds,
            LayerDto dto,
            int fallbackSiblingIndex)
        {
            ImportedLayerInfo layer = new ImportedLayerInfo();
            layer.NodeId = dto.nodeId;
            layer.NodeName = dto.nodeName;
            layer.NodeType = dto.type;
            layer.AssetPath = ResolveAssetPath(outputFolder, dto.image);
            layer.FigmaBounds = ToRect(dto.bounds);
            layer.SiblingIndex = dto.siblingIndex;

            if (layer.SiblingIndex < 0)
            {
                layer.SiblingIndex = fallbackSiblingIndex;
            }

            if (dto.unity != null)
            {
                layer.AnchoredPosition = ToVector(dto.unity.anchoredPosition);
                layer.SizeDelta = ToVector(dto.unity.sizeDelta);
            }

            if (layer.SizeDelta.x <= 0.01f || layer.SizeDelta.y <= 0.01f)
            {
                layer.SizeDelta = new Vector2(layer.FigmaBounds.width, layer.FigmaBounds.height);
            }

            if (layer.SizeDelta.x <= 0.01f || layer.SizeDelta.y <= 0.01f)
            {
                throw new InvalidOperationException("manifest 中存在尺寸无效的切图：" + layer.NodeName);
            }

            if (dto.unity == null)
            {
                layer.AnchoredPosition = FigmaCoordinateUtility.ToUnityAnchoredPosition(rootBounds, layer.FigmaBounds);
            }

            layer.UnityObjectPath = dto.unityObjectPath;
            if (dto.clearChildNodeIds != null)
            {
                layer.ClearChildNodeIds.AddRange(dto.clearChildNodeIds);
            }

            return layer;
        }

        private static Rect ToRect(BoundsDto dto)
        {
            if (dto == null)
            {
                return new Rect();
            }

            return new Rect(dto.x, dto.y, dto.width, dto.height);
        }

        private static Vector2 ToVector(VectorDto dto)
        {
            if (dto == null)
            {
                return Vector2.zero;
            }

            return new Vector2(dto.x, dto.y);
        }

        private static string ResolveAssetPath(string outputFolder, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string normalized = path.Trim().Replace('\\', '/');
            if (normalized.StartsWith("Assets/", StringComparison.Ordinal) || string.Equals(normalized, "Assets", StringComparison.Ordinal))
            {
                return normalized;
            }

            return FigmaPathUtility.CombineAssetPath(outputFolder, normalized);
        }

        private static void EnsureAssetFileExists(string assetPath, string label)
        {
            if (string.IsNullOrEmpty(assetPath) || !File.Exists(FigmaPathUtility.ToFullPath(assetPath)))
            {
                throw new FileNotFoundException(label + "资源不存在：" + assetPath);
            }
        }

        private static string NormalizeManifestAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("请先选择 manifest.json。");
            }

            string normalized = path.Trim().Replace('\\', '/');
            if (normalized.StartsWith("Assets/", StringComparison.Ordinal) || string.Equals(normalized, "Assets", StringComparison.Ordinal))
            {
                return normalized;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
            string fullPath = Path.GetFullPath(path).Replace('\\', '/');
            if (!fullPath.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("manifest 必须位于当前 Unity 项目的 Assets 目录内。");
            }

            string assetPath = fullPath.Substring(projectRoot.Length + 1);
            if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("manifest 必须位于当前 Unity 项目的 Assets 目录内。");
            }

            return assetPath;
        }

        private static string GetAssetDirectory(string assetPath)
        {
            string normalized = assetPath.Replace('\\', '/');
            int slashIndex = normalized.LastIndexOf("/", StringComparison.Ordinal);
            if (slashIndex <= 0)
            {
                return "Assets";
            }

            return normalized.Substring(0, slashIndex);
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
            public List<string> clearChildNodeIds;
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
