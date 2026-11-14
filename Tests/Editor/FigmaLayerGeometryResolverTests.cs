using NUnit.Framework;
using UnityEngine;

namespace FigmaUiImporter.Editor.Tests
{
    /// <summary>验证 v2 manifest 的 exportBounds 会直接决定图片图层的尺寸和位置。</summary>
    public sealed class FigmaLayerGeometryResolverTests
    {
        [Test]
        public void ResolveUsesExportBoundsWithoutLoadingTexture()
        {
            FigmaNodeInfo root = new FigmaNodeInfo
            {
                Id = "1:2",
                AbsoluteBounds = new Rect(0f, 0f, 1920f, 1080f),
                HasAbsoluteBounds = true
            };

            ImportedLayerInfo layer = new ImportedLayerInfo
            {
                NodeId = "3:4",
                NodeType = "SECTION",
                // 文件不存在也没关系：exportBounds 路径会在加载纹理之前返回。
                AssetPath = "Assets/FigmaUiImporterResolverTests/not-needed.png",
                FigmaBounds = new Rect(0f, 0f, 1920f, 1080f),
                SizeDelta = new Vector2(1920f, 1080f),
                ExportBounds = new Rect(-51f, -40f, 2011f, 1161f),
                HasExportBounds = true,
                ExportBoundsExact = true
            };

            FigmaImportSettings settings = new FigmaImportSettings { ImageScale = 1f };
            using (FigmaLayerGeometryResolver resolver = new FigmaLayerGeometryResolver(settings, root, string.Empty))
            {
                resolver.Resolve(layer);
            }

            Assert.AreEqual(new Vector2(2011f, 1161f), layer.SizeDelta);
            Assert.AreEqual(new Vector2(-5.5f, -0.5f), layer.AnchoredPosition);
        }
    }
}
