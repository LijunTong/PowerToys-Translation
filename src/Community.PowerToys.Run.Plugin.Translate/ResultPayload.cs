// Copyright (c) Joont. All rights reserved.
// Licensed under the MIT license.

namespace Community.PowerToys.Run.Plugin.Translate
{
    /// <summary>
    /// Carried on <c>Result.ContextData</c> so the context menu can rebuild the actions without
    /// re-parsing the query or re-requesting a translation.
    /// </summary>
    internal sealed class ResultPayload
    {
        /// <summary>Gets or sets the text the user typed after the action keyword.</summary>
        public string Search { get; set; } = string.Empty;

        /// <summary>Gets or sets the untranslated text.</summary>
        public string SourceText { get; set; } = string.Empty;

        /// <summary>Gets or sets the translated text, when available.</summary>
        public string TranslatedText { get; set; } = string.Empty;

        /// <summary>Gets or sets the canonical source language, or <c>auto</c>.</summary>
        public string Source { get; set; } = LanguageCatalog.Auto;

        /// <summary>Gets or sets the canonical target language.</summary>
        public string Target { get; set; } = string.Empty;

        /// <summary>Gets or sets the source language the service actually detected.</summary>
        public string DetectedSource { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether a translation is available to copy.</summary>
        public bool HasTranslation { get; set; }

        /// <summary>Gets or sets a value indicating whether the failure was missing credentials.</summary>
        public bool NotConfigured { get; set; }
    }
}
