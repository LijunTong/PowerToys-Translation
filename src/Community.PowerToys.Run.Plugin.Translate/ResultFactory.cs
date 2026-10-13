// Copyright (c) Joont. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using ManagedCommon;
using Wox.Plugin;

namespace Community.PowerToys.Run.Plugin.Translate
{
    /// <summary>
    /// Everything the factory needs that does not come from the translation itself.
    /// </summary>
    internal sealed class ResultContext
    {
        public string IconPath { get; set; } = ResultFactory.IconDark;

        /// <summary>Gets or sets the text the user typed after the action keyword.</summary>
        public string Search { get; set; } = string.Empty;

        /// <summary>Gets or sets the full query including the action keyword, for re-querying.</summary>
        public string RawQuery { get; set; } = string.Empty;

        /// <summary>Gets or sets the host API, used to re-query for the swap and retry actions.</summary>
        public IPublicAPI? Api { get; set; }

        /// <summary>Gets or sets a configuration problem to surface in every subtitle.</summary>
        public string? ConfigWarning { get; set; }
    }

    /// <summary>
    /// Builds <see cref="Result"/> instances. Centralised so that icons, scores, the query text
    /// display and the never-throw guarantee are applied consistently.
    /// </summary>
    internal static class ResultFactory
    {
        /// <summary>
        /// Relative on purpose: PowerToys rewrites <c>Result.IcoPath</c> to an absolute path when it
        /// assigns <c>Result.PluginDirectory</c>, which is how the built-in plugins do it too.
        /// </summary>
        public const string IconDark = "Images/translate.dark.png";

        public const string IconLight = "Images/translate.light.png";

        private const int TranslationScore = 300;
        private const string IconFont = "Segoe MDL2 Assets";

        public static string IconFor(Theme theme)
        {
            return theme == Theme.Light || theme == Theme.HighContrastWhite ? IconLight : IconDark;
        }

        /// <summary>Shown by the fast query pass while the delayed pass runs the request.</summary>
        public static Result Placeholder(ResultContext context, string source, string target)
        {
            return new Result
            {
                Title = PluginStrings.PlaceholderTitle,
                SubTitle = Direction(source, target),
                IcoPath = context.IconPath,
                Score = 0,
                QueryTextDisplay = context.Search,
                ContextData = new ResultPayload { Search = context.Search },
            };
        }

        /// <summary>Used for usage hints and other "nothing to translate" situations.</summary>
        public static Result Notice(ResultContext context, string title, string? detail)
        {
            return new Result
            {
                Title = title,
                SubTitle = Compose(detail, context.ConfigWarning),
                IcoPath = context.IconPath,
                Score = TranslationScore,
                QueryTextDisplay = context.Search,
                ContextData = new ResultPayload { Search = context.Search },
            };
        }

        public static Result Success(ResultContext context, TranslationResult result, ParsedQuery parsed)
        {
            string source = SourceLabel(parsed, result);
            var payload = new ResultPayload
            {
                Search = context.Search,
                SourceText = parsed.Text,
                TranslatedText = result.Outcome.TranslatedText,
                Source = parsed.Source,
                Target = parsed.Target,
                DetectedSource = result.Outcome.DetectedSource,
                HasTranslation = true,
            };

            return new Result
            {
                Title = result.Outcome.TranslatedText,
                SubTitle = Compose(BuildSuccessSubTitle(result, source, parsed.Target), context.ConfigWarning),
                IcoPath = context.IconPath,
                Score = TranslationScore,
                QueryTextDisplay = context.Search,
                ContextData = payload,
                Action = _ => CopyWithFeedback(result.Outcome.TranslatedText),
            };
        }

        public static Result Failure(ResultContext context, TranslationResult result, ParsedQuery parsed, bool notConfigured)
        {
            string title = notConfigured
                ? PluginStrings.NotConfiguredTitle
                : PluginStrings.TranslateFailedTitle + "：" + FirstLine(result.Outcome.UserHint, result.Outcome.ErrorMessage);

            string detail = notConfigured
                ? result.Outcome.UserHint
                : DescribeError(result, parsed);

            var payload = new ResultPayload
            {
                Search = context.Search,
                SourceText = parsed.Text,
                Source = parsed.Source,
                Target = parsed.Target,
                DetectedSource = result.Outcome.DetectedSource,
                NotConfigured = notConfigured,
            };

            Result built = new Result
            {
                Title = title,
                SubTitle = Compose(detail, context.ConfigWarning),
                IcoPath = context.IconPath,
                Score = TranslationScore,
                QueryTextDisplay = context.Search,
                ContextData = payload,
            };

            if (notConfigured)
            {
                built.Action = _ => OpenConfigFolder();
            }
            else if (context.Api != null)
            {
                // Forcing a requery is the only way a plugin can retry: the failed attempt was not
                // cached, so this goes straight back to the network.
                string rawQuery = context.RawQuery;
                IPublicAPI api = context.Api;
                built.Action = _ => Requery(api, rawQuery);
            }

            return built;
        }

        public static List<ContextMenuResult> ContextMenu(Result selected, IPublicAPI? api)
        {
            var items = new List<ContextMenuResult>();
            if (selected?.ContextData is not ResultPayload payload)
            {
                return items;
            }

            if (payload.HasTranslation && payload.TranslatedText.Length > 0)
            {
                string translated = payload.TranslatedText;
                items.Add(new ContextMenuResult
                {
                    Title = PluginStrings.ContextMenuCopyTranslation,
                    Glyph = "\uE8C8",
                    FontFamily = IconFont,
                    AcceleratorKey = System.Windows.Input.Key.C,
                    AcceleratorModifiers = System.Windows.Input.ModifierKeys.Control,
                    Action = _ => CopyWithFeedback(translated),
                });
            }

            if (payload.SourceText.Length > 0)
            {
                string sourceText = payload.SourceText;
                items.Add(new ContextMenuResult
                {
                    Title = PluginStrings.ContextMenuCopySource,
                    Glyph = "\uE8C8",
                    FontFamily = IconFont,
                    AcceleratorKey = System.Windows.Input.Key.C,
                    AcceleratorModifiers = System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift,
                    Action = _ => CopyWithFeedback(sourceText),
                });
            }

            string swapSource = ResolveSwapSource(payload);
            if (api != null
                && payload.SourceText.Length > 0
                && swapSource.Length > 0
                && payload.Target.Length > 0
                && !swapSource.Equals(payload.Target, StringComparison.OrdinalIgnoreCase))
            {
                string query = "tr " + payload.Target + ":" + swapSource + " " + payload.SourceText;
                IPublicAPI localApi = api;
                items.Add(new ContextMenuResult
                {
                    Title = PluginStrings.ContextMenuSwap,
                    Glyph = "\uE8AB",
                    FontFamily = IconFont,
                    AcceleratorKey = System.Windows.Input.Key.S,
                    AcceleratorModifiers = System.Windows.Input.ModifierKeys.Control,
                    Action = _ => Requery(localApi, query),
                });
            }

            if (payload.SourceText.Length > 0 && payload.Target.Length > 0)
            {
                string url = WebUrl(payload, swapSource);
                items.Add(new ContextMenuResult
                {
                    Title = PluginStrings.ContextMenuOpenWeb,
                    Glyph = "\uE774",
                    FontFamily = IconFont,
                    AcceleratorKey = System.Windows.Input.Key.O,
                    AcceleratorModifiers = System.Windows.Input.ModifierKeys.Control,
                    Action = _ => ShellHelper.OpenUrl(url),
                });
            }

            items.Add(new ContextMenuResult
            {
                Title = PluginStrings.ContextMenuOpenConfigFolder,
                Glyph = "\uE8B7",
                FontFamily = IconFont,
                AcceleratorKey = System.Windows.Input.Key.O,
                AcceleratorModifiers = System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift,
                Action = _ => OpenConfigFolder(),
            });

            return items;
        }

        private static string DescribeError(TranslationResult result, ParsedQuery parsed)
        {
            var builder = new StringBuilder();
            builder.Append(Direction(SourceLabel(parsed, result), parsed.Target));

            if (!string.IsNullOrEmpty(result.Outcome.ErrorCode))
            {
                builder.Append(" · 错误码 ").Append(result.Outcome.ErrorCode);
            }

            if (!string.IsNullOrEmpty(result.ProviderName))
            {
                builder.Append(" · ").Append(result.ProviderName);
            }

            string hint = FirstLine(result.Outcome.UserHint, result.Outcome.ErrorMessage);
            if (hint.Length > 0)
            {
                builder.Append(" · ").Append(hint);
            }

            return builder.ToString();
        }

        private static string BuildSuccessSubTitle(TranslationResult result, string source, string target)
        {
            var builder = new StringBuilder();
            builder.Append(result.ProviderName);
            builder.Append(" · ").Append(source).Append(" → ").Append(LanguageCatalog.Display(target));

            if (result.FromCache)
            {
                builder.Append(" · ").Append(PluginStrings.CachedNote);
            }

            if (result.Truncated)
            {
                builder.Append(" · ").Append(PluginStrings.TruncatedNote);
            }

            builder.Append(" · ").Append(PluginStrings.ActionCopyTranslation);
            return builder.ToString();
        }

        private static string Direction(string source, string target)
        {
            return source + " → " + LanguageCatalog.Display(target);
        }

        private static string SourceLabel(ParsedQuery parsed, TranslationResult result)
        {
            if (!string.IsNullOrEmpty(result.Outcome.DetectedSource))
            {
                return LanguageCatalog.Display(result.Outcome.DetectedSource);
            }

            return parsed.Source.Equals(LanguageCatalog.Auto, StringComparison.OrdinalIgnoreCase)
                ? "自动检测"
                : LanguageCatalog.Display(parsed.Source);
        }

        private static string ResolveSwapSource(ResultPayload payload)
        {
            if (!string.IsNullOrEmpty(payload.DetectedSource))
            {
                return payload.DetectedSource;
            }

            return payload.Source.Equals(LanguageCatalog.Auto, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : payload.Source;
        }

        private static string WebUrl(ResultPayload payload, string swapSource)
        {
            string from = swapSource.Length > 0 ? LanguageCatalog.ToBaidu(swapSource) ?? "auto" : "auto";
            string to = LanguageCatalog.ToBaidu(payload.Target) ?? "zh";
            return "https://fanyi.baidu.com/#" + from + "/" + to + "/" + Uri.EscapeDataString(payload.SourceText);
        }

        private static string? Compose(string? detail, string? warning)
        {
            if (string.IsNullOrEmpty(warning))
            {
                return detail;
            }

            string prefix = PluginStrings.ConfigInvalidPrefix + warning;
            return string.IsNullOrEmpty(detail) ? prefix : prefix + " · " + detail;
        }

        private static string FirstLine(string preferred, string fallback)
        {
            string value = string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            int index = value.IndexOfAny(new[] { '\r', '\n' });
            string line = index < 0 ? value : value.Substring(0, index);
            return line.Trim();
        }

        private static bool CopyWithFeedback(string text)
        {
            if (!ClipboardHelper.Copy(text))
            {
                try
                {
                    System.Windows.MessageBox.Show(
                        PluginStrings.ClipboardFailedSubTitle,
                        PluginStrings.ClipboardFailedTitle);
                }
                catch (Exception)
                {
                    // Never let feedback break the action.
                }
            }

            return true;
        }

        private static bool OpenConfigFolder()
        {
            return ShellHelper.OpenFolder(PluginConfig.PluginDirectory);
        }

        /// <summary>
        /// Replaces the query text and lets the launcher re-run it. Returns <c>false</c> so the
        /// launcher stays open and the user actually sees the new result.
        /// </summary>
        private static bool Requery(IPublicAPI api, string query)
        {
            try
            {
                api.ChangeQuery(query, true);
            }
            catch (Exception)
            {
                // Never let a re-query failure break the action.
            }

            return false;
        }
    }
}
