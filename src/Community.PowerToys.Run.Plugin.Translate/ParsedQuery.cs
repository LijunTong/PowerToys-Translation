// Copyright (c) Joont. All rights reserved.
// Licensed under the MIT license.

namespace Community.PowerToys.Run.Plugin.Translate
{
    /// <summary>Outcome of parsing the text the user typed after the <c>tr</c> action keyword.</summary>
    internal sealed class ParsedQuery
    {
        private ParsedQuery()
        {
        }

        /// <summary>
        /// Gets a value indicating whether there is actual text to translate. When <c>false</c> the
        /// plugin shows <see cref="Notice"/> instead of calling a translation service.
        /// </summary>
        public bool HasText { get; private set; }

        /// <summary>Gets the canonical source language, or <see cref="LanguageCatalog.Auto"/>.</summary>
        public string Source { get; private set; } = LanguageCatalog.Auto;

        /// <summary>Gets the canonical target language (never <see cref="LanguageCatalog.Auto"/>).</summary>
        public string Target { get; private set; } = string.Empty;

        /// <summary>Gets the text to translate.</summary>
        public string Text { get; private set; } = string.Empty;

        /// <summary>Gets a value indicating whether the user pinned the source language.</summary>
        public bool SourceExplicit { get; private set; }

        /// <summary>Gets a value indicating whether the user pinned the target language.</summary>
        public bool TargetExplicit { get; private set; }

        /// <summary>Gets the message to display when <see cref="HasText"/> is <c>false</c>.</summary>
        public string? Notice { get; private set; }

        /// <summary>Gets the subtitle to display together with <see cref="Notice"/>.</summary>
        public string? NoticeDetail { get; private set; }

        public static ParsedQuery Usage(string title, string? detail)
        {
            return new ParsedQuery { HasText = false, Notice = title, NoticeDetail = detail };
        }

        public static ParsedQuery Translate(string text, string source, string target, bool sourceExplicit, bool targetExplicit)
        {
            return new ParsedQuery
            {
                HasText = true,
                Text = text,
                Source = source,
                Target = target,
                SourceExplicit = sourceExplicit,
                TargetExplicit = targetExplicit,
            };
        }
    }
}
