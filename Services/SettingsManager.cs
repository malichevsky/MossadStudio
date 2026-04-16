using System;
using System.IO;
using System.Text.Json;

namespace MossadStudio.Services
{
    public class HiddenFlags
    {
        /// <summary>
        /// Skip the bootstrapper entirely. DLL and exe presence is NOT verified.
        /// INCOMPATIBLE with ForceRedownload — app will shut down if both are true.
        /// </summary>
        public bool SkipBootstrapper { get; set; } = false;

        /// <summary>
        /// Skip SirHurt API and Roblox version checks.
        /// Versions display as "N/A (skipped)" and version.txt is deleted on launch.
        /// </summary>
        public bool SkipSirHurtUpdateCheck { get; set; } = false;

        /// <summary>
        /// Skip GitHub release polling. The studio will not offer self-updates.
        /// </summary>
        public bool SkipStudioUpdateCheck { get; set; } = false;

        /// <summary>
        /// Force a full re-download of SirHurt core on every launch, ignoring version cache.
        /// INCOMPATIBLE with SkipBootstrapper — app will shut down if both are true.
        /// </summary>
        public bool ForceRedownload { get; set; } = false;

        /// <summary>
        /// Emit verbose diagnostic output to the Output tab and Bootstrapper_log.txt.
        /// </summary>
        public bool VerboseLogging { get; set; } = false;
    }

    public class MossadConfig
    {
        public bool AutoAttach   { get; set; } = false;
        public bool TopMost      { get; set; } = false;
        public bool FadeEffects  { get; set; } = false;
        public bool DiscordRPC   { get; set; } = false;  // reserved
        public bool FpsUnlocker  { get; set; } = false;  // reserved
        public bool AutoExecute  { get; set; } = false;  // reserved (UI removed)
        public bool MonacoEditor { get; set; } = false;
        public HiddenFlags HiddenFlags { get; set; } = new HiddenFlags();
    }

    public static class SettingsManager
    {
        public static string BinDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin");
        public static string ConfigPath => Path.Combine(BinDir, "config.json");

        public static MossadConfig Config { get; private set; } = new MossadConfig();

        public static void Load()
        {
            try
            {
                if (!Directory.Exists(BinDir))
                    Directory.CreateDirectory(BinDir);

                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var deserialized = JsonSerializer.Deserialize<MossadConfig>(json);
                    if (deserialized != null)
                    {
                        Config = deserialized;
                        // Write back immediately — this adds any new fields introduced
                        // in this version of the app (using their C# default values)
                        // without touching the user's existing settings. Free migration.
                        Save();
                    }
                }
                else
                {
                    Save(); // Create default config on first run
                }
            }
            catch (Exception) { /* Fail silently, reverting to defaults */ }
        }

        public static void Save()
        {
            try
            {
                if (!Directory.Exists(BinDir))
                    Directory.CreateDirectory(BinDir);

                string json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Returns a list of human-readable descriptions for every hidden flag that is currently true.
        /// </summary>
        public static List<(string Name, string Description)> GetActiveHiddenFlags()
        {
            var f = Config.HiddenFlags;
            var active = new List<(string, string)>();

            if (f.SkipBootstrapper)
                active.Add(("SkipBootstrapper", "The bootstrapper is bypassed. SirHurt files are NOT verified."));
            if (f.SkipSirHurtUpdateCheck)
                active.Add(("SkipSirHurtUpdateCheck", "SirHurt API / Roblox version checks are skipped. version.txt will be deleted."));
            if (f.SkipStudioUpdateCheck)
                active.Add(("SkipStudioUpdateCheck", "GitHub release polling is skipped. No self-update offers."));
            if (f.ForceRedownload)
                active.Add(("ForceRedownload", "SirHurt core is fully re-downloaded on every launch."));
            if (f.VerboseLogging)
                active.Add(("VerboseLogging", "Extra diagnostic output is written to Output tab and log file."));

            return active;
        }
    }
}
