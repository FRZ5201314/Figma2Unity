using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using static FigmaUiImporter.Editor.FigmaLocalization;

namespace FigmaUiImporter.Editor
{
    /// <summary>
    /// 为带 Figma background blur 的图层生成导入期资源：
    /// 1. 对根背景/backdrop 做一次高斯近似模糊；
    /// 2. 生成卡片圆角遮罩；
    /// 3. 计算 RawImage 采样背景的 uvRect。
    /// </summary>
    internal sealed class FigmaGeneratedAssetService
    {
        private const int BlurDownsample = 2;
        private const int BlurPasses = 3;
        private const int MaxMaskSize = 1024;

        private readonly UnitySpriteAssetImporter _spriteImporter = new UnitySpriteAssetImporter();

        public Dictionary<string, FigmaGeneratedBlurAssets> Generate(
            FigmaImportSettings settings,
            FigmaManifestRebuildData data,
            IList<ImportedLayerInfo> layers)
        {
            Dictionary<string, FigmaGeneratedBlurAssets> result =
                new Dictionary<string, FigmaGeneratedBlurAssets>(StringComparer.Ordinal);
            if (data == null || layers == null || layers.Count == 0)
            {
                return result;
            }

            string generatedFolder = FigmaPathUtility.CombineAssetPath(data.OutputFolder, "generated");
            FigmaPathUtility.EnsureAssetFolder(generatedFolder);
            Dictionary<string, Texture2D> blurredTextureCache =
                new Dictionary<string, Texture2D>(StringComparer.Ordinal);

            for (int i = 0; i < layers.Count; i++)
            {
                ImportedLayerInfo layer = layers[i];
                if (layer == null || layer.BackgroundBlur == null || !layer.BackgroundBlur.Enabled)
                {
                    continue;
                }

                ImportedLayerInfo backdrop = FindBackdropLayer(data, layers, layer);
                if (backdrop == null || !backdrop.HasUsableExportBounds || string.IsNullOrEmpty(backdrop.AssetPath))
                {
                    Debug.LogWarning(F(
                        "无法为背景模糊图层 {0} 找到 backdrop 图层，跳过模糊生成。",
                        "Could not find a backdrop layer for background-blur layer {0}; skipping blur generation.",
                        layer.NodeName));
                    continue;
                }

                Texture2D backdropTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(backdrop.AssetPath);
                if (backdropTexture == null)
                {
                    Debug.LogWarning(F(
                        "backdrop 纹理未导入：{0}",
                        "Backdrop texture is not imported: {0}",
                        backdrop.AssetPath));
                    continue;
                }

                Rect layoutBounds = layer.BackgroundBlur.HasBounds
                    ? layer.BackgroundBlur.Bounds
                    : layer.HasUsableLayoutBounds ? layer.LayoutBounds : layer.FigmaBounds;
                Rect sourceUvRect = ComputeSourceUvRect(backdrop.ExportBounds, layoutBounds);
                if (sourceUvRect.width <= 0.001f || sourceUvRect.height <= 0.001f)
                {
                    Debug.LogWarning(F(
                        "背景模糊图层 {0} 的布局边界不在 backdrop 范围内，跳过模糊生成。",
                        "Background-blur layer {0} is outside the backdrop bounds; skipping blur generation.",
                        layer.NodeName));
                    continue;
                }

                float radius = layer.BackgroundBlur.Radius;
                string blurPath = FigmaPathUtility.CombineAssetPath(
                    generatedFolder,
                    "blur_" + FigmaPathUtility.SafeFileName(backdrop.NodeId)
                        + "_r" + Mathf.RoundToInt(radius) + ".png");
                string maskPath = FigmaPathUtility.CombineAssetPath(
                    generatedFolder,
                    "mask_" + FigmaPathUtility.SafeFileName(layer.NodeId)
                        + "_" + Mathf.RoundToInt(layoutBounds.width)
                        + "x" + Mathf.RoundToInt(layoutBounds.height) + ".png");

                try
                {
                    Texture2D blurredAsset;
                    if (!blurredTextureCache.TryGetValue(blurPath, out blurredAsset))
                    {
                        if (!CreateBlurredTexture(backdrop.AssetPath, backdrop.ExportBounds, radius, blurPath))
                        {
                            continue;
                        }

                        blurredAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(blurPath);
                        if (blurredAsset != null)
                        {
                            blurredTextureCache[blurPath] = blurredAsset;
                        }
                    }

                    if (!CreateMaskTexture(layoutBounds, layer.BackgroundBlur.CornerRadii, maskPath))
                    {
                        continue;
                    }

                    Sprite maskAsset = AssetDatabase.LoadAssetAtPath<Sprite>(maskPath);
                    if (blurredAsset == null || maskAsset == null)
                    {
                        Debug.LogWarning(F(
                            "生成的模糊资源未能作为 Sprite/Texture 导入：{0} / {1}",
                            "Generated blur assets could not be imported as Sprite/Texture: {0} / {1}",
                            blurPath,
                            maskPath));
                        continue;
                    }

                    result[layer.NodeId] = new FigmaGeneratedBlurAssets
                    {
                        BlurredTexture = blurredAsset,
                        MaskSprite = maskAsset,
                        BackdropTexture = backdropTexture,
                        BackdropExportBounds = backdrop.ExportBounds,
                        SourceUvRect = sourceUvRect,
                        BlurBounds = layoutBounds,
                        BlurRadius = radius
                    };
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(F(
                        "生成背景模糊资源失败：{0}",
                        "Failed to generate background blur assets: {0}",
                        exception.Message));
                }
            }

            return result;
        }

        /// <summary>
        /// 按单个 FigmaLayerBinding 保存的元数据生成模糊资源，供 Inspector 按钮按需调用。
        /// </summary>
        public FigmaGeneratedBlurAssets GenerateForBinding(string layerNodeId, FigmaBlurLayerInfo info)
        {
            if (info == null
                || !info.enabled
                || string.IsNullOrEmpty(info.backdropAssetPath)
                || info.bounds.width <= 0.01f
                || info.bounds.height <= 0.01f
                || info.backdropExportBounds.width <= 0.01f
                || info.backdropExportBounds.height <= 0.01f)
            {
                return null;
            }

            string generatedFolder = info.generatedFolder;
            if (string.IsNullOrEmpty(generatedFolder))
            {
                string backdropPath = info.backdropAssetPath.Replace('\\', '/');
                int slash = backdropPath.LastIndexOf('/');
                int parentSlash = slash > 0 ? backdropPath.LastIndexOf('/', slash - 1) : -1;
                generatedFolder = parentSlash > 0
                    ? backdropPath.Substring(0, parentSlash) + "/generated"
                    : "Assets/FigmaImports/generated";
            }

            FigmaPathUtility.EnsureAssetFolder(generatedFolder);
            string blurPath = FigmaPathUtility.CombineAssetPath(
                generatedFolder,
                "blur_" + FigmaPathUtility.SafeFileName(info.backdropNodeId)
                    + "_r" + Mathf.RoundToInt(info.radius) + ".png");
            string maskPath = FigmaPathUtility.CombineAssetPath(
                generatedFolder,
                "mask_" + FigmaPathUtility.SafeFileName(layerNodeId)
                    + "_" + Mathf.RoundToInt(info.bounds.width)
                    + "x" + Mathf.RoundToInt(info.bounds.height) + ".png");

            if (!CreateBlurredTexture(info.backdropAssetPath, info.backdropExportBounds, info.radius, blurPath))
            {
                return null;
            }

            if (!CreateMaskTexture(info.bounds, info.cornerRadii, maskPath))
            {
                return null;
            }

            Texture2D blurredAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(blurPath);
            Sprite maskAsset = AssetDatabase.LoadAssetAtPath<Sprite>(maskPath);
            Texture2D backdropTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(info.backdropAssetPath);
            if (blurredAsset == null || maskAsset == null || backdropTexture == null)
            {
                return null;
            }

            return new FigmaGeneratedBlurAssets
            {
                BlurredTexture = blurredAsset,
                MaskSprite = maskAsset,
                BackdropTexture = backdropTexture,
                BackdropExportBounds = info.backdropExportBounds,
                SourceUvRect = ComputeSourceUvRect(info.backdropExportBounds, info.bounds),
                BlurBounds = info.bounds,
                BlurRadius = info.radius
            };
        }

        private static ImportedLayerInfo FindBackdropLayer(
            FigmaManifestRebuildData data,
            IList<ImportedLayerInfo> layers,
            ImportedLayerInfo blurLayer)
        {
            string backdropNodeId = blurLayer.BackgroundBlur != null
                ? blurLayer.BackgroundBlur.BackdropNodeId
                : string.Empty;
            if (!string.IsNullOrEmpty(backdropNodeId))
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    if (layers[i] != null && string.Equals(layers[i].NodeId, backdropNodeId, StringComparison.Ordinal))
                    {
                        return layers[i];
                    }
                }
            }

            // 兼容旧包/缺失字段：回退到导出根节点对应的背景层。
            if (data.RootNode != null)
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    if (layers[i] != null && string.Equals(layers[i].NodeId, data.RootNode.Id, StringComparison.Ordinal))
                    {
                        return layers[i];
                    }
                }
            }

            return null;
        }

        private static Rect ComputeSourceUvRect(Rect backdropBounds, Rect layoutBounds)
        {
            if (backdropBounds.width <= 0.01f || backdropBounds.height <= 0.01f)
            {
                return new Rect();
            }

            float u = (layoutBounds.x - backdropBounds.x) / backdropBounds.width;
            float v = 1f - ((layoutBounds.y - backdropBounds.y) + layoutBounds.height) / backdropBounds.height;
            float width = layoutBounds.width / backdropBounds.width;
            float height = layoutBounds.height / backdropBounds.height;

            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);
            width = Mathf.Clamp(width, 0f, 1f - u);
            height = Mathf.Clamp(height, 0f, 1f - v);
            return new Rect(u, v, width, height);
        }

        private bool CreateBlurredTexture(
            string backdropAssetPath,
            Rect backdropBounds,
            float radius,
            string outputAssetPath)
        {
            Texture2D source = LoadTextureFromFile(backdropAssetPath);
            if (source == null)
            {
                return false;
            }

            try
            {
                Color32[] sourcePixels = source.GetPixels32();
                int sourceWidth = source.width;
                int sourceHeight = source.height;
                int width = Mathf.Max(1, sourceWidth / BlurDownsample);
                int height = Mathf.Max(1, sourceHeight / BlurDownsample);
                Color32[] pixels = Downsample(sourcePixels, sourceWidth, sourceHeight, width, height);

                float pixelsPerUnit = backdropBounds.width > 0.01f
                    ? sourceWidth / backdropBounds.width
                    : 1f;
                int radiusPixels = Mathf.Max(
                    1,
                    Mathf.RoundToInt(radius * pixelsPerUnit / BlurDownsample * 0.5f));
                for (int pass = 0; pass < BlurPasses; pass++)
                {
                    pixels = BoxBlur(pixels, width, height, radiusPixels, true);
                    pixels = BoxBlur(pixels, width, height, radiusPixels, false);
                }

                Texture2D blurred = new Texture2D(width, height, TextureFormat.RGBA32, false);
                try
                {
                    blurred.SetPixels32(pixels);
                    blurred.Apply();
                    WritePng(outputAssetPath, blurred.EncodeToPNG());
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(blurred);
                }

                ImportGeneratedAssets(outputAssetPath);
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        private bool CreateMaskTexture(Rect layoutBounds, Vector4 cornerRadii, string outputAssetPath)
        {
            int width = Mathf.Max(1, Mathf.CeilToInt(layoutBounds.width));
            int height = Mathf.Max(1, Mathf.CeilToInt(layoutBounds.height));
            float scale = Mathf.Min(1f, MaxMaskSize / (float)Mathf.Max(width, height));
            int maskWidth = Mathf.Max(1, Mathf.RoundToInt(width * scale));
            int maskHeight = Mathf.Max(1, Mathf.RoundToInt(height * scale));
            float radiusScale = scale;

            // 圆角按比例缩小，避免大卡片生成过大的遮罩纹理。
            float topLeft = cornerRadii.x * radiusScale;
            float topRight = cornerRadii.y * radiusScale;
            float bottomRight = cornerRadii.z * radiusScale;
            float bottomLeft = cornerRadii.w * radiusScale;

            Color32[] pixels = new Color32[maskWidth * maskHeight];
            for (int y = 0; y < maskHeight; y++)
            {
                float py = (y + 0.5f) * height / maskHeight;
                for (int x = 0; x < maskWidth; x++)
                {
                    float px = (x + 0.5f) * width / maskWidth;
                    float radius = GetCornerRadius(px, py, width, height, topLeft, topRight, bottomRight, bottomLeft);
                    float alpha = GetRoundedRectAlpha(px, py, width, height, radius);
                    pixels[y * maskWidth + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            Texture2D maskTexture = new Texture2D(maskWidth, maskHeight, TextureFormat.RGBA32, false);
            try
            {
                maskTexture.SetPixels32(pixels);
                maskTexture.Apply();
                WritePng(outputAssetPath, maskTexture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(maskTexture);
            }

            ImportGeneratedAssets(outputAssetPath);
            return true;
        }

        private static float GetCornerRadius(
            float px,
            float py,
            float width,
            float height,
            float topLeft,
            float topRight,
            float bottomRight,
            float bottomLeft)
        {
            bool left = px < width * 0.5f;
            bool top = py < height * 0.5f;
            if (left && top) return topLeft;
            if (!left && top) return topRight;
            if (!left && !top) return bottomRight;
            return bottomLeft;
        }

        private static float GetRoundedRectAlpha(float px, float py, float width, float height, float radius)
        {
            // 标准圆角矩形 SDF；distance=0 表示在边界上。
            float halfWidth = width * 0.5f;
            float halfHeight = height * 0.5f;
            float qx = Mathf.Abs(px - halfWidth) - halfWidth + radius;
            float qy = Mathf.Abs(py - halfHeight) - halfHeight + radius;
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
            float distance = outside + inside - radius;
            return Mathf.Clamp01(0.5f - distance);
        }

        private static Color32[] Downsample(
            Color32[] source,
            int sourceWidth,
            int sourceHeight,
            int width,
            int height)
        {
            Color32[] result = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                int y0 = y * sourceHeight / height;
                int y1 = Mathf.Max(y0 + 1, (y + 1) * sourceHeight / height);
                for (int x = 0; x < width; x++)
                {
                    int x0 = x * sourceWidth / width;
                    int x1 = Mathf.Max(x0 + 1, (x + 1) * sourceWidth / width);
                    long r = 0;
                    long g = 0;
                    long b = 0;
                    long a = 0;
                    int count = 0;
                    for (int sy = y0; sy < y1; sy++)
                    {
                        int row = sy * sourceWidth;
                        for (int sx = x0; sx < x1; sx++)
                        {
                            Color32 color = source[row + sx];
                            r += color.r;
                            g += color.g;
                            b += color.b;
                            a += color.a;
                            count++;
                        }
                    }

                    if (count == 0)
                    {
                        count = 1;
                    }

                    result[y * width + x] = new Color32(
                        (byte)(r / count),
                        (byte)(g / count),
                        (byte)(b / count),
                        (byte)(a / count));
                }
            }

            return result;
        }

        private static Color32[] BoxBlur(
            Color32[] source,
            int width,
            int height,
            int radius,
            bool horizontal)
        {
            Color32[] result = new Color32[source.Length];
            int window = radius * 2 + 1;
            if (horizontal)
            {
                for (int y = 0; y < height; y++)
                {
                    int row = y * width;
                    long r = 0;
                    long g = 0;
                    long b = 0;
                    long a = 0;
                    for (int x = -radius; x <= radius; x++)
                    {
                        Color32 color = source[row + Mathf.Clamp(x, 0, width - 1)];
                        r += color.r;
                        g += color.g;
                        b += color.b;
                        a += color.a;
                    }

                    for (int x = 0; x < width; x++)
                    {
                        result[row + x] = new Color32(
                            (byte)(r / window),
                            (byte)(g / window),
                            (byte)(b / window),
                            (byte)(a / window));
                        Color32 remove = source[row + Mathf.Clamp(x - radius, 0, width - 1)];
                        Color32 add = source[row + Mathf.Clamp(x + radius + 1, 0, width - 1)];
                        r += add.r - remove.r;
                        g += add.g - remove.g;
                        b += add.b - remove.b;
                        a += add.a - remove.a;
                    }
                }
            }
            else
            {
                for (int x = 0; x < width; x++)
                {
                    long r = 0;
                    long g = 0;
                    long b = 0;
                    long a = 0;
                    for (int y = -radius; y <= radius; y++)
                    {
                        Color32 color = source[Mathf.Clamp(y, 0, height - 1) * width + x];
                        r += color.r;
                        g += color.g;
                        b += color.b;
                        a += color.a;
                    }

                    for (int y = 0; y < height; y++)
                    {
                        result[y * width + x] = new Color32(
                            (byte)(r / window),
                            (byte)(g / window),
                            (byte)(b / window),
                            (byte)(a / window));
                        Color32 remove = source[Mathf.Clamp(y - radius, 0, height - 1) * width + x];
                        Color32 add = source[Mathf.Clamp(y + radius + 1, 0, height - 1) * width + x];
                        r += add.r - remove.r;
                        g += add.g - remove.g;
                        b += add.b - remove.b;
                        a += add.a - remove.a;
                    }
                }
            }

            return result;
        }

        private void ImportGeneratedAssets(string assetPath)
        {
            _spriteImporter.ImportSprites(
                new[] { assetPath },
                TextureImporterCompressionMode.None);
        }

        private static void WritePng(string assetPath, byte[] bytes)
        {
            string fullPath = FigmaPathUtility.ToFullPath(assetPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(fullPath, bytes);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        private static Texture2D LoadTextureFromFile(string assetPath)
        {
            string fullPath = FigmaPathUtility.ToFullPath(assetPath);
            if (!File.Exists(fullPath))
            {
                return null;
            }

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(fullPath)))
            {
                UnityEngine.Object.DestroyImmediate(texture);
                return null;
            }

            return texture;
        }
    }
}
