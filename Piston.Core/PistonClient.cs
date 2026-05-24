using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Piston.Core.Models;

namespace Piston.Core
{
    public class PistonClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<PistonClient> _logger;
        private readonly int _maxRetries;
        private readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);

        private List<PistonRuntime> _runtimeCache;

        private const string DefaultBaseUrl = "https://emkc.org/api/v2/piston/";

        // ThreadStatic ensures thread-safety for Random without lock contention in C# 7.3
        [ThreadStatic]
        private static Random _localRandom;
        private static Random SafeRandom => _localRandom ?? (_localRandom = new Random(Guid.NewGuid().GetHashCode()));

        public PistonClient(HttpClient httpClient, ILogger<PistonClient> logger = null, int maxRetries = 3)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

            if (_httpClient.BaseAddress == null) _httpClient.BaseAddress = new Uri(DefaultBaseUrl);
            else if (!_httpClient.BaseAddress.ToString().EndsWith("/")) _httpClient.BaseAddress = new Uri(_httpClient.BaseAddress.ToString() + "/");

            _logger = logger;
            _maxRetries = Math.Max(0, maxRetries);
        }

        private async Task<HttpResponseMessage> SendWithRetriesAsync(Func<CancellationToken, Task<HttpResponseMessage>> action, CancellationToken cancellationToken)
        {
            Exception lastException = null;

            for (int attempt = 0; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    var resp = await action(cancellationToken).ConfigureAwait(false);

                    if (resp.IsSuccessStatusCode) return resp;

                    // 429 (Rate Limit) and 5xx (Server Error) are transient candidates
                    int statusCode = (int)resp.StatusCode;
                    if ((statusCode == 429 || statusCode >= 500) && attempt < _maxRetries)
                    {
                        _logger?.LogWarning("Transient error {StatusCode}, retrying... Attempt: {Attempt}", resp.StatusCode, attempt + 1);
                    }
                    else
                    {
                        string content = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new PistonException(resp.StatusCode, content);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw; // Always respect cancellation
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger?.LogWarning(ex, "Request attempt {Attempt} failed", attempt + 1);
                    if (attempt >= _maxRetries) break;
                }

                // Exponential backoff with Jitter to prevent thundering herd
                int delayMs = (int)(Math.Pow(2, attempt) * 200) + SafeRandom.Next(0, 100);
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }

            throw new PistonException("Request failed after retries", lastException);
        }

        public async Task<List<PistonRuntime>> GetRuntimesAsync(CancellationToken cancellationToken = default)
        {
            // Lock-free check for performance
            if (_runtimeCache != null) return _runtimeCache;

            await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Double-check pattern
                if (_runtimeCache != null) return _runtimeCache;

                var response = await SendWithRetriesAsync(ct => _httpClient.GetAsync("runtimes", ct), cancellationToken).ConfigureAwait(false);
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                _runtimeCache = JsonConvert.DeserializeObject<List<PistonRuntime>>(json) ?? new List<PistonRuntime>();
                return _runtimeCache;
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        public async Task<PistonRuntime> GetRuntimeAsync(string language, string version = "*", CancellationToken cancellationToken = default)
        {
            var runtimes = await GetRuntimesAsync(cancellationToken).ConfigureAwait(false);
            var runtime = runtimes.FirstOrDefault(r =>
                string.Equals(r.Language, language, StringComparison.OrdinalIgnoreCase) &&
                (version == "*" || string.Equals(r.Version, version, StringComparison.OrdinalIgnoreCase))
            ) ?? throw new PistonException($"Runtime not found for language '{language}' with version '{version}'.");
            return runtime;
        }

        public async Task<List<string>> GetLanguagesAsync(CancellationToken cancellationToken = default)
        {
            var runtimes = await GetRuntimesAsync(cancellationToken).ConfigureAwait(false);
            return runtimes.Select(r => r.Language).ToList();
        }

        public async Task<List<string>> GetVersionsAsync(string language, CancellationToken cancellationToken = default)
        {
            var runtimes = await GetRuntimesAsync(cancellationToken).ConfigureAwait(false);
            return runtimes.Where(r => string.Equals(r.Language, language, StringComparison.OrdinalIgnoreCase))
                           .Select(r => r.Version)
                           .ToList();
        }

        public async Task<PistonResult> ExecuteAsync(string language, string version, string sourceCode, string stdin = "", CancellationToken cancellationToken = default)
        {
            var file = PistonUtils.PrepareFileForSubmission(language, sourceCode, null);
            return await ExecuteAsync(language, version, new List<PistonFile> { file }, stdin, cancellationToken).ConfigureAwait(false);
        }

        public async Task<PistonResult> ExecuteAsync(string language, string version, List<PistonFile> files, string stdin = "", CancellationToken cancellationToken = default)
        {
            bool allBase64 = true;
            List<PistonFile> processedFiles = files.Select(f =>
                {
                    bool useBase64 = PistonUtils.ShouldUseBase64Encoding(language, f.Name);

                    if (!useBase64 && allBase64) allBase64 = false;

                    return new PistonFile
                    {
                        Name = f.Name,
                        Encoding = useBase64 ? "base64" : "utf8",
                        Content = useBase64
                            ? Convert.ToBase64String(Encoding.UTF8.GetBytes(f.Content ?? string.Empty))
                            : f.Content
                    };
                }).ToList();

            if (allBase64)
            {
                throw new PistonException("All files are using base64 encoding, which is not allowed. Please check file extensions and content size against PistonUtils.ShouldUseBase64Encoding criteria.");
            }

            var requestBody = new PistonRequest
            {
                Language = language,
                Version = version,
                Files = processedFiles,
                Stdin = stdin
            };

            string jsonPayload = JsonConvert.SerializeObject(requestBody);
            using (var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json"))
            {
                var response = await SendWithRetriesAsync(ct => _httpClient.PostAsync("execute", httpContent, ct), cancellationToken).ConfigureAwait(false);
                string jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonConvert.DeserializeObject<PistonResult>(jsonResponse);
            }
        }

        public async Task<PistonResult> ExecuteStreamingAsync(
            string language,
            string version,
            List<PistonFile> files,
            Action<string> onStdout = null,
            Action<string> onStderr = null,
            Action<string> onStage = null,
            CancellationToken cancellationToken = default)
        {
            using (var ws = new ClientWebSocket())
            {
                var builder = new UriBuilder(_httpClient.BaseAddress)
                {
                    Scheme = _httpClient.BaseAddress.Scheme == "https" ? "wss" : "ws"
                };
                builder.Path = builder.Path.TrimEnd('/') + "/connect";

                await ws.ConnectAsync(builder.Uri, cancellationToken).ConfigureAwait(false);

                // Send init message
                var initObj = new { type = "init", language, version, files };
                var initBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(initObj));
                await ws.SendAsync(new ArraySegment<byte>(initBytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);

                var buffer = new byte[4096];
                var finalResult = new PistonResult();
                var msgBuilder = new StringBuilder();

                while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    WebSocketReceiveResult result;
                    msgBuilder.Clear();

                    // Handle fragmented messages
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close) break;

                        msgBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close || msgBuilder.Length == 0) break;

                    ParseWsMessage(msgBuilder.ToString(), finalResult, onStdout, onStderr, onStage);
                }

                return finalResult;
            }
        }

        private void ParseWsMessage(string json, PistonResult result, Action<string> onStdout, Action<string> onStderr, Action<string> onStage)
        {
            try
            {
                var msg = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (msg == null || !msg.TryGetValue("type", out var typeObj)) return;

                string type = typeObj.ToString();
                switch (type)
                {
                    case "data":
                        msg.TryGetValue("stream", out var stream);
                        msg.TryGetValue("data", out var data);
                        if (stream?.ToString() == "stdout") onStdout?.Invoke(data?.ToString());
                        else if (stream?.ToString() == "stderr") onStderr?.Invoke(data?.ToString());
                        break;

                    case "stage":
                        if (msg.TryGetValue("stage", out var s)) onStage?.Invoke(s.ToString());
                        break;

                    case "exit":
                        var stageResult = new PistonStageResult();
                        if (msg.TryGetValue("code", out var c)) stageResult.Code = Convert.ToInt32(c);
                        if (msg.TryGetValue("output", out var o)) stageResult.Output = o.ToString();

                        msg.TryGetValue("stage", out var stageName);
                        if (stageName?.ToString() == "run") result.Run = stageResult;
                        else result.Compile = stageResult;
                        break;
                }
            }
            catch { /* Silently drop malformed frames */ }
        }

        public void Dispose()
        {
            _cacheLock?.Dispose();
        }
    }
}
