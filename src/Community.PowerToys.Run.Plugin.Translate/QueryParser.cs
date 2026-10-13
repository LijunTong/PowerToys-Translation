// Copyright (c) Joont. All rights reserved.
// Licensed under the MIT license.

using System;

namespace Community.PowerToys.Run.Plugin.Translate
{
    /// <summary>
    /// Parses what the user typed after the <c>tr</c> action keyword.
    /// <para>
    /// Grammar (whitespace around the colon is tolerated):
    /// <list type="bullet">
    /// <item><c>&lt;text&gt;</c> — detect the source, pick the target automatically.</item>
    /// <item><c>&lt;src&gt;:&lt;dst&gt; &lt;text&gt;</c> — pin both, for example <c>en:ja good morning</c>.</item>
    /// <item><c>:&lt;dst&gt; &lt;text&gt;</c> — detect the source, pin the target.</item>
    /// <item><c>&lt;src&gt;: &lt;text&gt;</c> — pin the source, pick the target automatically.</item>
    /// </list>
    /// </para>
    /// <para>
    /// A language prefix is only honoured when the token actually resolves to a known language, so
    /// inputs such as <c>3:00 pm</c> or <c>http://example.com</c> are translated as literal text
    /// instead of being misread as a language pair.
    /// </para>
    /// </summary>
    internal static class QueryParser
    {
        public static ParsedQuery Parse(string? search, string defaultTarget, bool autoDirection)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return ParsedQuery.Usage(PluginStrings.UsageTitle, PluginStrings.UsageSubTitle);
            }

            // Chinese IMEs produce the full-width colon; accept it as the language separator.
            string input = search.Trim().Replace('\uFF1A', ':');

            string source = LanguageCatalog.Auto;
            string target = string.Empty;
            bool sourceExplicit = false;
            bool targetExplicit = false;
            string text = input;

            int colon = input.IndexOf(':');
            if (colon >= 0)
            {
                string left = input.Substring(0, colon).Trim();
                string rest = input.Substring(colon + 1).Trim();

                // The part before the colon must be empty or a real language token, otherwise the
                // colon belongs to the text itself (for example "3:00 pm").
                bool leftIsLanguage = TryResolveSourceToken(left, out string leftCode);
                bool prefixUsable = left.Length == 0 || leftIsLanguage;

                if (prefixUsable)
                {
                    string rightToken = FirstToken(rest);

                    // A target must be a concrete language; "auto" is not a valid target.
                    bool rightIsLanguage = LanguageCatalog.TryResolve(rightToken, out LanguageCatalog.Language rightLanguage);

                    // Insist that the prefix conveys something; a bare ":" is just text.
                    if (left.Length > 0 || rightIsLanguage)
                    {
                        if (left.Length > 0)
                        {
                            source = leftCode;
                            sourceExplicit = true;
                        }

                        if (rightIsLanguage)
                        {
                            target = rightLanguage.Canonical;
                            targetExplicit = true;
                            text = rest.Substring(rightToken.Length).Trim();
                        }
                        else
                        {
                            text = rest;
                        }
                    }
                }
            }

            if (text.Length == 0)
            {
                return ParsedQuery.Usage(PluginStrings.MissingTextTitle, PluginStrings.UsageSubTitle);
            }

            if (!targetExplicit)
            {
                target = defaultTarget;

                // The point of the automatic direction is to avoid a pointless same-language
                // translation: with a Chinese default target, Chinese input should become English
                // instead. Two guards keep that from surprising the user:
                //   * a pinned source ("tr ja: こんにちは") means the language is already known, so
                //     the default target is honoured and the result is Chinese rather than English;
                //   * Japanese and Korean text uses Han characters too, so only genuinely Chinese
                //     text triggers the switch.
                if (autoDirection
                    && !sourceExplicit
                    && LanguageCatalog.IsChineseVariant(defaultTarget)
                    && LanguageCatalog.IsChineseHeavy(text))
                {
                    target = "en";
                }
            }

            if (sourceExplicit
                && !source.Equals(LanguageCatalog.Auto, StringComparison.OrdinalIgnoreCase)
                && source.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                return ParsedQuery.Usage(
                    PluginStrings.SameLanguageTitle,
                    "源语言与目标语言都是 " + LanguageCatalog.Display(target) + "，请指定不同的目标语言。");
            }

            return ParsedQuery.Translate(text, source, target, sourceExplicit, targetExplicit);
        }

        /// <summary>
        /// Resolves the left hand token, which additionally accepts <c>auto</c> because "let the
        /// service detect the source" is a meaningful thing to write explicitly.
        /// </summary>
        private static bool TryResolveSourceToken(string token, out string canonical)
        {
            canonical = LanguageCatalog.Auto;

            if (token.Length == 0)
            {
                return false;
            }

            if (token.Equals(LanguageCatalog.Auto, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (LanguageCatalog.TryResolve(token, out LanguageCatalog.Language language))
            {
                canonical = language.Canonical;
                return true;
            }

            return false;
        }

        private static string FirstToken(string value)
        {
            if (value.Length == 0)
            {
                return string.Empty;
            }

            int end = 0;
            while (end < value.Length && !char.IsWhiteSpace(value[end]))
            {
                end++;
            }

            return value.Substring(0, end);
        }
    }
}
