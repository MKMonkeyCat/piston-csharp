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
    public class PistonClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<PistonClient> _logger;
        private readonly int _maxRetries;
        private readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);
        private List<PistonRuntime> _runtimeCache;
        private List<string> _languageCache;
        private const string DefaultBaseUrl = "https://emkc.org/api/v2/piston";

        public PistonClient(HttpClient httpClient = null, ILogger<PistonClient> logger = null, int maxRetries = 3)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            if (_httpClient.BaseAddress == null)
            {
                _httpClient.BaseAddress = new Uri(DefaultBaseUrl);
            }
            _logger = logger;
            _maxRetries = Math.Max(0, maxRetries);
        }

        private async Task<HttpResponseMessage> SendWithRetriesAsync(Func<CancellationToken, Task<HttpResponseMessage>> action, CancellationToken cancellationToken)
        {
            Exception lastException = null;
            HttpResponseMessage lastResponse = null;

            for (int attempt = 0; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    var resp = await action(cancellationToken).ConfigureAwait(false);
                    lastResponse = resp;
                    if (resp.IsSuccessStatusCode)
                    {
                        return resp;
                    }

                    // Treat 5xx as transient
                    if ((int)resp.StatusCode >= 500 && attempt < _maxRetries)
                    {
                        _logger?.LogWarning("Transient server error {StatusCode}, attempt {Attempt}", resp.StatusCode, attempt + 1);
                        // fallthrough to retry
                    }
                    else
                    {
                        string content = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new PistonException(resp.StatusCode, content);
                    }
                }
                catch (OperationCanceledException oce)
                {
                    // Respect cancellation requests
                    _logger?.LogInformation(oce, "Request cancelled");
                    throw new PistonException("Request cancelled", oce);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger?.LogWarning(ex, "Request attempt {Attempt} failed", attempt + 1);
                    if (attempt >= _maxRetries) break;
                }

                // Exponential backoff with jitter
                int delayMs = (int)(Math.Pow(2, attempt) * 100) + new Random().Next(0, 100);
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }

            if (lastException != null)
            {
                throw new PistonException("Request failed after retries", lastException);
            }

            if (lastResponse != null)
            {
                string content = await lastResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new PistonException(lastResponse.StatusCode, content);
            }

            throw new PistonException("Request failed");
        }

        private async Task<List<PistonRuntime>> FetchRuntimesAsync(CancellationToken cancellationToken)
        {
            var response = await SendWithRetriesAsync(ct => _httpClient.GetAsync("runtimes", ct), cancellationToken).ConfigureAwait(false);
            string jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonConvert.DeserializeObject<List<PistonRuntime>>(jsonResponse) ?? new List<PistonRuntime>();
        }

        public async Task<List<PistonRuntime>> GetRuntimesAsync(CancellationToken cancellationToken = default)
        {
            if (_runtimeCache != null) return new List<PistonRuntime>(_runtimeCache);

            await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_runtimeCache != null) return new List<PistonRuntime>(_runtimeCache);

                _runtimeCache = await FetchRuntimesAsync(cancellationToken).ConfigureAwait(false);
                _languageCache = null;
                return new List<PistonRuntime>(_runtimeCache);
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        public async Task<List<string>> GetLanguagesAsync(CancellationToken cancellationToken = default)
        {
            if (_languageCache != null)
            {
                return new List<string>(_languageCache);
            }

            await _cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_languageCache != null) return new List<string>(_languageCache);

                if (_runtimeCache == null) _runtimeCache = await FetchRuntimesAsync(cancellationToken).ConfigureAwait(false);

                _languageCache = _runtimeCache
                    .Where(runtime => !string.IsNullOrWhiteSpace(runtime?.Language))
                    .Select(runtime => runtime.Language)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new List<string>(_languageCache);
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        public async Task<PistonResult> ExecuteAsync(string language, string version, string sourceCode, CancellationToken cancellationToken = default)
        {
            var files = PistonUtils.PrepareFilesForSubmission(language, sourceCode);

            return await ExecuteAsync(language, version, files, string.Empty, cancellationToken).ConfigureAwait(false);
        }

        public async Task<PistonResult> ExecuteAsync(string language, string version, List<PistonFile> files, string stdin = "", CancellationToken cancellationToken = default)
        {
            var requestBody = new PistonRequest
            {
                Language = language,
                Version = version,
                Files = files,
                Stdin = stdin
            };

            string jsonPayload = JsonConvert.SerializeObject(requestBody);
            StringContent httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var response = await SendWithRetriesAsync(ct => _httpClient.PostAsync("execute", httpContent, ct), cancellationToken).ConfigureAwait(false);

            string jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonConvert.DeserializeObject<PistonResult>(jsonResponse);
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
            var ws = new ClientWebSocket();

            if (_httpClient.BaseAddress == null)
            {
                throw new InvalidOperationException("HttpClient BaseAddress is not set");
            }

            var builder = new UriBuilder(_httpClient.BaseAddress)
            {
                Scheme = _httpClient.BaseAddress.Scheme == "https" ? "wss" : "ws"
            };

            var basePath = builder.Path?.TrimEnd('/') ?? string.Empty;
            builder.Path = string.IsNullOrEmpty(basePath) ? "connect" : basePath + "/connect";
            var wsUri = builder.Uri;

            await ws.ConnectAsync(wsUri, cancellationToken).ConfigureAwait(false);

            try
            {
                var initObj = new { type = "init", language, version, files, stdin = string.Empty };

                var initJson = JsonConvert.SerializeObject(initObj);
                var initBytes = Encoding.UTF8.GetBytes(initJson);
                await ws.SendAsync(new ArraySegment<byte>(initBytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);

                var buffer = new ArraySegment<byte>(new byte[8192]);
                var sb = new StringBuilder();

                PistonResult finalResult = new PistonResult();

                while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    WebSocketReceiveResult recv;
                    sb.Clear();
                    do
                    {
                        recv = await ws.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                        if (recv.MessageType == WebSocketMessageType.Close)
                        {
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", cancellationToken).ConfigureAwait(false);
                            break;
                        }

                        var chunk = Encoding.UTF8.GetString(buffer.Array, buffer.Offset, recv.Count);
                        sb.Append(chunk);
                    } while (!recv.EndOfMessage);

                    if (sb.Length == 0) continue;

                    try
                    {
                        var msg = JsonConvert.DeserializeObject<Dictionary<string, object>>(sb.ToString());
                        if (msg == null || !msg.TryGetValue("type", out var typeObj)) continue;
                        var type = typeObj.ToString();

                        switch (type)
                        {
                            case "data":
                                var stream = msg.ContainsKey("stream") ? msg["stream"]?.ToString() : null;
                                var data = msg.ContainsKey("data") ? msg["data"]?.ToString() : null;
                                if (stream == "stdout") onStdout?.Invoke(data ?? string.Empty);
                                else if (stream == "stderr") onStderr?.Invoke(data ?? string.Empty);
                                break;
                            case "stage":
                                var stage = msg.ContainsKey("stage") ? msg["stage"]?.ToString() : null;
                                if (stage != null) onStage?.Invoke(stage);
                                break;
                            case "exit":
                                var stageName = msg.ContainsKey("stage") ? msg["stage"]?.ToString() : null;
                                var stageResult = new PistonStageResult();
                                if (msg.ContainsKey("code") && int.TryParse(msg["code"]?.ToString(), out var code))
                                {
                                    stageResult.Code = code;
                                }
                                if (msg.ContainsKey("signal")) stageResult.Signal = msg["signal"]?.ToString();
                                if (msg.ContainsKey("output")) stageResult.Output = msg["output"]?.ToString();

                                if (string.Equals(stageName, "run", StringComparison.OrdinalIgnoreCase))
                                {
                                    finalResult.Run = stageResult;
                                }
                                else if (string.Equals(stageName, "compile", StringComparison.OrdinalIgnoreCase))
                                {
                                    finalResult.Compile = stageResult;
                                }
                                break;
                            case "runtime":
                                break;
                            case "error":
                                var message = msg.ContainsKey("message") ? msg["message"]?.ToString() : "";
                                throw new PistonException(message);
                        }
                    }
                    catch (JsonException)
                    {
                        // ignore unparsable messages
                    }
                }

                return finalResult;
            }
            finally
            {
                try { ws.Dispose(); } catch { }
            }
        }
    }
}
