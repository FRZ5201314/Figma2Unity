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
            manifest.selectedNodeId = selectedRoot.Id;
            manifest.selectedNodeName = selectedRoot.Name;
            manifest.selectedNodeType = selectedRoot.Type;
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

        [Serializable]
        private sealed class ManifestDto
        {
            public string fileKey;
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
