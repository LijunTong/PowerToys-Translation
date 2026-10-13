// Copyright (c) Joont. All rights reserved.
// Licensed under the MIT license.

using System.Threading;
using System.Threading.Tasks;

namespace Community.PowerToys.Run.Plugin.Translate.Providers
{
    /// <summary>A translation backend. Implementations must report failures as data, not exceptions.</summary>
    internal interface ITranslationProvider
    {
        /// <summary>Gets the configuration identifier, for example <c>baidu</c>.</summary>
        string Id { get; }

        /// <summary>Gets the human readable name shown in result subtitles.</summary>
        string DisplayName { get; }

        /// <summary>Gets a value indicating whether this provider needs credentials to work.</summary>
        bool IsConfigured { get; }

        Task<TranslationOutcome> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken);
    }
}
