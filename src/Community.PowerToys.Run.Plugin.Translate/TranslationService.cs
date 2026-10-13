// Copyright (c) Joont. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Community.PowerToys.Run.Plugin.Translate.Providers;

namespace Community.PowerToys.Run.Plugin.Translate
{
    /// <summary>
    /// Turns a <see cref="TranslationRequest"/> into a <see cref="TranslationResult"/>.
    /// <para>
    /// PowerToys Run issues a query for every keystroke, and the Baidu free tier only allows roughly
    /// one request per second with a monthly character quota. Caching, request coalescing, throttling
    /// and cancelling superseded requests are therefore correctness features, not optimizations.
    /// </para>
    /// </summary>
    internal sealed class TranslationService : IDisposable
    {
        private const int MaxCacheEntries = 200;

        private static readonly HttpClient SharedHttp = CreateHttpClient();

        private readonly PluginConfig _config;
        private readonly Dictionary<string, ITranslationProvider> _providers;

        private readonly ConcurrentDictionary<string, TranslationOutcome> _cache = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _cacheOrder = new();
        private readonly ConcurrentDictionary<string, Task<TranslationOutcome>> _inflight = new(StringComparer.Ordinal);

        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly object _pendingLock = new();

        private CancellationTokenSource _pending = new();
        private DateTime _lastRequestUtc = DateTime.MinValue;
        private bool _disposed;

        public TranslationService(PluginConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            var baidu = new BaiduTranslateProvider(SharedHttp, config.Baidu.AppId, config.Baidu.SecretKey);
            var google = new GoogleFreeTranslateProvider(SharedHttp);

            _providers = new Dictionary<string, ITranslationProvider>(StringComparer.Ordinal)
            {
                [baidu.Id] = baidu,
                [google.Id] = google,
            };

            PrimaryProviderName = _providers.TryGetValue(config.Provider ?? PluginConfig.ProviderBaidu, out ITranslationProvider? primary)
                ? primary.DisplayName
                : string.Empty;
        }

        /// <summary>Gets the display name of the configured primary provider.</summary>
        public string PrimaryProviderName { get; }

        /// <summary>
        /// Cancels the request in flight, if any. Called at the start of every delayed query so a
        /// superseded request stops before it consumes quota.
        /// </summary>
        public void CancelPending()
        {
            CancellationTokenSource previous;
            lock (_pendingLock)
            {
                previous = _pending;
                _pending = new CancellationTokenSource();
            }

            try
            {
                previous.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already torn down during shutdown.
            }

            // Intentionally not disposing `previous`: in-flight work still reads its token, and
            // reading a token whose source was disposed throws.
        }

        /// <summary>
        /// Synchronous entry point used by the plugin query. Runs on the thread pool so it can never
        /// deadlock against the WPF dispatcher.
        /// </summary>
        public TranslationResult Translate(TranslationRequest request)
        {
            return Task.Run(() => TranslateAsync(request)).GetAwaiter().GetResult();
        }

        private async Task<TranslationResult> TranslateAsync(TranslationRequest request)
        {
            CancellationToken pendingToken;
            lock (_pendingLock)
            {
                pendingToken = _pending.Token;
            }

            (string text, bool truncated) = TruncateToUtf8Bytes(request.Text, _config.MaxCharsPerRequest);
            var effective = new TranslationRequest(text, request.Source, request.Target);

            TranslationOutcome? lastFailure = null;
            string lastProviderName = string.Empty;
            bool sawUnconfigured = false;
            string unconfiguredReason = string.Empty;

            foreach (string providerId in _config.ProviderChain())
            {
                if (!_providers.TryGetValue(providerId, out ITranslationProvider? provider))
                {
                    continue;
                }

                if (!provider.IsConfigured)
                {
                    sawUnconfigured = true;
                    unconfiguredReason += (unconfiguredReason.Length > 0 ? "；" : string.Empty) + provider.DisplayName + "未配置密钥";
                    continue;
                }

                string cacheKey = effective.CacheKey(provider.Id);

                if (_cache.TryGetValue(cacheKey, out TranslationOutcome? cached))
                {
                    return new TranslationResult(cached, provider.DisplayName) { FromCache = true, Truncated = truncated };
                }

                // Coalesce identical requests that are already in flight. Note that the factory may
                // run more than once under contention but only one task wins TryAdd.
                Task<TranslationOutcome> task = _inflight.GetOrAdd(
                    cacheKey,
                    _ => RunOnceAsync(provider, effective, cacheKey, pendingToken));

                TranslationOutcome outcome = await task.ConfigureAwait(false);

                if (outcome.Cancelled)
                {
                    return new TranslationResult(outcome, provider.DisplayName) { Truncated = truncated };
                }

                if (outcome.Success)
                {
                    return new TranslationResult(outcome, provider.DisplayName) { Truncated = truncated };
                }

                lastFailure = outcome;
                lastProviderName = provider.DisplayName;

                // Only fall through to the next provider when this one could not be used at all
                // (for example Baidu is down and a fallback is configured).
                if (pendingToken.IsCancellationRequested)
                {
                    return new TranslationResult(TranslationOutcome.CancelledOutcome(), provider.DisplayName) { Truncated = truncated };
                }
            }

            if (lastFailure != null)
            {
                return new TranslationResult(lastFailure, lastProviderName) { Truncated = truncated };
            }

            if (sawUnconfigured)
            {
                return new TranslationResult(
                    TranslationOutcome.Fail("not-configured", unconfiguredReason, PluginStrings.NotConfiguredSubTitle),
                    PrimaryProviderName) { Truncated = truncated };
            }

            return new TranslationResult(
                TranslationOutcome.Fail("noprovider", "没有可用的翻译服务", "请检查 config.json 中的 provider 设置"),
                PrimaryProviderName) { Truncated = truncated };
        }

        private async Task<TranslationOutcome> RunOnceAsync(
            ITranslationProvider provider,
            TranslationRequest request,
            string cacheKey,
            CancellationToken pendingToken)
        {
            try
            {
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(pendingToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(_config.RequestTimeoutSeconds));

                    TranslationOutcome outcome;
                    try
                    {
                        await WaitForSlotAsync(timeoutCts.Token).ConfigureAwait(false);
                        outcome = await provider.TranslateAsync(request, timeoutCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return pendingToken.IsCancellationRequested
                            ? TranslationOutcome.CancelledOutcome()
                            : TimeoutOutcome();
                    }

                    if (outcome.Cancelled)
                    {
                        // The provider swallows the cancellation, so disambiguate here: a superseded
                        // query must not be reported to the user as a timeout.
                        if (pendingToken.IsCancellationRequested)
                        {
                            return TranslationOutcome.CancelledOutcome();
                        }

                        return timeoutCts.IsCancellationRequested ? TimeoutOutcome() : TranslationOutcome.CancelledOutcome();
                    }

                    if (outcome.Success)
                    {
                        Cache(cacheKey, outcome);
                        return outcome;
                    }

                    if (!outcome.Retryable || attempt == 2)
                    {
                        return outcome;
                    }

                    await Task.Delay(250, pendingToken).ConfigureAwait(false);
                }

                return TimeoutOutcome();
            }
            catch (OperationCanceledException)
            {
                return TranslationOutcome.CancelledOutcome();
            }
            finally
            {
                _inflight.TryRemove(cacheKey, out _);
            }
        }

        private TranslationOutcome TimeoutOutcome()
        {
            string seconds = _config.RequestTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return TranslationOutcome.Fail("timeout", "请求超时 " + seconds + " 秒", "请求超时，请检查网络后重试", retryable: true);
        }

        /// <summary>Serializes outbound requests and enforces the configured minimum interval.</summary>
        private async Task WaitForSlotAsync(CancellationToken token)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                int elapsed = (int)(DateTime.UtcNow - _lastRequestUtc).TotalMilliseconds;
                int wait = _config.MinIntervalMs - elapsed;
                if (wait > 0)
                {
                    await Task.Delay(wait, token).ConfigureAwait(false);
                }

                _lastRequestUtc = DateTime.UtcNow;
            }
            finally
            {
                _gate.Release();
            }
        }

        private void Cache(string key, TranslationOutcome outcome)
        {
            if (_cache.TryAdd(key, outcome))
            {
                _cacheOrder.Enqueue(key);
            }

            while (_cache.Count > MaxCacheEntries && _cacheOrder.TryDequeue(out string? oldest))
            {
                if (oldest != null)
                {
                    _cache.TryRemove(oldest, out _);
                }
            }
        }

        /// <summary>
        /// Cuts text to a UTF-8 byte budget without splitting a surrogate pair, because the Baidu
        /// endpoint limits the payload size.
        /// </summary>
        internal static (string Text, bool Truncated) TruncateToUtf8Bytes(string text, int maxBytes)
        {
            if (string.IsNullOrEmpty(text) || Encoding.UTF8.GetByteCount(text) <= maxBytes)
            {
                return (text, false);
            }

            var builder = new StringBuilder(maxBytes);
            int used = 0;
            int index = 0;

            while (index < text.Length)
            {
                int length = 1;
                if (char.IsHighSurrogate(text[index])
                    && index + 1 < text.Length
                    && char.IsLowSurrogate(text[index + 1]))
                {
                    length = 2;
                }

                string element = text.Substring(index, length);
                int elementBytes = Encoding.UTF8.GetByteCount(element);
                if (used + elementBytes > maxBytes)
                {
                    break;
                }

                builder.Append(element);
                used += elementBytes;
                index += length;
            }

            return (builder.ToString(), true);
        }

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
            };

            var client = new HttpClient(handler)
            {
                // Per-request timeouts are enforced with a CancellationTokenSource so that a slow
                // request cannot outlive the query that asked for it.
                Timeout = System.Threading.Timeout.InfiniteTimeSpan,
            };

            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) PowerToys-Translate/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
            return client;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            lock (_pendingLock)
            {
                try
                {
                    _pending.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Nothing to do.
                }
            }

            _gate.Dispose();
        }
    }
}
