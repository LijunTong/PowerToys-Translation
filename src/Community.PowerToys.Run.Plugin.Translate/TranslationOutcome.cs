// Copyright (c) Joont. All rights reserved.
// Licensed under the MIT license.

namespace Community.PowerToys.Run.Plugin.Translate
{
    /// <summary>
    /// Result of a translation attempt. Providers report failures as data rather than exceptions so
    /// the plugin can always show the user something actionable.
    /// </summary>
    internal sealed class TranslationOutcome
    {
        private TranslationOutcome()
        {
        }

        public bool Success { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the attempt was abandoned because the query was superseded
        /// or timed out. The caller decides which of the two, because only it knows whether the
        /// request token or the timeout token was signalled.
        /// </summary>
        public bool Cancelled { get; private set; }

        /// <summary>Gets a value indicating whether repeating the very same request could succeed.</summary>
        public bool Retryable { get; private set; }

        public string TranslatedText { get; private set; } = string.Empty;

        /// <summary>Gets the detected source language reported by the service, as a canonical code.</summary>
        public string DetectedSource { get; private set; } = string.Empty;

        public string ErrorCode { get; private set; } = string.Empty;

        public string ErrorMessage { get; private set; } = string.Empty;

        /// <summary>Gets an actionable, Chinese suggestion shown as the result subtitle.</summary>
        public string UserHint { get; private set; } = string.Empty;

        public static TranslationOutcome Ok(string translatedText, string detectedSource)
        {
            return new TranslationOutcome
            {
                Success = true,
                TranslatedText = translatedText ?? string.Empty,
                DetectedSource = detectedSource ?? string.Empty,
            };
        }

        public static TranslationOutcome Fail(string errorCode, string errorMessage, string userHint, bool retryable = false)
        {
            return new TranslationOutcome
            {
                Retryable = retryable,
                ErrorCode = errorCode ?? string.Empty,
                ErrorMessage = errorMessage ?? string.Empty,
                UserHint = userHint ?? string.Empty,
            };
        }

        public static TranslationOutcome CancelledOutcome()
        {
            return new TranslationOutcome { Cancelled = true };
        }
    }
}
