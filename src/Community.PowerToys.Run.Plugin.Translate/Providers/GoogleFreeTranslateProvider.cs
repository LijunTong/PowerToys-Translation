// Copyright (c) Joont. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Community.PowerToys.Run.Plugin.Translate.Providers
{
    /// <summary>
    /// The key-less Google web endpoint.
    /// <para>
    /// This exists as an alternative and as a way to verify the whole plugin pipeline without Baidu
    /// credentials. It is an undocumented endpoint, so it can change or throttle without notice;
    /// Baidu remains the default provider.
    /// </para>
    /// <para>
    /// The response is a nested array with no field names: element 0 is the list of translated
    /// segments (each segment's element 0 is a chunk of the translation) and element 2 is the
    /// detected source language. Segments must be concatenated, because a multi-sentence input is
    /// split across several of them.
    /// </para>
    /// </summary>
    internal sealed class GoogleFreeTranslateProvider : ITranslationProvider
    {
        private const string Endpoint = "https://translate.googleapis.com/translate_a/single";

        private readonly HttpClient _http;

        public GoogleFreeTranslateProvider(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        public string Id => PluginConfig.ProviderGoogle;

        public string DisplayName => "Google 翻译(免密钥)";

        public bool IsConfigured => true;

        public async Task<TranslationOutcome> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
        {
            string? target = LanguageCatalog.ToGoogle(request.Target);
            if (target == null)
            {
                return TranslationOutcome.Fail(
                    "unsupported-target",
                    "Google 免密钥端点不支持目标语言 " + request.Target,
                    "该语言请改用百度翻译作为 provider");
            }

            string source = LanguageCatalog.ToGoogle(request.Source) ?? "auto";

            var form = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["client"] = "gtx",
                ["sl"] = source,
                ["tl"] = target,
                ["dt"] = "t",
                ["q"] = request.Text,
            };

            try
            {
                using var content = new FormUrlEncodedContent(form);
                using HttpResponseMessage response = await _http
                    .PostAsync(Endpoint, content, cancellationToken)
                    .ConfigureAwait(false);

                string body = await response.Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    int status = (int)response.StatusCode;
                    return TranslationOutcome.Fail(
                        "http" + status.ToString(CultureInfo.InvariantCulture),
                        "HTTP " + status.ToString(CultureInfo.InvariantCulture),
                        status == 429
                            ? "Google 端点限流，请稍后重试或改用百度翻译"
                            : "Google 端点返回 HTTP " + status.ToString(CultureInfo.InvariantCulture),
                        retryable: status >= 500);
                }

                return Parse(body);
            }
            catch (OperationCanceledException)
            {
                return TranslationOutcome.CancelledOutcome();
            }
            catch (HttpRequestException e)
            {
                return TranslationOutcome.Fail("network", e.Message, "网络不可达或连接被拦截，请检查网络与代理设置", retryable: true);
            }
            catch (Exception e)
            {
                return TranslationOutcome.Fail("unexpected", e.GetType().Name + ": " + e.Message, "未预期错误，详见 PowerToys Run 日志");
            }
        }

        private static TranslationOutcome Parse(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return TranslationOutcome.Fail("emptyresponse", "响应为空", "Google 端点返回了空响应，请重试", retryable: true);
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(body);
            }
            catch (JsonException e)
            {
                return TranslationOutcome.Fail("badjson", e.Message, "Google 端点返回了非 JSON 内容，可能被拦截或已变更");
            }

            using (doc)
            {
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                {
                    return TranslationOutcome.Fail("badresponse", "响应不是预期的数组结构", "Google 端点响应格式已变更，请改用百度翻译");
                }

                var builder = new StringBuilder();
                JsonElement segments = root[0];
                if (segments.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement segment in segments.EnumerateArray())
                    {
                        if (segment.ValueKind != JsonValueKind.Array || segment.GetArrayLength() == 0)
                        {
                            continue;
                        }

                        JsonElement chunk = segment[0];
                        if (chunk.ValueKind == JsonValueKind.String)
                        {
                            builder.Append(chunk.GetString());
                        }
                    }
                }

                if (builder.Length == 0)
                {
                    return TranslationOutcome.Fail("notranslation", "响应中没有译文", "Google 端点返回了空结果，请重试", retryable: true);
                }

                string detected = root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String
                    ? LanguageCatalog.FromProvider(root[2].GetString(), isBaidu: false)
                    : string.Empty;

                return TranslationOutcome.Ok(builder.ToString(), detected);
            }
        }
    }
}
