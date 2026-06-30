using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FigmaUiImporter.Editor
{
    internal sealed class FigmaLayerGeometryResolver : IDisposable
    {
        private const int MaxSamples = 512;
        private const int MinAlpha = 48;
        private const int StrongAlpha = 128;
        private const float MaxAcceptedMeanDifference = 48f;
        private const float CenteredXScoreTolerance = 4f;

        private readonly Rect _rootBounds;
        private readonly TextureData _reference;
        private readonly Vector2 _pixelScale;

        public FigmaLayerGeometryResolver(
            FigmaImportSettings settings,
            FigmaNodeInfo selectedRoot,
            string referenceAssetPath)
        {
            _rootBounds = selectedRoot != null ? selectedRoot.AbsoluteBounds : new Rect();
            _pixelScale = GetFallbackPixelScale(settings);

            if (!string.IsNullOrEmpty(referenceAssetPath))
            {
                _reference = TextureData.Load(referenceAssetPath);
                if (_reference != null && _rootBounds.width > 0.01f && _rootBounds.height > 0.01f)
                {
                    _pixelScale = new Vector2(
                        Mathf.Max(0.01f, _reference.Width / _rootBounds.width),
                        Mathf.Max(0.01f, _reference.Height / _rootBounds.height));
                }
            }
        }

        public void Resolve(ImportedLayerInfo layer)
        {
            if (layer == null || string.IsNullOrEmpty(layer.AssetPath))
            {
                return;
            }

            using (TextureData layerTexture = TextureData.Load(layer.AssetPath))
            {
                if (layerTexture == null)
                {
                    return;
                }

                Vector2 imageSize = new Vector2(
                    layerTexture.Width / _pixelScale.x,
                    layerTexture.Height / _pixelScale.y);

                if (IsRectangle(layer))
                {
                    ResolveRectangleGeometry(layer, layerTexture, imageSize);
                    return;
                }

                if (!ShouldUseImageSize(layer, imageSize))
                {
                    return;
                }

                Vector2 originalPosition = layer.AnchoredPosition;
                layer.SizeDelta = imageSize;

                Vector2 matchedPosition;
                if (TryMatchReference(layer, layerTexture, imageSize, out matchedPosition))
                {
                    if (ShouldUseMatchedPosition(layer, imageSize, originalPosition, matchedPosition))
                    {
                        layer.AnchoredPosition = matchedPosition;
                    }

                    return;
                }
            }
        }

        private void ResolveRectangleGeometry(
            ImportedLayerInfo layer,
            TextureData layerTexture,
            Vector2 imageSize)
        {
            if (imageSize.x <= layer.SizeDelta.x + 2f && imageSize.y <= layer.SizeDelta.y + 2f)
            {
                return;
            }

            layer.SizeDelta = imageSize;
            Vector2 matchedPosition;
            if (TryMatchReference(layer, layerTexture, imageSize, out matchedPosition))
            {
                layer.AnchoredPosition = matchedPosition;
            }
        }

        public void Dispose()
        {
            if (_reference != null)
            {
                _reference.Dispose();
            }
        }

        private bool TryMatchReference(
            ImportedLayerInfo layer,
            TextureData layerTexture,
            Vector2 imageSize,
            out Vector2 anchoredPosition)
        {
            anchoredPosition = layer.AnchoredPosition;
            if (_reference == null
                || layerTexture.Width > _reference.Width
                || layerTexture.Height > _reference.Height
                || !_rootBounds.HasUsableSize())
            {
                return false;
            }

            List<SamplePoint> samples = BuildSamples(layerTexture, StrongAlpha);
            if (samples.Count < 24)
            {
                samples = BuildSamples(layerTexture, MinAlpha);
            }

            if (samples.Count < 8)
            {
                return false;
            }

            int expectedX = Mathf.RoundToInt((layer.FigmaBounds.x - _rootBounds.x) * _pixelScale.x);
            int expectedY = Mathf.RoundToInt((layer.FigmaBounds.y - _rootBounds.y) * _pixelScale.y);
            int radiusX = GetSearchRadius(layerTexture.Width, layer.FigmaBounds.width * _pixelScale.x, _pixelScale.x);
            int radiusY = GetSearchRadius(layerTexture.Height, layer.FigmaBounds.height * _pixelScale.y, _pixelScale.y);
            int minComparedSamples = Mathf.Max(8, Mathf.CeilToInt(samples.Count * 0.25f));

            int minX = Mathf.Max(-layerTexture.Width + 1, expectedX - radiusX);
            int maxX = Mathf.Min(_reference.Width - 1, expectedX + radiusX);
            int minY = Mathf.Max(-layerTexture.Height + 1, expectedY - radiusY);
            int maxY = Mathf.Min(_reference.Height - 1, expectedY + radiusY);
            if (minX > maxX || minY > maxY)
            {
                return false;
            }

            float bestScore = float.MaxValue;
            int bestX = expectedX;
            int bestY = expectedY;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int comparedSamples;
                    float score = ScoreAt(_reference, layerTexture, samples, x, y, out comparedSamples);
                    if (comparedSamples < minComparedSamples)
                    {
                        continue;
                    }

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestX = x;
                        bestY = y;
                    }
                }
            }

            if (bestScore > MaxAcceptedMeanDifference)
            {
                return false;
            }

            bestX = ResolveHorizontalPosition(
                layer,
                layerTexture,
                samples,
                bestX,
                bestY,
                bestScore,
                minX,
                maxX,
                minComparedSamples);

            Rect imageBounds = new Rect(
                _rootBounds.x + bestX / _pixelScale.x,
                _rootBounds.y + bestY / _pixelScale.y,
                imageSize.x,
                imageSize.y);
            anchoredPosition = FigmaCoordinateUtility.ToUnityAnchoredPosition(_rootBounds, imageBounds);
            return true;
        }

        private int ResolveHorizontalPosition(
            ImportedLayerInfo layer,
            TextureData layerTexture,
            IList<SamplePoint> samples,
            int matchedX,
            int matchedY,
            float matchedScore,
            int minX,
            int maxX,
            int minComparedSamples)
        {
            float layerCenterX = layer.FigmaBounds.x + layer.FigmaBounds.width * 0.5f;
            int centeredX = Mathf.RoundToInt((layerCenterX - _rootBounds.x) * _pixelScale.x - layerTexture.Width * 0.5f);
            if (centeredX < minX || centeredX > maxX || Mathf.Abs(centeredX - matchedX) <= 1)
            {
                return matchedX;
            }

            int comparedSamples;
            float centeredScore = ScoreAt(_reference, layerTexture, samples, centeredX, matchedY, out comparedSamples);
            if (comparedSamples < minComparedSamples)
            {
                return matchedX;
            }

            if (centeredScore <= matchedScore + CenteredXScoreTolerance)
            {
                return centeredX;
            }

            return matchedX;
        }

        private static bool ShouldUseImageSize(ImportedLayerInfo layer, Vector2 imageSize)
        {
            if (layer == null)
            {
                return false;
            }

            if (IsText(layer))
            {
                return true;
            }

            float widthDifference = Mathf.Abs(imageSize.x - layer.SizeDelta.x);
            float heightDifference = Mathf.Abs(imageSize.y - layer.SizeDelta.y);
            if (widthDifference <= 1f && heightDifference <= 1f)
            {
                return false;
            }

            if (IsRectangle(layer))
            {
                return false;
            }

            return true;
        }

        private static bool ShouldUseMatchedPosition(
            ImportedLayerInfo layer,
            Vector2 imageSize,
            Vector2 originalPosition,
            Vector2 matchedPosition)
        {
            if (layer == null || IsRectangle(layer))
            {
                return false;
            }

            float deltaX = Mathf.Abs(matchedPosition.x - originalPosition.x);
            float deltaY = Mathf.Abs(matchedPosition.y - originalPosition.y);
            if (IsText(layer))
            {
                return deltaY <= 12f;
            }

            bool hasMeaningfulExpansion = imageSize.x > layer.FigmaBounds.width + 4f
                || imageSize.y > layer.FigmaBounds.height + 4f;
            return hasMeaningfulExpansion && deltaX <= 32f && deltaY <= 32f;
        }

        private static bool IsText(ImportedLayerInfo layer)
        {
            return layer != null && string.Equals(layer.NodeType, "TEXT", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRectangle(ImportedLayerInfo layer)
        {
            return layer != null && string.Equals(layer.NodeType, "RECTANGLE", StringComparison.OrdinalIgnoreCase);
        }

        private static List<SamplePoint> BuildSamples(TextureData texture, int minAlpha)
        {
            List<SamplePoint> visible = new List<SamplePoint>();
            for (int y = 0; y < texture.Height; y++)
            {
                for (int x = 0; x < texture.Width; x++)
                {
                    Color32 color = texture.GetPixelTopLeft(x, y);
                    if (color.a >= minAlpha)
                    {
                        visible.Add(new SamplePoint(x, y, color));
                    }
                }
            }

            if (visible.Count <= MaxSamples)
            {
                return visible;
            }

            List<SamplePoint> samples = new List<SamplePoint>(MaxSamples);
            float step = (float)(visible.Count - 1) / (MaxSamples - 1);
            for (int i = 0; i < MaxSamples; i++)
            {
                int index = Mathf.RoundToInt(i * step);
                samples.Add(visible[index]);
            }

            return samples;
        }

        private static float ScoreAt(
            TextureData reference,
            TextureData layer,
            IList<SamplePoint> samples,
            int offsetX,
            int offsetY,
            out int comparedSamples)
        {
            int total = 0;
            comparedSamples = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                SamplePoint sample = samples[i];
                int referenceX = offsetX + sample.X;
                int referenceY = offsetY + sample.Y;
                if (!reference.Contains(referenceX, referenceY))
                {
                    continue;
                }

                Color32 referenceColor = reference.GetPixelTopLeft(referenceX, referenceY);
                Color32 layerColor = layer.GetPixelTopLeft(sample.X, sample.Y);
                total += Math.Abs(referenceColor.r - layerColor.r);
                total += Math.Abs(referenceColor.g - layerColor.g);
                total += Math.Abs(referenceColor.b - layerColor.b);
                comparedSamples++;
            }

            return comparedSamples > 0
                ? total / (comparedSamples * 3f)
                : float.MaxValue;
        }

        private static int GetSearchRadius(int imagePixels, float figmaPixels, float pixelScale)
        {
            float baseRadius = 32f * pixelScale;
            float sizeDifference = Mathf.Abs(imagePixels - figmaPixels);
            return Mathf.Clamp(Mathf.CeilToInt(baseRadius + sizeDifference), 8, 180);
        }

        private static Vector2 GetFallbackPixelScale(FigmaImportSettings settings)
        {
            float scale = settings != null ? Mathf.Max(0.01f, settings.ImageScale) : 1f;
            return new Vector2(scale, scale);
        }

        private struct SamplePoint
        {
            public readonly int X;
            public readonly int Y;
            public readonly Color32 Color;

            public SamplePoint(int x, int y, Color32 color)
            {
                X = x;
                Y = y;
                Color = color;
            }
        }

        private sealed class TextureData : IDisposable
        {
            private readonly Texture2D _texture;
            private readonly Color32[] _pixels;

            private TextureData(Texture2D texture)
            {
                _texture = texture;
                Width = texture.width;
                Height = texture.height;
                _pixels = texture.GetPixels32();
            }

            public int Width { get; private set; }
            public int Height { get; private set; }

            public static TextureData Load(string assetPath)
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

                return new TextureData(texture);
            }

            public Color32 GetPixelTopLeft(int x, int y)
            {
                return _pixels[(Height - 1 - y) * Width + x];
            }

            public bool Contains(int x, int y)
            {
                return x >= 0 && x < Width && y >= 0 && y < Height;
            }

            public void Dispose()
            {
                if (_texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(_texture);
                }
            }
        }
    }

    internal static class RectExtensions
    {
        public static bool HasUsableSize(this Rect rect)
        {
            return rect.width > 0.01f && rect.height > 0.01f;
        }
    }
}
