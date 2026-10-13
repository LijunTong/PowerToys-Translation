// Copyright (c) Joont. All rights reserved.
// Licensed under the MIT license.

using System;

namespace Community.PowerToys.Run.Plugin.Translate
{
    /// <summary>A provider agnostic translation request. Language codes are canonical.</summary>
    internal sealed class TranslationRequest
    {
        public TranslationRequest(string text, string source, string target)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Source = string.IsNullOrWhiteSpace(source) ? LanguageCatalog.Auto : source;
            Target = target ?? throw new ArgumentNullException(nameof(target));
        }

        /// <summary>Gets the text to translate.</summary>
        public string Text { get; }

        /// <summary>Gets the canonical source language, or <see cref="LanguageCatalog.Auto"/>.</summary>
        public string Source { get; }

        /// <summary>Gets the canonical target language.</summary>
        public string Target { get; }

        /// <summary>
        /// Cache key. Includes the provider because different providers can return different text
        /// for the same input.
        /// </summary>
        public string CacheKey(string providerId)
        {
            return providerId + "|" + Source + "|" + Target + "|" + Text;
        }
    }
}
