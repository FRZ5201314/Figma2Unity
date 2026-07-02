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
            manifest.textImportMode = settings.TextImportMode.ToString();
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
                dto.kind = layer.IsTextLayer ? "text" : "image";
                dto.image = MakeRelativeAssetPath(outputFolder, layer.AssetPath);
                dto.text = layer.HasTextStyle ? ToTextDto(layer.TextStyle) : null;
                dto.bounds = ToBoundsDto(layer.FigmaBounds);
                dto.unity = new UnityRectDto();
                dto.unity.anchoredPosition = ToVectorDto(layer.AnchoredPosition);
                dto.unity.sizeDelta = ToVectorDto(layer.SizeDelta);
                dto.siblingIndex = layer.SiblingIndex;
                dto.unityObjectPath = layer.UnityObjectPath;
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
            data.TextImportMode = manifest.textImportMode;
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

        public bool TryLoadTextStyleForLayer(
            string manifestPath,
            string nodeId,
            out FigmaTextStyleInfo textStyle)
        {
            textStyle = null;
            if (string.IsNullOrEmpty(nodeId))
            {
                return false;
            }

            string normalizedManifestPath = NormalizeManifestAssetPath(manifestPath);
            string fullPath = FigmaPathUtility.ToFullPath(normalizedManifestPath);
            if (!File.Exists(fullPath))
            {
                return false;
            }

            ManifestDto manifest = JsonUtility.FromJson<ManifestDto>(File.ReadAllText(fullPath));
            if (manifest == null || manifest.layers == null)
            {
                return false;
            }

            for (int i = 0; i < manifest.layers.Count; i++)
            {
                LayerDto layer = manifest.layers[i];
                if (layer != null
                    && layer.text != null
                    && string.Equals(layer.nodeId, nodeId, StringComparison.Ordinal))
                {
                    textStyle = ToTextStyle(layer.text);
                    return true;
                }
            }

            return false;
        }

        public bool TryLoadLayerForConversion(
            string manifestPath,
            string nodeId,
            out FigmaManifestRebuildData data)
        {
            data = null;
            if (string.IsNullOrEmpty(nodeId))
            {
                return false;
            }

            string normalizedManifestPath = NormalizeManifestAssetPath(manifestPath);
            string fullPath = FigmaPathUtility.ToFullPath(normalizedManifestPath);
            if (!File.Exists(fullPath))
            {
                return false;
            }

            ManifestDto manifest = JsonUtility.FromJson<ManifestDto>(File.ReadAllText(fullPath));
            if (manifest == null || manifest.layers == null)
            {
                return false;
            }

            string outputFolder = GetAssetDirectory(normalizedManifestPath);
            FigmaNodeInfo rootNode = CreateRootNode(manifest);
            for (int i = 0; i < manifest.layers.Count; i++)
            {
                LayerDto layerDto = manifest.layers[i];
                if (layerDto == null || !string.Equals(layerDto.nodeId, nodeId, StringComparison.Ordinal))
                {
                    continue;
                }

                data = new FigmaManifestRebuildData();
                data.FileKey = manifest.fileKey;
                data.ImageScale = manifest.imageScale > 0.01f ? manifest.imageScale : 1f;
                data.ContainerSliceStrategy = manifest.containerSliceStrategy;
                data.TextImportMode = manifest.textImportMode;
                data.ManifestPath = normalizedManifestPath;
                data.OutputFolder = outputFolder;
                data.RootNode = rootNode;
                data.ReferenceAssetPath = ResolveAssetPath(outputFolder, manifest.referenceImage);
                data.Layers.Add(CreateLayerInfo(outputFolder, rootNode.AbsoluteBounds, layerDto, i));
                return true;
            }

            return false;
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
            layer.Kind = string.Equals(dto.kind, "text", StringComparison.OrdinalIgnoreCase) && dto.text != null
                ? ImportedLayerKind.Text
                : ImportedLayerKind.Image;
            layer.AssetPath = ResolveAssetPath(outputFolder, dto.image);
            layer.TextStyle = dto.text != null ? ToTextStyle(dto.text) : null;
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
            return layer;
        }

        private static TextDto ToTextDto(FigmaTextStyleInfo text)
        {
            if (text == null)
            {
                return null;
            }

            TextDto dto = new TextDto();
            dto.characters = text.Characters;
            dto.fontFamily = text.FontFamily;
            dto.fontStyleName = text.FontStyleName;
            dto.fontPostScriptName = text.FontPostScriptName;
            dto.fontWeight = text.FontWeight;
            dto.fontSize = text.FontSize;
            dto.lineHeightPx = text.LineHeightPx;
            dto.letterSpacing = text.LetterSpacing;
            dto.color = ToColorDto(text.Color);
            dto.horizontalAlign = text.HorizontalAlign;
            dto.verticalAlign = text.VerticalAlign;
            dto.autoResize = text.AutoResize;
            dto.hasMixedStyles = text.HasMixedStyles;
            dto.hasUnsupportedVisuals = text.HasUnsupportedVisuals;
            return dto;
        }

        private static FigmaTextStyleInfo ToTextStyle(TextDto dto)
        {
            if (dto == null)
            {
                return null;
            }

            FigmaTextStyleInfo text = new FigmaTextStyleInfo();
            text.Characters = dto.characters;
            text.FontFamily = dto.fontFamily;
            text.FontStyleName = dto.fontStyleName;
            text.FontPostScriptName = dto.fontPostScriptName;
            text.FontWeight = dto.fontWeight;
            text.FontSize = dto.fontSize;
            text.LineHeightPx = dto.lineHeightPx;
            text.LetterSpacing = dto.letterSpacing;
            text.Color = ToColor(dto.color);
            text.HorizontalAlign = dto.horizontalAlign;
            text.VerticalAlign = dto.verticalAlign;
            text.AutoResize = dto.autoResize;
            text.HasMixedStyles = dto.hasMixedStyles;
            text.HasUnsupportedVisuals = dto.hasUnsupportedVisuals;
            return text;
        }

        private static ColorDto ToColorDto(Color color)
        {
            ColorDto dto = new ColorDto();
            dto.r = color.r;
            dto.g = color.g;
            dto.b = color.b;
            dto.a = color.a;
            return dto;
        }

        private static Color ToColor(ColorDto dto)
        {
            if (dto == null)
            {
                return Color.white;
            }

            return new Color(dto.r, dto.g, dto.b, dto.a);
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
        private sealed class BoundsDto
        {
            public float x;
            public float y;
            public float width;
            public float height;
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
