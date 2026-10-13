// Copyright (c) Joont. All rights reserved.
// Licensed under the MIT license.

namespace Community.PowerToys.Run.Plugin.Translate
{
    /// <summary>
    /// What the result builder needs to know: the outcome itself plus how it was produced.
    /// </summary>
    internal sealed class TranslationResult
    {
        public TranslationResult(TranslationOutcome outcome, string providerName)
        {
            Outcome = outcome;
            ProviderName = providerName;
        }

        public TranslationOutcome Outcome { get; }

        /// <summary>Gets the display name of the provider that produced the outcome.</summary>
        public string ProviderName { get; }

        /// <summary>Gets a value indicating whether the answer came from the in-memory cache.</summary>
        public bool FromCache { get; set; }

        /// <summary>Gets a value indicating whether the request text was cut down to the size limit.</summary>
        public bool Truncated { get; set; }
    }
}
