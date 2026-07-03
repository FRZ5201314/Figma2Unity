using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FigmaUiImporter.Editor
{
    internal sealed class FigmaImageExporter
    {
        private const int BatchSize = 20;
        private readonly FigmaApiClient _client;
        private readonly UnitySpriteAssetImporter _spriteImporter;

        public FigmaImageExporter(FigmaApiClient client, UnitySpriteAssetImporter spriteImporter)
        {
            _client = client;
            _spriteImporter = spriteImporter;
        }

        public async Task<string> ExportSingleImageAsync(
            FigmaImportSettings settings,
            string nodeId,
            string assetPath,
            IProgress<FigmaImportProgress> progress,
            CancellationToken cancellationToken)
        {
            progress.Report(new FigmaImportProgress("请求参考图地址...", 0.24f));
            Dictionary<string, string> urls = await _client.GetImageUrlsAsync(
                settings.FileKey,
                new List<string> { nodeId },
                "png",
                settings.ImageScale,
                cancellationToken);

            string url;
            if (!urls.TryGetValue(nodeId, out url) || string.IsNullOrEmpty(url))
            {
                throw new System.InvalidOperationException("Figma 没有返回参考图地址。");
            }

            progress.Report(new FigmaImportProgress("下载参考图...", 0.32f));
            await DownloadToAssetPathAsync(url, assetPath, cancellationToken);

            _spriteImporter.ImportSprites(new List<string> { assetPath }, settings.CompressionMode);
            return assetPath;
        }

        public async Task<Dictionary<string, string>> ExportLayerImagesAsync(
            FigmaImportSettings settings,
            IList<FigmaNodeInfo> nodes,
            string layersFolder,
            IProgress<FigmaImportProgress> progress,
            CancellationToken cancellationToken)
        {
            Dictionary<string, string> exported = new Dictionary<string, string>();
            if (nodes == null || nodes.Count == 0)
            {
                return exported;
            }

            FigmaPathUtility.EnsureAssetFolder(layersFolder);
            List<string> importedAssetPaths = new List<string>();

            for (int start = 0; start < nodes.Count; start += BatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int count = System.Math.Min(BatchSize, nodes.Count - start);
                List<string> nodeIds = new List<string>(count);
                for (int i = 0; i < count; i++)
                {
                    nodeIds.Add(nodes[start + i].Id);
                }

                float batchStartProgress = 0.42f + 0.38f * start / nodes.Count;
                progress.Report(new FigmaImportProgress("请求切图地址 " + (start + count) + "/" + nodes.Count + "...", batchStartProgress));

                Dictionary<string, string> urls = await _client.GetImageUrlsAsync(
                    settings.FileKey,
                    nodeIds,
                    "png",
                    settings.ImageScale,
                    cancellationToken);

                for (int i = 0; i < count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    FigmaNodeInfo node = nodes[start + i];
                    string url;
                    if (!urls.TryGetValue(node.Id, out url) || string.IsNullOrEmpty(url))
                    {
                        continue;
                    }

                    string assetPath = FigmaPathUtility.CombineAssetPath(
                        layersFolder,
                        FigmaPathUtility.SafeFileName(node.Id) + ".png");
                    float downloadProgress = 0.42f + 0.38f * (start + i + 1) / nodes.Count;
                    progress.Report(new FigmaImportProgress("下载切图 " + (start + i + 1) + "/" + nodes.Count + "...", downloadProgress));

                    await DownloadToAssetPathAsync(url, assetPath, cancellationToken);
                    exported[node.Id] = assetPath;
                    importedAssetPaths.Add(assetPath);
                }
            }

            progress.Report(new FigmaImportProgress("导入 Sprite 资源...", 0.84f));
            _spriteImporter.ImportSprites(importedAssetPaths, settings.CompressionMode);
            return exported;
        }

        private async Task DownloadToAssetPathAsync(string url, string assetPath, CancellationToken cancellationToken)
        {
            string fullPath = FigmaPathUtility.ToFullPath(assetPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            byte[] bytes = await _client.DownloadBytesAsync(url, cancellationToken);
            File.WriteAllBytes(fullPath, bytes);
        }
    }
}
