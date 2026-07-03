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
        private const int MaxAutomaticRateLimitDelayMs = 30000;
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

            return await GetImageUrlsBatchAsync(fileKey, nodeIds, format, scale, cancellationToken);
        }

        private async Task<Dictionary<string, string>> GetImageUrlsBatchAsync(
            string fileKey,
            IList<string> nodeIds,
            string format,
            float scale,
            CancellationToken cancellationToken)
        {
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

            try
            {
                string json = await SendTextWithRetryAsync(url, true, cancellationToken);
                ReportStatus("Figma 图片地址读取完成。");
                return ParseImageUrls(json);
            }
            catch (FigmaApiException exception)
            {
                if (!IsImageRenderTimeout(exception))
                {
                    throw;
                }

                if (nodeIds.Count <= 1)
                {
                    throw new FigmaApiException(
                        exception.StatusCode,
                        exception.RawMessage + "。该节点在 Figma 服务端渲染超时，可尝试降低图片倍率、拆小节点或改用本地 Figma 插件包导入。",
                        exception.RetryAfter);
                }

                int firstCount = Math.Max(1, nodeIds.Count / 2);
                int secondCount = nodeIds.Count - firstCount;
                ReportStatus(string.Format(
                    "Figma 图片渲染超时，正在拆分为 {0}+{1} 个节点重试...",
                    firstCount,
                    secondCount));

                Dictionary<string, string> urls = new Dictionary<string, string>();
                MergeImageUrls(urls, await GetImageUrlsBatchAsync(
                    fileKey,
                    CopyNodeIds(nodeIds, 0, firstCount),
                    format,
                    scale,
                    cancellationToken));
                MergeImageUrls(urls, await GetImageUrlsBatchAsync(
                    fileKey,
                    CopyNodeIds(nodeIds, firstCount, secondCount),
                    format,
                    scale,
                    cancellationToken));
                return urls;
            }
        }

        public async Task<byte[]> DownloadBytesAsync(string url, CancellationToken cancellationToken)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                ConfigureRequest(request);
                await SendRequestAsync(request, cancellationToken, false);
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

                        await SendRequestAsync(request, cancellationToken, true);
                        return request.downloadHandler.text;
                    }
                }
                catch (FigmaApiException exception)
                {
                    lastException = exception;
                    int requiredRateLimitDelayMs;
                    if (ShouldStopForLongRateLimit(exception, out requiredRateLimitDelayMs))
                    {
                        throw CreateLongRateLimitException(exception, requiredRateLimitDelayMs);
                    }

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

        private static async Task SendRequestAsync(
            UnityWebRequest request,
            CancellationToken cancellationToken,
            bool preferResponseText)
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
                string message = BuildRequestFailureMessage(request, preferResponseText);
                if (!string.IsNullOrEmpty(message)
                    && message.IndexOf("SSL", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    message += "。如果当前网络需要代理或自签证书，可在插件窗口勾选“忽略 SSL 证书错误”后重试。";
                }

                string retryAfter = request.GetResponseHeader("Retry-After");
                throw new FigmaApiException((int)request.responseCode, message, retryAfter);
            }
        }

        private static string BuildRequestFailureMessage(UnityWebRequest request, bool preferResponseText)
        {
            string responseText = null;
            if (preferResponseText || IsTextResponse(request))
            {
                responseText = request.downloadHandler != null ? request.downloadHandler.text : null;
                if (LooksLikeBinaryText(responseText))
                {
                    responseText = null;
                }
            }

            if (!string.IsNullOrWhiteSpace(responseText))
            {
                return responseText.Trim();
            }

            if (!string.IsNullOrWhiteSpace(request.error))
            {
                return request.error;
            }

            string contentType = request.GetResponseHeader("Content-Type");
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                return "请求失败，HTTP 状态码 " + request.responseCode + "，响应类型：" + contentType;
            }

            return "请求失败，HTTP 状态码 " + request.responseCode + "。";
        }

        private static bool IsTextResponse(UnityWebRequest request)
        {
            string contentType = request.GetResponseHeader("Content-Type");
            if (string.IsNullOrWhiteSpace(contentType))
            {
                return false;
            }

            return contentType.IndexOf("text/", StringComparison.OrdinalIgnoreCase) >= 0
                || contentType.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0
                || contentType.IndexOf("xml", StringComparison.OrdinalIgnoreCase) >= 0
                || contentType.IndexOf("javascript", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeBinaryText(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            int suspicious = 0;
            int inspected = Math.Min(value.Length, 512);
            for (int i = 0; i < inspected; i++)
            {
                char c = value[i];
                if (c == '\uFFFD' || (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t'))
                {
                    suspicious++;
                }
            }

            return suspicious >= 4 || suspicious * 6 > inspected;
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

        private static bool IsImageRenderTimeout(FigmaApiException exception)
        {
            if (exception == null || exception.StatusCode != 400 || string.IsNullOrEmpty(exception.RawMessage))
            {
                return false;
            }

            return exception.RawMessage.IndexOf("Render timeout", StringComparison.OrdinalIgnoreCase) >= 0
                || exception.RawMessage.IndexOf("fewer or smaller images", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<string> CopyNodeIds(IList<string> nodeIds, int start, int count)
        {
            List<string> copy = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                copy.Add(nodeIds[start + i]);
            }

            return copy;
        }

        private static void MergeImageUrls(Dictionary<string, string> target, Dictionary<string, string> source)
        {
            foreach (KeyValuePair<string, string> pair in source)
            {
                target[pair.Key] = pair.Value;
            }
        }

        private static int GetRetryDelayMs(FigmaApiException exception, int attempt)
        {
            int retryAfterDelayMs;
            if (TryGetRetryAfterDelayMs(exception.RetryAfter, out retryAfterDelayMs))
            {
                return Math.Max(1000, Math.Min(MaxAutomaticRateLimitDelayMs, retryAfterDelayMs));
            }

            if (exception.StatusCode == 429)
            {
                int[] delays = { 5000, 10000, 20000, 30000, 30000 };
                int index = Math.Max(0, Math.Min(delays.Length - 1, attempt - 1));
                return delays[index];
            }

            return 1000 * attempt;
        }

        private static bool ShouldStopForLongRateLimit(FigmaApiException exception, out int retryAfterDelayMs)
        {
            retryAfterDelayMs = 0;
            return exception != null
                && exception.StatusCode == 429
                && TryGetRetryAfterDelayMs(exception.RetryAfter, out retryAfterDelayMs)
                && retryAfterDelayMs > MaxAutomaticRateLimitDelayMs;
        }

        private static FigmaApiException CreateLongRateLimitException(FigmaApiException exception, int retryAfterDelayMs)
        {
            return new FigmaApiException(
                exception.StatusCode,
                exception.RawMessage
                    + "。Figma REST API 已限流，服务端要求等待约 "
                    + FormatDelay(retryAfterDelayMs)
                    + "。加载文件、读取节点和请求图片地址都会使用 REST；如果当前正在导入，通常是图片地址请求触发。插件已停止长时间自动等待，请稍后重试；也可以减少一次导入的节点数量，或改用 Figma 插件包本地导入。",
                exception.RetryAfter);
        }

        private static string FormatDelay(int delayMs)
        {
            int totalSeconds = Math.Max(1, (delayMs + 999) / 1000);
            int days = totalSeconds / 86400;
            int hours = totalSeconds % 86400 / 3600;
            int minutes = totalSeconds % 3600 / 60;
            int seconds = totalSeconds % 60;

            if (days > 0)
            {
                return string.Format("{0} 天 {1} 小时", days, hours);
            }

            if (hours > 0)
            {
                return string.Format("{0} 小时 {1} 分钟", hours, minutes);
            }

            if (minutes > 0)
            {
                return string.Format("{0} 分钟 {1} 秒", minutes, seconds);
            }

            return seconds + " 秒";
        }

        private static bool TryGetRetryAfterDelayMs(string retryAfter, out int delayMs)
        {
            delayMs = 0;
            if (string.IsNullOrWhiteSpace(retryAfter))
            {
                return false;
            }

            int retryAfterSeconds;
            if (int.TryParse(retryAfter, NumberStyles.Integer, CultureInfo.InvariantCulture, out retryAfterSeconds))
            {
                delayMs = Math.Max(0, retryAfterSeconds * 1000);
                return true;
            }

            DateTimeOffset retryAfterDate;
            if (DateTimeOffset.TryParse(
                retryAfter,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out retryAfterDate))
            {
                delayMs = Math.Max(0, (int)(retryAfterDate.UtcDateTime - DateTime.UtcNow).TotalMilliseconds);
                return true;
            }

            return false;
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
