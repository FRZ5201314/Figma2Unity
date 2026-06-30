using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace FigmaUiImporter.Editor
{
    internal sealed class FigmaApiClient
    {
        private const string ApiBaseUrl = "https://api.figma.com/v1";
        private readonly string _accessToken;

        public FigmaApiClient(string accessToken)
        {
            _accessToken = accessToken;
        }

        public async Task<FigmaNodeInfo> LoadFileAsync(string fileKey, CancellationToken cancellationToken)
        {
            string url = ApiBaseUrl + "/files/" + Uri.EscapeDataString(fileKey);
            string json = await SendTextWithRetryAsync(url, true, cancellationToken);
            return FigmaDocumentParser.ParseFile(json);
        }

        public async Task<FigmaNodeInfo> LoadNodeTreeAsync(string fileKey, string nodeId, CancellationToken cancellationToken)
        {
            string url = ApiBaseUrl
                + "/files/"
                + Uri.EscapeDataString(fileKey)
                + "/nodes?ids="
                + Uri.EscapeDataString(nodeId);
            string json = await SendTextWithRetryAsync(url, true, cancellationToken);
            return FigmaDocumentParser.ParseNodeResponse(json, nodeId);
        }

        public async Task<Dictionary<string, string>> GetImageUrlsAsync(
            string fileKey,
            IList<string> nodeIds,
            string format,
            float scale,
            CancellationToken cancellationToken)
        {
            if (nodeIds == null || nodeIds.Count == 0)
            {
                return new Dictionary<string, string>();
            }

            string joinedIds = string.Join(",", nodeIds);
            string scaleText = Math.Max(0.01f, Math.Min(4f, scale)).ToString("0.###", CultureInfo.InvariantCulture);
            string url = ApiBaseUrl
                + "/images/"
                + Uri.EscapeDataString(fileKey)
                + "?ids="
                + Uri.EscapeDataString(joinedIds)
                + "&format="
                + Uri.EscapeDataString(format)
                + "&scale="
                + scaleText;

            string json = await SendTextWithRetryAsync(url, true, cancellationToken);
            return ParseImageUrls(json);
        }

        public async Task<byte[]> DownloadBytesAsync(string url, CancellationToken cancellationToken)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                await SendRequestAsync(request, cancellationToken);
                return request.downloadHandler.data;
            }
        }

        private async Task<string> SendTextWithRetryAsync(string url, bool authenticated, CancellationToken cancellationToken)
        {
            const int maxAttempts = 3;
            Exception lastException = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using (UnityWebRequest request = UnityWebRequest.Get(url))
                    {
                        if (authenticated)
                        {
                            request.SetRequestHeader("X-Figma-Token", _accessToken);
                        }

                        await SendRequestAsync(request, cancellationToken);
                        return request.downloadHandler.text;
                    }
                }
                catch (FigmaApiException exception)
                {
                    lastException = exception;
                    if (!ShouldRetry(exception.StatusCode) || attempt == maxAttempts)
                    {
                        throw;
                    }
                }
                catch (Exception exception)
                {
                    lastException = exception;
                    if (attempt == maxAttempts)
                    {
                        throw;
                    }
                }

                await Task.Delay(300 * attempt, cancellationToken);
            }

            throw lastException ?? new InvalidOperationException("Figma 请求失败。");
        }

        private static async Task SendRequestAsync(UnityWebRequest request, CancellationToken cancellationToken)
        {
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(50, cancellationToken);
            }

            bool failed;
#if UNITY_2020_2_OR_NEWER
            failed = request.result == UnityWebRequest.Result.ConnectionError
                || request.result == UnityWebRequest.Result.ProtocolError
                || request.result == UnityWebRequest.Result.DataProcessingError;
#else
            failed = request.isNetworkError || request.isHttpError;
#endif

            if (failed)
            {
                string message = string.IsNullOrEmpty(request.downloadHandler.text)
                    ? request.error
                    : request.downloadHandler.text;
                throw new FigmaApiException((int)request.responseCode, message);
            }
        }

        private static Dictionary<string, string> ParseImageUrls(string json)
        {
            Dictionary<string, object> root = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (root == null)
            {
                throw new InvalidOperationException("Figma 图片接口返回内容无法解析。");
            }

            object error;
            if (root.TryGetValue("err", out error) && error != null)
            {
                string errorText = Convert.ToString(error);
                if (!string.IsNullOrEmpty(errorText))
                {
                    throw new InvalidOperationException(errorText);
                }
            }

            Dictionary<string, object> images = null;
            object imagesObject;
            if (root.TryGetValue("images", out imagesObject))
            {
                images = imagesObject as Dictionary<string, object>;
            }

            Dictionary<string, string> urls = new Dictionary<string, string>();
            if (images == null)
            {
                return urls;
            }

            foreach (KeyValuePair<string, object> pair in images)
            {
                string url = pair.Value == null ? string.Empty : Convert.ToString(pair.Value);
                if (!string.IsNullOrEmpty(url))
                {
                    urls[pair.Key] = url;
                }
            }

            return urls;
        }

        private static bool ShouldRetry(int statusCode)
        {
            return statusCode == 429 || statusCode >= 500 || statusCode == 0;
        }
    }

    internal sealed class FigmaApiException : Exception
    {
        public int StatusCode { get; private set; }

        public FigmaApiException(int statusCode, string message)
            : base(string.Format("Figma API 请求失败 ({0})：{1}", statusCode, message))
        {
            StatusCode = statusCode;
        }
    }
}
