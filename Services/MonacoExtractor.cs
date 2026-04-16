using System;
using System.IO;

namespace MossadStudio.Services
{
    /// <summary>
    /// Locates the Monaco editor folder that ships alongside the executable.
    /// Monaco files live in a "Monaco\" subfolder next to MossadStudio.exe —
    /// no runtime extraction required.
    /// </summary>
    public static class MonacoExtractor
    {
        private static readonly string MonacoFolder = AppPaths.MonacoDir;

        /// <summary>
        /// Returns the absolute path to the Monaco folder.
        /// Throws <see cref="DirectoryNotFoundException"/> if the folder or
        /// editor.html is missing.
        /// </summary>
        public static string GetExtractedPath()
        {
            if (!Directory.Exists(MonacoFolder) ||
                !File.Exists(Path.Combine(MonacoFolder, "editor.html")))
            {
                throw new DirectoryNotFoundException(
                    $"Monaco editor files not found at:\n{MonacoFolder}\n\n" +
                    "Ensure the 'Monaco' folder is present next to the executable.");
            }

            return MonacoFolder;
        }
    }
}
