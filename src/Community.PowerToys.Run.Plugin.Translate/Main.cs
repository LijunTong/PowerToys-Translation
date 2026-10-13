// Copyright (c) Joont. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ManagedCommon;
using Wox.Plugin;

namespace Community.PowerToys.Run.Plugin.Translate
{
    /// <summary>
    /// PowerToys Run entry point.
    /// <para>
    /// The fast query pass answers immediately with a placeholder and the delayed pass performs the
    /// request, which is how the plugin avoids hitting the translation API once per keystroke. The
    /// host only replaces a plugin's earlier results when the delayed call returns a non-empty list,
    /// so the delayed pass here always yields exactly one result.
    /// </para>
    /// <para>
    /// Nothing in this class is allowed to throw: the host catches query exceptions but then shows
    /// nothing at all, and it invokes <c>Result.Action</c> with no exception handling whatsoever.
    /// </para>
    /// </summary>
    public class Main : IPlugin, IDelayedExecutionPlugin, IContextMenu, IReloadable, IDisposable
    {
        /// <summary>
        /// Must match the "ID" field in plugin.json. PowerToys uses it to de-duplicate plugins and
        /// to key the settings entry, and it must be a GUID without dashes.
        /// </summary>
        public static string PluginID => "FEC38B56721B49B888D221913E65C853";

        private PluginInitContext? _context;
        private PluginConfig _config = new PluginConfig();
        private TranslationService? _service;
        private string _iconPath = ResultFactory.IconDark;
        private bool _disposed;

        private readonly object _configLock = new object();
        private DateTime _configStamp = DateTime.MinValue;

        /// <summary>Poll interval while waiting for the typed text to settle.</summary>
        private const int DebouncePollMs = 25;

        private readonly object _debounceLock = new object();
        private string _lastSearch = string.Empty;
        private DateTime _lastSearchChangeUtc = DateTime.MinValue;

        /// <summary>Gets the name shown in the PowerToys settings list.</summary>
        public string Name => PluginStrings.PluginName;

        /// <summary>Gets the description shown in the PowerToys settings list.</summary>
        public string Description => PluginStrings.PluginDescription;

        public void Init(PluginInitContext context)
        {
            _context = context;

            // Init must not throw: PluginPair.InitializePlugin marks the plugin as permanently
            // failed if it does, and the user gets no results and no explanation.
            LoadConfig();

            try
            {
                if (context?.API != null)
                {
                    _iconPath = ResultFactory.IconFor(context.API.GetCurrentTheme());
                    context.API.ThemeChanged += OnThemeChanged;
                }
            }
            catch (Exception)
            {
                // Keep the default dark icon.
            }
        }

        /// <summary>Fast pass. Returns a placeholder so the launcher reacts immediately.</summary>
        public List<Result> Query(Query query)
        {
            return QueryInternal(query, delayedExecution: false);
        }

        /// <summary>Delayed pass, where the actual network request happens.</summary>
        public List<Result> Query(Query query, bool delayedExecution)
        {
            return QueryInternal(query, delayedExecution);
        }

        public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
        {
            try
            {
                return ResultFactory.ContextMenu(selectedResult, _context?.API);
            }
            catch (Exception)
            {
                return new List<ContextMenuResult>();
            }
        }

        /// <summary>
        /// Kept for completeness. Note that PowerToys 0.64 never calls
        /// <c>IPublicAPI.ReloadAllPluginData</c>, so this is unreachable in practice;
        /// <see cref="RefreshConfigIfChanged"/> is what actually makes an edited config take effect.
        /// </summary>
        public void ReloadData()
        {
            LoadConfig();
        }

        /// <summary>
        /// (Re)reads config.json and rebuilds the translation service.
        /// </summary>
        private void LoadConfig()
        {
            try
            {
                PluginConfig loaded = PluginConfig.Load();

                lock (_configLock)
                {
                    _config = loaded;

                    // The previous service is deliberately not disposed: an in-flight query may
                    // still be using it, and dropping the reference is enough. Neither the service
                    // nor its token sources hold unmanaged handles.
                    _service = new TranslationService(loaded);
                    _configStamp = GetConfigStamp();
                }
            }
            catch (Exception)
            {
                lock (_configLock)
                {
                    _service = null;
                }
            }
        }

        /// <summary>
        /// Re-reads config.json when it changed on disk.
        /// <para>
        /// The plugin reads its configuration during <c>Init</c>, and PowerToys 0.64 provides no UI
        /// that triggers a plugin reload ("reload plugin data" is never invoked by that version).
        /// Without this check an edited appid or secretKey would be silently ignored until PowerToys
        /// Run restarted, which looks exactly like "the key was not saved".
        /// </para>
        /// </summary>
        private void RefreshConfigIfChanged()
        {
            try
            {
                DateTime stamp = GetConfigStamp();
                lock (_configLock)
                {
                    if (stamp == _configStamp)
                    {
                        return;
                    }
                }

                LoadConfig();
            }
            catch (Exception)
            {
                // Keep whatever configuration is already loaded.
            }
        }

        private static DateTime GetConfigStamp()
        {
            try
            {
                string path = PluginConfig.ConfigPath;
                return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }

        /// <summary>
        /// Records the text of the current query so the debounce wait can tell when typing stopped.
        /// Called from both the fast and the delayed pass; only an actual change of text moves the
        /// timestamp, so the repeated pass belonging to one keystroke does not reset it.
        /// </summary>
        private void NoteSearch(string search)
        {
            lock (_debounceLock)
            {
                if (string.Equals(_lastSearch, search, StringComparison.Ordinal))
                {
                    return;
                }

                _lastSearch = search;
                _lastSearchChangeUtc = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Blocks until the typed text has been unchanged for <paramref name="debounceMs"/>.
        /// <para>
        /// The host queries the plugin on every keystroke and each query used to start an HTTP
        /// request immediately, only to cancel it on the next key. Waiting for the text to settle
        /// means a fast typist causes one request instead of one per key.
        /// </para>
        /// <para>
        /// Waiting is deliberate rather than returning the placeholder early: the host only re-runs
        /// the delayed query when the query text changes, so bailing out here would leave
        /// "正在翻译…" on screen forever. The wait is capped so continuous typing can never hang the
        /// launcher, and a 0 debounce skips it entirely.
        /// </para>
        /// </summary>
        private void WaitForSettledText(int debounceMs)
        {
            if (debounceMs <= 0)
            {
                return;
            }

            // Always leave room for the debounce itself, so a large debounceMs still gets honoured.
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(debounceMs * 2, 2000));

            while (!_disposed && DateTime.UtcNow < deadline)
            {
                DateTime changedUtc;
                lock (_debounceLock)
                {
                    changedUtc = _lastSearchChangeUtc;
                }

                if (changedUtc == DateTime.MinValue
                    || (int)(DateTime.UtcNow - changedUtc).TotalMilliseconds >= debounceMs)
                {
                    return;
                }

                Thread.Sleep(DebouncePollMs);
            }
        }

        /// <summary>
        /// True when <paramref name="search"/> is still what the user has typed, i.e. the user has
        /// not moved on to a longer query while this pass was waiting.
        /// </summary>
        private bool IsCurrentSearch(string search)
        {
            lock (_debounceLock)
            {
                return string.Equals(_lastSearch, search, StringComparison.Ordinal);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (!disposing)
            {
                return;
            }

            try
            {
                if (_context?.API != null)
                {
                    _context.API.ThemeChanged -= OnThemeChanged;
                }
            }
            catch (Exception)
            {
                // Nothing useful to do while shutting down.
            }

            _service?.Dispose();
            _service = null;
        }

        private void OnThemeChanged(Theme currentTheme, Theme newTheme)
        {
            _iconPath = ResultFactory.IconFor(newTheme);
        }

        private List<Result> QueryInternal(Query? query, bool delayedExecution)
        {
            var results = new List<Result>();

            try
            {
                if (query == null)
                {
                    return results;
                }

                // Pick up an edited config.json without needing a PowerToys restart.
                RefreshConfigIfChanged();
                PluginConfig config = _config;

                string search = query.Search ?? string.Empty;

                // Record this text before anything else so the delayed pass can tell whether the
                // user kept typing while it waited.
                NoteSearch(search);

                ParsedQuery parsed = QueryParser.Parse(
                    search,
                    config.DefaultTarget ?? "zh",
                    config.AutoDirection);

                var context = new ResultContext
                {
                    IconPath = _iconPath,
                    Search = search,
                    RawQuery = query.RawQuery ?? string.Empty,
                    Api = _context?.API,
                    ConfigWarning = config.LoadWarning,
                };

                if (!parsed.HasText)
                {
                    // Usage and "nothing to translate" hints only come from the delayed pass, so the
                    // fast pass does not flash a placeholder before them.
                    if (delayedExecution)
                    {
                        results.Add(ResultFactory.Notice(
                            context,
                            parsed.Notice ?? PluginStrings.UsageTitle,
                            parsed.NoticeDetail));
                    }

                    return results;
                }

                if (!delayedExecution)
                {
                    results.Add(ResultFactory.Placeholder(context, parsed.Source, parsed.Target));
                    return results;
                }

                // Debounce: hold the request back until the user stops typing.
                WaitForSettledText(config.DebounceMs);

                if (!IsCurrentSearch(search))
                {
                    // The user typed more while this pass was waiting. A newer query is already on
                    // its way and will translate the newer text, so returning the placeholder here
                    // keeps this superseded pass from spending a request on text nobody wants.
                    results.Add(ResultFactory.Placeholder(context, parsed.Source, parsed.Target));
                    return results;
                }

                results.Add(Translate(context, parsed));
                return results;
            }
            catch (Exception e)
            {
                // Never propagate: the host would log and silently drop the results.
                try
                {
                    results.Clear();
                    results.Add(ResultFactory.Notice(
                        new ResultContext { IconPath = _iconPath, Search = query?.Search ?? string.Empty },
                        PluginStrings.TranslateFailedTitle,
                        e.GetType().Name + ": " + e.Message));
                }
                catch (Exception)
                {
                    // Give up gracefully.
                }

                return results;
            }
        }

        private Result Translate(ResultContext context, ParsedQuery parsed)
        {
            if (_service == null)
            {
                var failed = new TranslationResult(
                    TranslationOutcome.Fail("init", "翻译服务未初始化", "插件初始化失败，请查看 PowerToys Run 日志"),
                    string.Empty);

                return ResultFactory.Failure(context, failed, parsed, notConfigured: false);
            }

            // Abandon whatever the previous keystroke started, so a superseded request stops before
            // it consumes the API quota.
            _service.CancelPending();

            TranslationResult result = _service.Translate(new TranslationRequest(parsed.Text, parsed.Source, parsed.Target));

            if (result.Outcome.Cancelled)
            {
                // The host drops this query's results anyway (it compares the query text), but an
                // empty delayed result would leave the placeholder stuck on screen.
                return ResultFactory.Placeholder(context, parsed.Source, parsed.Target);
            }

            if (result.Outcome.Success)
            {
                return ResultFactory.Success(context, result, parsed);
            }

            bool notConfigured = string.Equals(result.Outcome.ErrorCode, "not-configured", StringComparison.Ordinal);
            return ResultFactory.Failure(context, result, parsed, notConfigured);
        }
    }
}
