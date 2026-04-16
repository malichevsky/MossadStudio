using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MossadStudio.Services
{
    public class TabData
    {
        public string Title   { get; set; } = "";
        public string Content { get; set; } = "";
    }

    /// <summary>
    /// Persists open script tab titles + contents to
    ///   &lt;exe dir&gt;\saved_tabs\tabs.json
    /// so that tabs survive app restarts.
    /// </summary>
    public static class TabPersistenceService
    {
        public static readonly string SaveDir = AppPaths.SavedTabsDir;

        private static readonly string ManifestFile =
            Path.Combine(SaveDir, "tabs.json");

        private static readonly JsonSerializerOptions _json =
            new() { WriteIndented = true };

        /// <summary>
        /// Saves the given tab list to disk. Best-effort (silently ignores errors).
        /// </summary>
        public static void Save(IEnumerable<TabData> tabs)
        {
            try
            {
                Directory.CreateDirectory(SaveDir);
                File.WriteAllText(ManifestFile,
                    JsonSerializer.Serialize(tabs, _json));
            }
            catch { /* best-effort */ }
        }

        /// <summary>
        /// Loads the previously saved tab list, or an empty list on any error.
        /// </summary>
        public static List<TabData> Load()
        {
            try
            {
                if (!File.Exists(ManifestFile)) return new();
                return JsonSerializer.Deserialize<List<TabData>>(
                    File.ReadAllText(ManifestFile)) ?? new();
            }
            catch { return new(); }
        }
    }
}
