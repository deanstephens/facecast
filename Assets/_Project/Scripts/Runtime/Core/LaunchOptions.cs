using System;
using UnityEngine;

namespace Facecast
{
    /// <summary>
    /// Start-up options. Desktop builds and the Editor take them on the command line (<c>-bots 4</c>,
    /// <c>-autocapture &lt;dir&gt;</c>); the web build takes them from the page address (<c>?bots=4</c>).
    /// </summary>
    public sealed class LaunchOptions
    {
        /// <summary>Demo bots to add. Unset (-1): three when playing offline (Editor, desktop), none on the web.</summary>
        public int Bots = -1;
        /// <summary>Folder for the screenshot tour (<see cref="ScreenshotTour"/>); null when not touring.</summary>
        public string AutoCapture;

        static LaunchOptions _current;
        public static LaunchOptions Current => _current ??= FromEnvironment();

        static LaunchOptions FromEnvironment() =>
            Application.platform == RuntimePlatform.WebGLPlayer ? FromUrl(Application.absoluteURL) : FromArgs(Environment.GetCommandLineArgs());

        public static LaunchOptions FromArgs(string[] args)
        {
            var o = new LaunchOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (args[i])
                {
                    case "-bots" when int.TryParse(next, out int n): o.Bots = Mathf.Clamp(n, 0, 8); break;
                    case "-autocapture": o.AutoCapture = next != null && !next.StartsWith("-") ? next : "Screenshots"; break;
                }
            }
            return o;
        }

        public static LaunchOptions FromUrl(string url)
        {
            var o = new LaunchOptions();
            if (string.IsNullOrEmpty(url)) return o;
            int hash = url.IndexOf('#');
            if (hash >= 0) url = url.Substring(0, hash);  // the room name, not options
            int q = url.IndexOf('?');
            if (q < 0) return o;
            string query = url.Substring(q + 1);
            foreach (var pair in query.Split('&'))
            {
                var kv = pair.Split(new[] { '=' }, 2);
                string key = Uri.UnescapeDataString(kv[0]);
                string value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
                if (key == "bots") o.Bots = int.TryParse(value, out int n) ? Mathf.Clamp(n, 0, 8) : 3;
            }
            return o;
        }
    }
}
