using System;
using System.IO;
using System.Reflection;
using System.Linq;

namespace MossadStudio.Services
{
    /// <summary>
    /// Handles the extraction of the Monaco editor from embedded resources.
    /// This ensures Mossad Studio is truly a single-exe application that can
    /// repair its own dependencies at runtime.
    /// </summary>
    public static class MonacoExtractor
    {
        private static readonly string MonacoFolder = AppPaths.MonacoDir;

        /// <summary>
        /// Ensures the Monaco folder is correctly populated on disk and returns its path.
        /// If files are missing, they are extracted from the executable's resources.
        /// </summary>
        public static string GetExtractedPath()
        {
            // If the folder is missing or the main editor.html is gone, perform full extraction.
            if (!Directory.Exists(MonacoFolder) || 
                !File.Exists(Path.Combine(MonacoFolder, "editor.html")))
            {
                ExtractMonacoResources();
            }

            return MonacoFolder;
        }

        private static void ExtractMonacoResources()
        {
            var assembly = Assembly.GetExecutingAssembly();
            // Expected resource prefix: "MossadStudio.Monaco."
            // All files in Monaco/** become MossadStudio.Monaco.path.to.file
            string prefix = "MossadStudio.Monaco.";
            string[] resourceNames = assembly.GetManifestResourceNames();

            foreach (string resource in resourceNames)
            {
                if (resource.StartsWith(prefix))
                {
                    // Convert resource name back to a relative file path
                    // Note: .NET resource conversion is lossy for folders (it uses dots),
                    // but we can map them back if we know the structure.
                    // Actually, for editor.html it's MossadStudio.Monaco.editor.html
                    // For vs/loader.js it's MossadStudio.Monaco.vs.loader.js
                    
                    string relativePath = resource.Substring(prefix.Length);
                    
                    // We need to be careful with dots in filenames (e.g. editor.main.js).
                    // .NET replaces / with . in resource names.
                    // Since all Monaco files have extensions, the last dot is the extension.
                    // This logic is slightly brittle for complex folder names with dots, 
                    // but Monaco's structure is manageable.
                    
                    string diskPath = ResolveDiskPath(relativePath);
                    string fullPath = Path.Combine(MonacoFolder, diskPath);

                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

                    using Stream? stream = assembly.GetManifestResourceStream(resource);
                    if (stream != null)
                    {
                        using FileStream fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
                        stream.CopyTo(fileStream);
                    }
                }
            }
        }

        private static string ResolveDiskPath(string resourceSuffix)
        {
            // Monaco specifically has a 'vs/' folder. 
            // In resources, 'Monaco/vs/loader.js' -> 'Monaco.vs.loader.js'
            // In resources, 'Monaco/editor.html' -> 'Monaco.editor.html'
            
            // Heuristic: If it starts with 'vs.', it's in the 'vs' folder.
            if (resourceSuffix.StartsWith("vs."))
            {
                // We know Monaco folders usually don't have dots, but files do.
                // vs/editor/editor.main.js -> vs.editor.editor.main.js
                // We'll replace dots with slashes, then put the last dots back for the extension?
                // Actually, a safer way to embed many files is to use a Manifest or just be specific.
                // But for now, since we know the Monaco structure:
                
                if (resourceSuffix.Contains("editor.main.js")) return Path.Combine("vs", "editor", "editor.main.js");
                if (resourceSuffix.Contains("editor.main.css")) return Path.Combine("vs", "editor", "editor.main.css");
                if (resourceSuffix.Contains("loader.js")) return Path.Combine("vs", "loader.js");
                if (resourceSuffix.Contains("codicon.ttf")) return Path.Combine("vs", "base", "browser", "ui", "codicons", "codicon", "codicon.ttf");
                
                // Fallback: replace first occurrence of vs. with vs/
                return resourceSuffix.Replace(".", Path.DirectorySeparatorChar.ToString());
            }

            return resourceSuffix;
        }
    }
}
