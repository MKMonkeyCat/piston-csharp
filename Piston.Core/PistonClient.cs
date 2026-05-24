using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
            return await ExecuteAsync(language, version, new List<PistonFile> { file }, stdin, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<PistonResult> ExecuteAsync(string language, string version, string sourceCode, PistonExecuteOptions options,
            CancellationToken cancellationToken = default)
        {
            var file = PistonUtils.PrepareFileForSubmission(language, sourceCode, null);
            return await ExecuteAsync(language, version, new List<PistonFile> { file }, options, cancellationToken).ConfigureAwait(false);
        }

        public async Task<PistonResult> ExecuteAsync(string language, string version, List<PistonFile> files, PistonExecuteOptions options,
            CancellationToken cancellationToken = default)
        {
            string stdinValue = string.Empty;
            if (options != null)
            {
                if (options.StdinLines != null && options.StdinLines.Count > 0) stdinValue = string.Join("\n", options.StdinLines);
                else stdinValue = options.Stdin ?? string.Empty;
            }

            return await ExecuteAsync(language, version, files,
                stdin: stdinValue,
                args: options?.Args,
                runTimeout: options?.RunTimeout,
                compileTimeout: options?.CompileTimeout,
                runMemoryLimit: options?.RunMemoryLimit,
                compileMemoryLimit: options?.CompileMemoryLimit,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<PistonResult> ExecuteAsync(string language, string version, List<PistonFile> files, string stdin = "", List<string> args = null,
            int? runTimeout = null, int? compileTimeout = null, int? runMemoryLimit = null, int? compileMemoryLimit = null,
            CancellationToken cancellationToken = default)
        {
            bool allBase64 = true;
            List<PistonFile> processedFiles = files.Select(f =>
                {
                    string validatedName = PistonUtils.ValidateFilename(f.Name);
                    bool useBase64 = PistonUtils.ShouldUseBase64Encoding(language, validatedName);

                    if (!useBase64 && allBase64) allBase64 = false;

                    return new PistonFile
                    {
                        Name = validatedName,
                        Encoding = useBase64 ? "base64" : "utf8",
                        Content = useBase64 ? Convert.ToBase64String(Encoding.UTF8.GetBytes(f.Content ?? string.Empty)) : f.Content
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
                Stdin = stdin,
                Args = args,
                RunTimeout = runTimeout,
                CompileTimeout = compileTimeout,
                RunMemoryLimit = runMemoryLimit,
                CompileMemoryLimit = compileMemoryLimit
            };

            string jsonPayload = JsonConvert.SerializeObject(requestBody);
            using (var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json"))
            {
                var response = await SendWithRetriesAsync(ct => _httpClient.PostAsync("execute", httpContent, ct), cancellationToken).ConfigureAwait(false);
                string jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonConvert.DeserializeObject<PistonResult>(jsonResponse);
            }
        }

        public void Dispose()
        {
            _cacheLock?.Dispose();
        }
    }
}
