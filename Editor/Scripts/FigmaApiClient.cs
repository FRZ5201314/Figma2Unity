using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace FigmaUiImporter.Editor
{
    internal sealed class FigmaApiClient
    {
        private const string ApiBaseUrl = "https://api.figma.com/v1";
        private readonly string _accessToken;
        private readonly bool _ignoreSslCertificateErrors;
        private readonly Action<string> _statusReporter;

        static FigmaApiClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        public FigmaApiClient(string accessToken, bool ignoreSslCertificateErrors, Action<string> statusReporter = null)
        {
            _accessToken = accessToken;
            _ignoreSslCertificateErrors = ignoreSslCertificateErrors;
            _statusReporter = statusReporter;
        }

        public async Task<FigmaNodeInfo> LoadFileAsync(string fileKey, CancellationToken cancellationToken)
        {
            string json = await LoadFileJsonAsync(fileKey, cancellationToken);
            ReportStatus("Figma 文件结构读取完成，正在解析...");
            return FigmaDocumentParser.ParseFile(json);
        }

        public async Task<string> LoadFileJsonAsync(string fileKey, CancellationToken cancellationToken)
        {
            ReportStatus("正在请求 Figma 文件结构...");
            string url = ApiBaseUrl + "/files/" + Uri.EscapeDataString(fileKey);
            return await SendTextWithRetryAsync(url, true, cancellationToken);
        }

        public async Task<FigmaNodeInfo> LoadNodeTreeAsync(string fileKey, string nodeId, CancellationToken cancellationToken)
        {
            ReportStatus("正在读取所选节点结构...");
            string url = ApiBaseUrl
                + "/files/"
                + Uri.EscapeDataString(fileKey)
                + "/nodes?ids="
                + Uri.EscapeDataString(nodeId);
            string json = await SendTextWithRetryAsync(url, true, cancellationToken);
            ReportStatus("所选节点结构读取完成，正在解析...");
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

            ReportStatus("正在请求 Figma 图片地址：" + nodeIds.Count + " 个节点...");
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
            ReportStatus("Figma 图片地址读取完成。");
            return ParseImageUrls(json);
        }

        public async Task<byte[]> DownloadBytesAsync(string url, CancellationToken cancellationToken)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                ConfigureRequest(request);
                await SendRequestAsync(request, cancellationToken);
                return request.downloadHandler.data;
            }
        }

        private async Task<string> SendTextWithRetryAsync(string url, bool authenticated, CancellationToken cancellationToken)
        {
            const int maxAttempts = 5;
            Exception lastException = null;
            int retryDelayMs = 0;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReportStatus(string.Format("正在连接 Figma API（第 {0}/{1} 次）...", attempt, maxAttempts));

                try
                {
                    using (UnityWebRequest request = UnityWebRequest.Get(url))
                    {
                        ConfigureRequest(request);
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
                        if (exception.StatusCode == 429)
                        {
                            throw new FigmaApiException(
                                exception.StatusCode,
                                exception.RawMessage + "。Figma API 已限流，请等待一段时间后重试；插件已自动重试但仍未恢复。");
                        }

                        throw;
                    }

                    retryDelayMs = GetRetryDelayMs(exception, attempt);
                    ReportRetry(exception, retryDelayMs, attempt, maxAttempts);
                }
                catch (Exception exception)
                {
                    lastException = exception;
                    if (attempt == maxAttempts)
                    {
                        throw;
                    }

                    retryDelayMs = 1000 * attempt;
                    ReportStatus(string.Format("Figma 请求异常，等待 {0} 秒后重试（第 {1}/{2} 次）：{3}", retryDelayMs / 1000, attempt, maxAttempts, exception.Message));
                }

                await Task.Delay(retryDelayMs, cancellationToken);
            }

            throw lastException ?? new InvalidOperationException("Figma 请求失败。");
        }

        private void ConfigureRequest(UnityWebRequest request)
        {
            request.timeout = 60;
            if (_ignoreSslCertificateErrors)
            {
                request.certificateHandler = new TrustAllCertificateHandler();
                request.disposeCertificateHandlerOnDispose = true;
            }
        }

        private void ReportRetry(FigmaApiException exception, int retryDelayMs, int attempt, int maxAttempts)
        {
            int seconds = Math.Max(1, (retryDelayMs + 999) / 1000);
            if (exception.StatusCode == 429)
            {
                ReportStatus(string.Format("Figma API 已限流，等待 {0} 秒后自动重试（第 {1}/{2} 次）。", seconds, attempt, maxAttempts));
                return;
            }

            ReportStatus(string.Format("Figma API 请求失败，等待 {0} 秒后自动重试（第 {1}/{2} 次）：{3}", seconds, attempt, maxAttempts, exception.RawMessage));
        }

        private void ReportStatus(string message)
        {
            if (_statusReporter != null && !string.IsNullOrEmpty(message))
            {
                _statusReporter(message);
            }
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
                if (!string.IsNullOrEmpty(message)
                    && message.IndexOf("SSL", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    message += "。如果当前网络需要代理或自签证书，可在插件窗口勾选“忽略 SSL 证书错误”后重试。";
                }

                string retryAfter = request.GetResponseHeader("Retry-After");
                throw new FigmaApiException((int)request.responseCode, message, retryAfter);
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

        private static int GetRetryDelayMs(FigmaApiException exception, int attempt)
        {
            int retryAfterSeconds;
            if (!string.IsNullOrEmpty(exception.RetryAfter)
                && int.TryParse(exception.RetryAfter, NumberStyles.Integer, CultureInfo.InvariantCulture, out retryAfterSeconds))
            {
                return Math.Max(1000, Math.Min(120000, retryAfterSeconds * 1000));
            }

            if (exception.StatusCode == 429)
            {
                int[] delays = { 5000, 10000, 20000, 40000, 60000 };
                int index = Math.Max(0, Math.Min(delays.Length - 1, attempt - 1));
                return delays[index];
            }

            return 1000 * attempt;
        }
    }

    internal sealed class FigmaApiException : Exception
    {
        public int StatusCode { get; private set; }
        public string RawMessage { get; private set; }
        public string RetryAfter { get; private set; }

        public FigmaApiException(int statusCode, string message, string retryAfter = null)
            : base(string.Format("Figma API 请求失败 ({0})：{1}", statusCode, message))
        {
            StatusCode = statusCode;
            RawMessage = message;
            RetryAfter = retryAfter;
        }
    }

    internal sealed class TrustAllCertificateHandler : CertificateHandler
    {
        protected override bool ValidateCertificate(byte[] certificateData)
        {
            return true;
        }
    }
}
