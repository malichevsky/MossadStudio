using System.Text;
using System.Windows;
using MossadStudio.Services;

namespace MossadStudio
{
    public static class HiddenFlagsWarning
    {
        /// <summary>
        /// Checks hidden flag state at startup.
        /// - If SkipBootstrapper + ForceRedownload are both true: shows a critical error and returns false (caller must shut down).
        /// - If any other dangerous flags are active: shows a warning dialog requiring explicit confirmation.
        /// - Returns true if it is safe to proceed, false if the app must exit.
        /// </summary>
        public static bool CheckAndPrompt()
        {
            var flags = SettingsManager.Config.HiddenFlags;

            // Hard mutual exclusion: no prompt, no bypass, just exit
            if (flags.SkipBootstrapper && flags.ForceRedownload)
            {
                MessageBox.Show(
                    "CRITICAL CONFIGURATION ERROR\n\n" +
                    "You have enabled both:\n" +
                    "  • SkipBootstrapper\n" +
                    "  • ForceRedownload\n\n" +
                    "These flags are mutually exclusive and CANNOT be used together.\n" +
                    "SkipBootstrapper bypasses the entire download pipeline, making\n" +
                    "ForceRedownload a meaningless no-op.\n\n" +
                    "Edit config.json and set exactly ONE of these to true.\n" +
                    "Mossad Studio will now shut down.",
                    "Incompatible Hidden Flags — Fatal Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Stop);

                return false;
            }

            // General hidden flags warning
            var activeFlags = SettingsManager.GetActiveHiddenFlags();
            if (activeFlags.Count == 0)
                return true; // Nothing active, proceed silently

            var sb = new StringBuilder();
            sb.AppendLine("Heads-up! You have manually enabled some hidden flags.");
            sb.AppendLine();
            sb.AppendLine("The following flags have been enabled in config.json:");
            sb.AppendLine();

            foreach (var (name, description) in activeFlags)
            {
                sb.AppendLine($"  • {name}");
                sb.AppendLine($"    {description}");
                sb.AppendLine();
            }

            sb.AppendLine("These flags bypass normal safety checks and may cause instability and unexpected behavior.");
            sb.AppendLine("Only proceed if you understand the consequences.");
            sb.AppendLine();
            sb.AppendLine("Press OK to continue, or Cancel to shut down.");

            var result = MessageBox.Show(
                sb.ToString(),
                "Hidden Flags Warning",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            return result == MessageBoxResult.OK;
        }
    }
}
