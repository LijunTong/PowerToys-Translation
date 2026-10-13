// Copyright (c) Joont. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Community.PowerToys.Run.Plugin.Translate
{
    /// <summary>
    /// Language code plumbing between user input, Baidu and Google.
    /// <para>
    /// Codes are <em>canonical</em> (the plugin's own neutral identifiers) and each provider maps a
    /// canonical code to its own spelling. This indirection is required because Baidu and Google
    /// disagree on several languages (Baidu <c>jp</c>/<c>kor</c>/<c>fra</c>/<c>spa</c>/<c>cht</c>
    /// versus Google <c>ja</c>/<c>ko</c>/<c>fr</c>/<c>es</c>/<c>zh-TW</c>).
    /// </para>
    /// </summary>
    internal static class LanguageCatalog
    {
        internal sealed class Language
        {
            public Language(string canonical, string? baidu, string? google, string display, params string[] aliases)
            {
                Canonical = canonical;
                Baidu = baidu;
                Google = google;
                Display = display;
                Aliases = aliases;
            }

            /// <summary>Gets the canonical code used inside the plugin.</summary>
            public string Canonical { get; }

            /// <summary>Gets the Baidu language code, or <c>null</c> when Baidu cannot express it.</summary>
            public string? Baidu { get; }

            /// <summary>Gets the Google language code, or <c>null</c> when Google cannot express it.</summary>
            public string? Google { get; }

            /// <summary>Gets the Chinese display name shown in result subtitles.</summary>
            public string Display { get; }

            /// <summary>Gets the extra lowercase tokens accepted for this language.</summary>
            public string[] Aliases { get; }
        }

        public const string Auto = "auto";

        // Keep this table in sync with the Baidu and Google language lists. A null provider code
        // means "this provider cannot handle it"; the provider then returns a friendly error
        // instead of sending a request that is guaranteed to fail.
        private static readonly Language[] Table =
        {
            new Language("zh",    "zh",  "zh-CN", "中文(简体)", "zh-cn", "zh-hans", "cn", "chs", "chinese", "simplified"),
            new Language("zh-tw", "cht", "zh-TW", "中文(繁体)", "zh-hk", "zh-hant", "cht", "tw", "traditional"),
            new Language("en",    "en",  "en",    "英语",       "eng", "english"),
            new Language("ja",    "jp",  "ja",    "日语",       "jp", "jpn", "japanese"),
            new Language("ko",    "kor", "ko",    "韩语",       "kor", "kr", "korean"),
            new Language("fr",    "fra", "fr",    "法语",       "fra", "fre", "french"),
            new Language("es",    "spa", "es",    "西班牙语",   "spa", "spanish"),
            new Language("de",    "de",  "de",    "德语",       "ger", "deu", "german"),
            new Language("ru",    "ru",  "ru",    "俄语",       "rus", "russian"),
            new Language("pt",    "pt",  "pt",    "葡萄牙语",   "por", "portuguese"),
            new Language("it",    "it",  "it",    "意大利语",   "ita", "italian"),
            new Language("th",    "th",  "th",    "泰语",       "tha", "thai"),
            new Language("ar",    "ara", "ar",    "阿拉伯语",   "ara", "arabic"),
            new Language("vi",    "vie", "vi",    "越南语",     "vie", "vietnamese"),
            new Language("nl",    "nl",  "nl",    "荷兰语",     "nld", "dut", "dutch"),
            new Language("pl",    "pl",  "pl",    "波兰语",     "pol", "polish"),
            new Language("el",    "el",  "el",    "希腊语",     "ell", "gre", "greek"),
            new Language("hu",    "hu",  "hu",    "匈牙利语",   "hun", "hungarian"),
            new Language("sv",    "swe", "sv",    "瑞典语",     "swe", "swedish"),
            new Language("da",    "dan", "da",    "丹麦语",     "dan", "danish"),
            new Language("fi",    "fin", "fi",    "芬兰语",     "fin", "finnish"),
            new Language("cs",    "cs",  "cs",    "捷克语",     "ces", "cze", "czech"),
            new Language("ro",    "rom", "ro",    "罗马尼亚语", "rom", "ron", "romanian"),
            new Language("sk",    "slo", "sk",    "斯洛伐克语", "slo", "slk", "slovak"),
            new Language("et",    "est", "et",    "爱沙尼亚语", "est", "estonian"),
            new Language("bg",    "bul", "bg",    "保加利亚语", "bul", "bulgarian"),
            new Language("yue",   "yue", null,    "粤语",       "cantonese"),
            new Language("wyw",   "wyw", null,    "文言文",     "classical"),
        };

        private static readonly Dictionary<string, Language> ByToken = BuildIndex();

        private static Dictionary<string, Language> BuildIndex()
        {
            var map = new Dictionary<string, Language>(StringComparer.OrdinalIgnoreCase);
            foreach (Language language in Table)
            {
                map[language.Canonical] = language;
                foreach (string alias in language.Aliases)
                {
                    map[alias] = language;
                }
            }

            return map;
        }

        /// <summary>
        /// Resolves a user typed language token (for example <c>ja</c>, <c>jp</c> or <c>zh-TW</c>)
        /// to a canonical language.
        /// </summary>
        public static bool TryResolve(string? token, out Language language)
        {
            language = null!;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string key = token.Trim();
            if (key.Equals(Auto, StringComparison.OrdinalIgnoreCase))
            {
                // "auto" is not a language; it is handled by the caller as "let the service detect".
                return false;
            }

            return ByToken.TryGetValue(key, out language!);
        }

        /// <summary>Gets the Chinese display name for a canonical code, falling back to the raw code.</summary>
        public static string Display(string? canonical)
        {
            if (string.IsNullOrEmpty(canonical))
            {
                return "未知";
            }

            if (canonical.Equals(Auto, StringComparison.OrdinalIgnoreCase))
            {
                return "自动检测";
            }

            return ByToken.TryGetValue(canonical, out Language? language) ? language.Display : canonical;
        }

        /// <summary>Maps a canonical code to the Baidu code, or <c>null</c> when unsupported.</summary>
        public static string? ToBaidu(string? canonical)
        {
            if (string.IsNullOrEmpty(canonical))
            {
                return null;
            }

            if (canonical.Equals(Auto, StringComparison.OrdinalIgnoreCase))
            {
                return "auto";
            }

            return ByToken.TryGetValue(canonical, out Language? language) ? language.Baidu : null;
        }

        /// <summary>Maps a canonical code to the Google code, or <c>null</c> when unsupported.</summary>
        public static string? ToGoogle(string? canonical)
        {
            if (string.IsNullOrEmpty(canonical))
            {
                return null;
            }

            if (canonical.Equals(Auto, StringComparison.OrdinalIgnoreCase))
            {
                return "auto";
            }

            return ByToken.TryGetValue(canonical, out Language? language) ? language.Google : null;
        }

        /// <summary>
        /// Maps a provider reported source code back to a canonical code so the result subtitle can
        /// be shown in a consistent form (Baidu answers <c>jp</c>, Google answers <c>ja</c>).
        /// </summary>
        public static string FromProvider(string? providerCode, bool isBaidu)
        {
            if (string.IsNullOrWhiteSpace(providerCode))
            {
                return string.Empty;
            }

            string code = providerCode.Trim();
            foreach (Language language in Table)
            {
                string? candidate = isBaidu ? language.Baidu : language.Google;
                if (candidate != null && candidate.Equals(code, StringComparison.OrdinalIgnoreCase))
                {
                    return language.Canonical;
                }
            }

            return code.ToLower(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// True when the text is mostly Chinese Han characters.
        /// <para>
        /// Kana or Hangul makes the text Japanese or Korean rather than Chinese, and that
        /// distinction matters: the automatic direction rule exists to avoid a pointless
        /// Chinese-to-Chinese translation, so it must not fire for Japanese text just because
        /// Japanese also uses Han characters.
        /// </para>
        /// </summary>
        public static bool IsChineseHeavy(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            int total = 0;
            int han = 0;
            bool otherCjkScript = false;

            foreach (char ch in text)
            {
                if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch) || char.IsDigit(ch))
                {
                    continue;
                }

                total++;

                if (IsHan(ch))
                {
                    han++;
                }
                else if (IsKanaOrHangul(ch))
                {
                    otherCjkScript = true;
                }
            }

            return !otherCjkScript && total > 0 && (han * 100) >= (total * 30);
        }

        /// <summary>True when the language is a Chinese variant, including Cantonese and Classical Chinese.</summary>
        public static bool IsChineseVariant(string? canonical)
        {
            if (string.IsNullOrEmpty(canonical))
            {
                return false;
            }

            return canonical.Equals("zh", StringComparison.OrdinalIgnoreCase)
                || canonical.Equals("zh-tw", StringComparison.OrdinalIgnoreCase)
                || canonical.Equals("yue", StringComparison.OrdinalIgnoreCase)
                || canonical.Equals("wyw", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHan(char ch)
        {
            return (ch >= '\u3400' && ch <= '\u4DBF')
                || (ch >= '\u4E00' && ch <= '\u9FFF')
                || (ch >= '\uF900' && ch <= '\uFAFF');
        }

        private static bool IsKanaOrHangul(char ch)
        {
            return (ch >= '\u3040' && ch <= '\u30FF')
                || (ch >= '\uFF66' && ch <= '\uFF9D')
                || (ch >= '\u1100' && ch <= '\u11FF')
                || (ch >= '\uAC00' && ch <= '\uD7AF');
        }

        /// <summary>Builds the language list shown by the <c>tr</c> usage hint.</summary>
        public static string BuildHelpList()
        {
            var builder = new StringBuilder();
            foreach (Language language in Table)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(language.Canonical);
            }

            return builder.ToString();
        }
    }
}
