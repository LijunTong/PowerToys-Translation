// Copyright (c) Joont. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Threading;

namespace Community.PowerToys.Run.Plugin.Translate
{
    /// <summary>
    /// Clipboard access that never throws.
    /// <para>
    /// The host invokes <c>Result.Action</c> inside <c>Dispatcher.Invoke</c> with no surrounding
    /// try/catch, so an exception here would surface as an unhandled dispatcher exception. The
    /// clipboard is also shared with every other process, which makes "open failed" a normal race
    /// rather than an exceptional condition, hence the retries.
    /// </para>
    /// </summary>
    internal static class ClipboardHelper
    {
        private const int Attempts = 3;
        private const int RetryDelayMs = 60;

        public static bool Copy(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                return CopyOnCurrentThread(text);
            }

            // Defensive: the WPF dispatcher thread is STA, but never rely on the caller.
            bool copied = false;
            var thread = new Thread(() => copied = CopyOnCurrentThread(text))
            {
                IsBackground = true,
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(TimeSpan.FromSeconds(2));
            return copied;
        }

        private static bool CopyOnCurrentThread(string text)
        {
            for (int attempt = 1; attempt <= Attempts; attempt++)
            {
                try
                {
                    // SetDataObject with copy:true keeps the value on the clipboard after the
                    // process that set it moves on, and retries internally.
                    System.Windows.Clipboard.SetDataObject(text, true);
                    return true;
                }
                catch (Exception)
                {
                    if (attempt < Attempts)
                    {
                        Thread.Sleep(RetryDelayMs);
                    }
                }
            }

            return false;
        }
    }
}
