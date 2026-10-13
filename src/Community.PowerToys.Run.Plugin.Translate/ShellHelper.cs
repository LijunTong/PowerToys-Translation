// Copyright (c) Joont. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Diagnostics;

namespace Community.PowerToys.Run.Plugin.Translate
{
    /// <summary>Shell interactions that must never throw: they run inside result actions.</summary>
    internal static class ShellHelper
    {
        public static bool OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool OpenFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                // explorer.exe opens its own window and returns an exit code we must not inspect.
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true })?.Dispose();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
