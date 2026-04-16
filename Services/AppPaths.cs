using System;
using System.IO;

namespace MossadStudio.Services
{
    /// <summary>
    /// Single source of truth for every runtime directory used by Mossad Studio.
    /// All user-facing folders live inside  &lt;exe dir&gt;\bin\  so the root
    /// only contains the executable and its native dependencies.
    /// </summary>
    public static class AppPaths
    {
        // Root: the directory that contains MossadStudio.exe
        public static readonly string ExeDir =
            AppDomain.CurrentDomain.BaseDirectory;

        // bin\ — all runtime-generated / user-visible folders go here
        public static readonly string BinDir =
            Path.Combine(ExeDir, "bin");

        // Monaco editor files (Content → shipped next to exe, now under bin\Monaco\)
        public static readonly string MonacoDir =
            Path.Combine(BinDir, "Monaco");

        // WebView2 user-data cache (prevents the .exe.WebView2 clutter in root)
        public static readonly string WebView2DataDir =
            Path.Combine(BinDir, "WebView2");

        // Tab session persistence
        public static readonly string SavedTabsDir =
            Path.Combine(BinDir, "saved_tabs");

        // Config / scripts / workspace — already inside bin\ by app convention
        // (exposed here for reference / future use)
        public static readonly string ConfigDir  = BinDir;
        public static readonly string ScriptsDir = Path.Combine(BinDir, "scripts");
    }
}
