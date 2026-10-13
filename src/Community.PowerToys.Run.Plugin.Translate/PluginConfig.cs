// Copyright (c) Joont. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Community.PowerToys.Run.Plugin.Translate
{
    /// <summary>
    /// The plugin's own settings, loaded from <c>config.json</c> next to the plugin assembly.
    /// <para>
    /// A file is used rather than the PowerToys Settings UI because PowerToys 0.64 only supports
    /// boolean <c>ISettingProvider</c> options, so an API key simply cannot be entered there.
    /// </para>
    /// <para>
    /// Loading must never prevent the plugin from loading: every problem degrades to defaults plus a
    /// warning that is surfaced in the result subtitle.
    /// </para>
    /// </summary>
    internal sealed class PluginConfig
    {
        public const string ProviderBaidu = "baidu";
        public const string ProviderGoogle = "google";
        public const string ProviderNone = "none";

        private const string ConfigFileName = "config.json";

        [JsonPropertyName("provider")]
        public string? Provider { get; set; } = ProviderBaidu;

        /// <summary>Optional secondary provider used when the primary one fails. "none" disables it.</summary>
        [JsonPropertyName("fallbackProvider")]
        public string? FallbackProvider { get; set; } = ProviderNone;

        /// <summary>Canonical language code used when the direction cannot be detected automatically.</summary>
        [JsonPropertyName("defaultTarget")]
        public string? DefaultTarget { get; set; } = "zh";

        /// <summary>When true, CJK input is translated to English and everything else to the default target.</summary>
        [JsonPropertyName("autoDirection")]
        public bool AutoDirection { get; set; } = true;

        [JsonPropertyName("requestTimeoutSeconds")]
        public int RequestTimeoutSeconds { get; set; } = 8;

        /// <summary>Minimum delay between outbound requests. The Baidu free tier allows roughly 1 QPS.</summary>
        [JsonPropertyName("minIntervalMs")]
        public int MinIntervalMs { get; set; } = 1100;

        /// <summary>
        /// How long the typed text must stay unchanged before a request is sent. PowerToys Run
        /// queries the plugin on every keystroke, so without this a fast typist starts one HTTP
        /// request per key. 0 disables debouncing.
        /// </summary>
        [JsonPropertyName("debounceMs")]
        public int DebounceMs { get; set; } = 400;

        /// <summary>Maximum request payload size in UTF-8 bytes.</summary>
        [JsonPropertyName("maxCharsPerRequest")]
        public int MaxCharsPerRequest { get; set; } = 1900;

        /// <summary>
        /// Backing field for <see cref="Baidu"/>. Typed nullable because JSON may explicitly contain
        /// <c>"baidu": null</c>, which would otherwise leave the plugin with a null credentials object.
        /// </summary>
        [JsonPropertyName("baidu")]
        public BaiduCredentials? BaiduConfig { get; set; } = new BaiduCredentials();

        /// <summary>Gets the Baidu credentials, creating the object on first use.</summary>
        [JsonIgnore]
        public BaiduCredentials Baidu => BaiduConfig ??= new BaiduCredentials();

        /// <summary>Gets a human readable description of a load problem, or <c>null</c> when clean.</summary>
        [JsonIgnore]
        public string? LoadWarning { get; private set; }

        /// <summary>Gets the full path of the configuration file.</summary>
        public static string ConfigPath => Path.Combine(PluginDirectory, ConfigFileName);

        /// <summary>Gets the directory the plugin assembly was loaded from.</summary>
        public static string PluginDirectory =>
            Path.GetDirectoryName(typeof(PluginConfig).Assembly.Location) ?? AppContext.BaseDirectory;

        public static PluginConfig Load()
        {
            string path = ConfigPath;

            if (!File.Exists(path))
            {
                var fresh = new PluginConfig();
                fresh.TryWriteTemplate(path);
                return fresh;
            }

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                };

                PluginConfig? loaded = JsonSerializer.Deserialize<PluginConfig>(json, options);
                if (loaded == null)
                {
                    return new PluginConfig { LoadWarning = "配置内容为空" };
                }

                loaded.Normalize();
                return loaded;
            }
            catch (JsonException e)
            {
                return new PluginConfig { LoadWarning = "JSON 解析失败：" + FirstLine(e.Message) };
            }
            catch (Exception e)
            {
                return new PluginConfig { LoadWarning = "读取失败：" + FirstLine(e.Message) };
            }
        }

        /// <summary>Gets a value indicating whether the selected provider has what it needs to run.</summary>
        public bool IsConfigured(out string reason)
        {
            switch (Provider)
            {
                case ProviderGoogle:
                    reason = string.Empty;
                    return true;

                case ProviderBaidu:
                    if (string.IsNullOrWhiteSpace(Baidu.AppId))
                    {
                        reason = "缺少 appid";
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(Baidu.SecretKey))
                    {
                        reason = "缺少 secretKey";
                        return false;
                    }

                    reason = string.Empty;
                    return true;

                default:
                    reason = "未知的 provider：" + Provider;
                    return false;
            }
        }

        /// <summary>Gets the provider chain, primary first, including an optional distinct fallback.</summary>
        public IReadOnlyList<string> ProviderChain()
        {
            var chain = new List<string> { Provider ?? ProviderBaidu };

            if (!string.IsNullOrEmpty(FallbackProvider)
                && !FallbackProvider.Equals(ProviderNone, StringComparison.Ordinal)
                && !FallbackProvider.Equals(chain[0], StringComparison.Ordinal))
            {
                chain.Add(FallbackProvider);
            }

            return chain;
        }

        /// <summary>Clamps and repairs values so downstream code can trust them.</summary>
        private void Normalize()
        {
            Provider = NormalizeProvider(Provider, ProviderBaidu);
            FallbackProvider = NormalizeProvider(FallbackProvider, ProviderNone);

            if (LanguageCatalog.ToBaidu(DefaultTarget) == null)
            {
                LoadWarning = AppendWarning(LoadWarning, "defaultTarget \"" + DefaultTarget + "\" 无法识别，已改为 zh");
                DefaultTarget = "zh";
            }

            RequestTimeoutSeconds = Clamp(RequestTimeoutSeconds, 2, 60);
            MinIntervalMs = Clamp(MinIntervalMs, 0, 10000);
            DebounceMs = Clamp(DebounceMs, 0, 3000);
            MaxCharsPerRequest = Clamp(MaxCharsPerRequest, 200, 5000);

            Baidu.AppId = (Baidu.AppId ?? string.Empty).Trim();
            Baidu.SecretKey = (Baidu.SecretKey ?? string.Empty).Trim();
            DefaultTarget = DefaultTarget!.Trim();
        }

        private static string NormalizeProvider(string? value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            string normalized = value.Trim().ToLowerInvariant();
            return normalized is ProviderBaidu or ProviderGoogle or ProviderNone ? normalized : fallback;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        private static string? AppendWarning(string? existing, string message)
        {
            return existing == null ? message : existing + "；" + message;
        }

        private static string FirstLine(string value)
        {
            int index = value.IndexOfAny(new[] { '\r', '\n' });
            string line = index < 0 ? value : value.Substring(0, index);
            return line.Length > 160 ? line.Substring(0, 160) + "…" : line;
        }

        /// <summary>
        /// Writes a default file so the user has something to edit. Failures are swallowed: a
        /// read-only install directory must not break the plugin.
        /// </summary>
        private void TryWriteTemplate(string path)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                };

                File.WriteAllText(path, JsonSerializer.Serialize(this, options) + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // Intentionally ignored; the plugin works on defaults without a file.
            }
        }

        internal sealed class BaiduCredentials
        {
            [JsonPropertyName("appid")]
            public string? AppId { get; set; } = string.Empty;

            [JsonPropertyName("secretKey")]
            public string? SecretKey { get; set; } = string.Empty;
        }
    }
}
