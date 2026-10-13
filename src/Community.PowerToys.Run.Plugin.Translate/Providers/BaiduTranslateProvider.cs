// Copyright (c) Joont. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Community.PowerToys.Run.Plugin.Translate.Providers
{
    /// <summary>
    /// Baidu general text translation.
    /// <para>
    /// Request signing is <c>MD5(appid + q + salt + secretKey)</c> in lower case hex, computed over
    /// the raw (unencoded) values, with <c>q</c> being the text exactly as it is sent.
    /// </para>
    /// </summary>
    internal sealed class BaiduTranslateProvider : ITranslationProvider
    {
        private const string Endpoint = "https://fanyi-api.baidu.com/api/trans/vip/translate";

        private readonly HttpClient _http;
        private readonly string _appId;
        private readonly string _secretKey;

        public BaiduTranslateProvider(HttpClient http, string? appId, string? secretKey)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _appId = (appId ?? string.Empty).Trim();
            _secretKey = (secretKey ?? string.Empty).Trim();
        }

        public string Id => PluginConfig.ProviderBaidu;

        public string DisplayName => "百度翻译";

        public bool IsConfigured => _appId.Length > 0 && _secretKey.Length > 0;

        public async Task<TranslationOutcome> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
        {
            if (!IsConfigured)
            {
                return TranslationOutcome.Fail(
                    "nokey",
                    "缺少 appid 或 secretKey",
                    "运行仓库根目录的 install.ps1 -Reconfigure（选择 baidu 并填入 appid 与 secretKey），或直接编辑插件目录下的 config.json");
            }

            string? target = LanguageCatalog.ToBaidu(request.Target);
            if (target == null)
            {
                return TranslationOutcome.Fail(
                    "unsupported-target",
                    "百度翻译不支持目标语言 " + request.Target,
                    "请换用其它目标语言代码，例如 en / zh / jp / kor / fra");
            }

            string source = LanguageCatalog.ToBaidu(request.Source) ?? "auto";
            string salt = Random.Shared.Next(100000, int.MaxValue - 1).ToString(CultureInfo.InvariantCulture);
            string sign = ComputeSign(_appId, request.Text, salt, _secretKey);

            var form = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["q"] = request.Text,
                ["from"] = source,
                ["to"] = target,
                ["appid"] = _appId,
                ["salt"] = salt,
                ["sign"] = sign,
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
                        "百度翻译接口返回 HTTP " + status.ToString(CultureInfo.InvariantCulture) + "，请稍后重试",
                        retryable: status >= 500);
                }

                return Parse(body);
            }
            catch (OperationCanceledException)
            {
                // Either superseded by a newer keystroke or a timeout; the service disambiguates.
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

        /// <summary>Computes the Baidu request signature: lower case MD5 hex of appid + q + salt + key.</summary>
        internal static string ComputeSign(string appId, string query, string salt, string secretKey)
        {
            string raw = appId + query + salt + secretKey;
            byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static TranslationOutcome Parse(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return TranslationOutcome.Fail("emptyresponse", "响应为空", "服务返回了空响应，请重试", retryable: true);
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(body);
            }
            catch (JsonException e)
            {
                return TranslationOutcome.Fail(
                    "badjson",
                    e.Message,
                    "服务返回了非 JSON 内容，可能被网络劫持或代理拦截");
            }

            using (doc)
            {
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return TranslationOutcome.Fail("badresponse", "响应不是 JSON 对象", "服务返回了无法识别的响应");
                }

                if (root.TryGetProperty("error_code", out JsonElement codeElement))
                {
                    string code = ReadScalar(codeElement);
                    string message = root.TryGetProperty("error_msg", out JsonElement messageElement)
                        ? ReadScalar(messageElement)
                        : string.Empty;

                    return TranslationOutcome.Fail(code, message, HintFor(code, message), IsRetryableCode(code));
                }

                if (root.TryGetProperty("trans_result", out JsonElement resultElement)
                    && resultElement.ValueKind == JsonValueKind.Array)
                {
                    var builder = new StringBuilder();
                    foreach (JsonElement item in resultElement.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object
                            && item.TryGetProperty("dst", out JsonElement dstElement))
                        {
                            if (builder.Length > 0)
                            {
                                builder.Append('\n');
                            }

                            builder.Append(ReadScalar(dstElement));
                        }
                    }

                    if (builder.Length == 0)
                    {
                        return TranslationOutcome.Fail("notranslation", "响应中没有译文", "服务返回了空结果，请重试", retryable: true);
                    }

                    string detected = root.TryGetProperty("from", out JsonElement fromElement)
                        ? LanguageCatalog.FromProvider(ReadScalar(fromElement), isBaidu: true)
                        : string.Empty;

                    return TranslationOutcome.Ok(builder.ToString(), detected);
                }

                return TranslationOutcome.Fail("badresponse", "响应中既没有 error_code 也没有 trans_result", "服务返回了无法识别的响应");
            }
        }

        /// <summary>Baidu returns <c>error_code</c> as a string, but tolerate numbers defensively.</summary>
        private static string ReadScalar(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.Null => string.Empty,
                _ => element.GetRawText(),
            };
        }

        private static string HintFor(string code, string message)
        {
            switch (code)
            {
                case "52001":
                    return "请求超时，请重试";
                case "52002":
                    return "百度服务端系统错误，请稍后重试";
                case "52003":
                    return "appid 未授权或不存在，请核对 appid";
                case "54000":
                    return "必填参数为空；也可能是源语言与目标语言相同";
                case "54001":
                    return "签名错误，请核对 secretKey 是否与 appid 匹配";
                case "54003":
                    return "访问频率受限（免费版约 1 QPS），请稍后重试";
                case "54004":
                    return "账户余额或免费额度不足，请前往百度翻译开放平台查看";
                case "54005":
                    return "长文本请求过于频繁，请稍后重试";
                case "58000":
                    return "客户端 IP 非法，请在百度控制台检查 IP 白名单设置";
                case "58001":
                    return "该语言方向不被支持，请更换源或目标语言";
                case "58002":
                    return "百度翻译服务当前已关闭，请在控制台确认服务状态";
                case "90107":
                    return "认证未通过或未生效，请检查百度账号的认证状态";
                default:
                    return string.IsNullOrEmpty(message) ? "百度翻译返回了未知错误" : message;
            }
        }

        private static bool IsRetryableCode(string code)
        {
            return code is "52001" or "52002";
        }
    }
}
