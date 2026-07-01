using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FigmaUiImporter.Editor
{
    internal sealed class FigmaLayerImagePostprocessor
    {
        private const float AlphaEpsilon = 0.001f;

        public List<string> RemoveClearChildPixels(IList<ImportedLayerInfo> layers)
        {
            List<string> changedAssetPaths = new List<string>();
            if (layers == null || layers.Count == 0)
            {
                return changedAssetPaths;
            }

            Dictionary<string, ImportedLayerInfo> layerMap = ToLayerMap(layers);
            Dictionary<string, byte[]> originalChildBytes = LoadOriginalChildBytes(layers, layerMap);

            for (int i = 0; i < layers.Count; i++)
            {
                ImportedLayerInfo layer = layers[i];
                if (TryRemoveClearChildPixels(layer, layerMap, originalChildBytes))
                {
                    changedAssetPaths.Add(layer.AssetPath);
                }
            }

            return changedAssetPaths;
        }

        private static bool TryRemoveClearChildPixels(
            ImportedLayerInfo parentLayer,
            IDictionary<string, ImportedLayerInfo> layerMap,
            IDictionary<string, byte[]> originalChildBytes)
        {
            if (parentLayer == null
                || parentLayer.ClearChildNodeIds.Count == 0
                || string.IsNullOrEmpty(parentLayer.AssetPath)
                || !IsUsableRect(parentLayer.FigmaBounds))
            {
                return false;
            }

            string parentFullPath = FigmaPathUtility.ToFullPath(parentLayer.AssetPath);
            if (!File.Exists(parentFullPath))
            {
                Debug.LogWarning("容器底图处理跳过，未找到父级图片：" + parentLayer.AssetPath);
                return false;
            }

            Texture2D parentTexture;
            if (!TryLoadTexture(File.ReadAllBytes(parentFullPath), parentLayer.AssetPath, out parentTexture))
            {
                return false;
            }

            bool changed = false;
            try
            {
                Color32[] parentPixels = parentTexture.GetPixels32();
                List<ImportedLayerInfo> childLayers = ResolveChildLayers(parentLayer, layerMap);

                for (int i = 0; i < childLayers.Count; i++)
                {
                    ImportedLayerInfo childLayer = childLayers[i];
                    byte[] childBytes;
                    if (!originalChildBytes.TryGetValue(childLayer.NodeId, out childBytes))
                    {
                        continue;
                    }

                    Texture2D childTexture;
                    if (!TryLoadTexture(childBytes, childLayer.AssetPath, out childTexture))
                    {
                        continue;
                    }

                    try
                    {
                        changed |= RemoveChildFromParent(
                            parentLayer.FigmaBounds,
                            parentTexture.width,
                            parentTexture.height,
                            parentPixels,
                            childLayer.FigmaBounds,
                            childTexture.width,
                            childTexture.height,
                            childTexture.GetPixels32());
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(childTexture);
                    }
                }

                if (!changed)
                {
                    return false;
                }

                parentTexture.SetPixels32(parentPixels);
                parentTexture.Apply(false, false);
                File.WriteAllBytes(parentFullPath, parentTexture.EncodeToPNG());
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parentTexture);
            }
        }

        private static bool RemoveChildFromParent(
            Rect parentBounds,
            int parentWidth,
            int parentHeight,
            Color32[] parentPixels,
            Rect childBounds,
            int childWidth,
            int childHeight,
            Color32[] childPixels)
        {
            if (!IsUsableRect(childBounds)
                || parentWidth <= 0
                || parentHeight <= 0
                || childWidth <= 0
                || childHeight <= 0)
            {
                return false;
            }

            bool changed = false;
            for (int childY = 0; childY < childHeight; childY++)
            {
                float figmaY = childBounds.yMin
                    + childBounds.height * (1f - (childY + 0.5f) / childHeight);
                int parentY = Mathf.FloorToInt(
                    (parentBounds.yMax - figmaY) / parentBounds.height * parentHeight);
                if (parentY < 0 || parentY >= parentHeight)
                {
                    continue;
                }

                for (int childX = 0; childX < childWidth; childX++)
                {
                    int childIndex = childY * childWidth + childX;
                    Color32 source = childPixels[childIndex];
                    if (source.a == 0)
                    {
                        continue;
                    }

                    float figmaX = childBounds.xMin
                        + childBounds.width * (childX + 0.5f) / childWidth;
                    int parentX = Mathf.FloorToInt(
                        (figmaX - parentBounds.xMin) / parentBounds.width * parentWidth);
                    if (parentX < 0 || parentX >= parentWidth)
                    {
                        continue;
                    }

                    int parentIndex = parentY * parentWidth + parentX;
                    parentPixels[parentIndex] = RemoveSource(parentPixels[parentIndex], source);
                    changed = true;
                }
            }

            return changed;
        }

        private static Color32 RemoveSource(Color32 composite, Color32 source)
        {
            float sourceAlpha = source.a / 255f;
            if (sourceAlpha <= AlphaEpsilon)
            {
                return composite;
            }

            if (sourceAlpha >= 1f - AlphaEpsilon)
            {
                return new Color32(0, 0, 0, 0);
            }

            float compositeAlpha = composite.a / 255f;
            float inverseSourceAlpha = 1f - sourceAlpha;
            float destinationAlpha = Mathf.Clamp01((compositeAlpha - sourceAlpha) / inverseSourceAlpha);
            if (destinationAlpha <= AlphaEpsilon)
            {
                return new Color32(0, 0, 0, 0);
            }

            return new Color32(
                ToByte(RecoverChannel(composite.r, compositeAlpha, source.r, sourceAlpha, inverseSourceAlpha, destinationAlpha)),
                ToByte(RecoverChannel(composite.g, compositeAlpha, source.g, sourceAlpha, inverseSourceAlpha, destinationAlpha)),
                ToByte(RecoverChannel(composite.b, compositeAlpha, source.b, sourceAlpha, inverseSourceAlpha, destinationAlpha)),
                ToByte(destinationAlpha));
        }

        private static float RecoverChannel(
            byte compositeChannel,
            float compositeAlpha,
            byte sourceChannel,
            float sourceAlpha,
            float inverseSourceAlpha,
            float destinationAlpha)
        {
            float compositePremultiplied = compositeChannel / 255f * compositeAlpha;
            float sourcePremultiplied = sourceChannel / 255f * sourceAlpha;
            float destinationPremultiplied = (compositePremultiplied - sourcePremultiplied) / inverseSourceAlpha;
            return Mathf.Clamp01(destinationPremultiplied / destinationAlpha);
        }

        private static byte ToByte(float value)
        {
            return (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
        }

        private static List<ImportedLayerInfo> ResolveChildLayers(
            ImportedLayerInfo parentLayer,
            IDictionary<string, ImportedLayerInfo> layerMap)
        {
            List<ImportedLayerInfo> childLayers = new List<ImportedLayerInfo>();
            HashSet<string> added = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < parentLayer.ClearChildNodeIds.Count; i++)
            {
                string childNodeId = parentLayer.ClearChildNodeIds[i];
                ImportedLayerInfo childLayer;
                if (string.IsNullOrEmpty(childNodeId)
                    || !added.Add(childNodeId)
                    || !layerMap.TryGetValue(childNodeId, out childLayer)
                    || string.IsNullOrEmpty(childLayer.AssetPath)
                    || !IsUsableRect(childLayer.FigmaBounds))
                {
                    continue;
                }

                childLayers.Add(childLayer);
            }

            childLayers.Sort((left, right) => right.SiblingIndex.CompareTo(left.SiblingIndex));
            return childLayers;
        }

        private static Dictionary<string, byte[]> LoadOriginalChildBytes(
            IList<ImportedLayerInfo> layers,
            IDictionary<string, ImportedLayerInfo> layerMap)
        {
            Dictionary<string, byte[]> bytesByNodeId = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            for (int i = 0; i < layers.Count; i++)
            {
                ImportedLayerInfo parentLayer = layers[i];
                if (parentLayer == null || parentLayer.ClearChildNodeIds.Count == 0)
                {
                    continue;
                }

                for (int childIndex = 0; childIndex < parentLayer.ClearChildNodeIds.Count; childIndex++)
                {
                    string childNodeId = parentLayer.ClearChildNodeIds[childIndex];
                    if (string.IsNullOrEmpty(childNodeId) || bytesByNodeId.ContainsKey(childNodeId))
                    {
                        continue;
                    }

                    ImportedLayerInfo childLayer;
                    if (!layerMap.TryGetValue(childNodeId, out childLayer) || string.IsNullOrEmpty(childLayer.AssetPath))
                    {
                        continue;
                    }

                    string childFullPath = FigmaPathUtility.ToFullPath(childLayer.AssetPath);
                    if (!File.Exists(childFullPath))
                    {
                        Debug.LogWarning("容器底图处理跳过，未找到子级图片：" + childLayer.AssetPath);
                        continue;
                    }

                    bytesByNodeId.Add(childNodeId, File.ReadAllBytes(childFullPath));
                }
            }

            return bytesByNodeId;
        }

        private static Dictionary<string, ImportedLayerInfo> ToLayerMap(IList<ImportedLayerInfo> layers)
        {
            Dictionary<string, ImportedLayerInfo> map = new Dictionary<string, ImportedLayerInfo>(StringComparer.Ordinal);
            for (int i = 0; i < layers.Count; i++)
            {
                ImportedLayerInfo layer = layers[i];
                if (layer != null && !string.IsNullOrEmpty(layer.NodeId) && !map.ContainsKey(layer.NodeId))
                {
                    map.Add(layer.NodeId, layer);
                }
            }

            return map;
        }

        private static bool TryLoadTexture(byte[] bytes, string assetPath, out Texture2D texture)
        {
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (bytes != null && ImageConversion.LoadImage(texture, bytes, false))
            {
                return true;
            }

            UnityEngine.Object.DestroyImmediate(texture);
            texture = null;
            Debug.LogWarning("容器底图处理跳过，PNG 无法解析：" + assetPath);
            return false;
        }

        private static bool IsUsableRect(Rect rect)
        {
            return rect.width > 0.01f && rect.height > 0.01f;
        }
    }
}
