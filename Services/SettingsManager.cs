using System;
using System.IO;
using System.Text.Json;

namespace MossadStudio.Services
{
    public class MossadConfig
    {
        public bool AutoAttach { get; set; } = false;
        public bool TopMost { get; set; } = false;
        public bool FadeEffects { get; set; } = false;
        public bool DiscordRPC { get; set; } = false;
        public bool FpsUnlocker { get; set; } = false;
        public bool AutoExecute { get; set; } = false;
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
                        Config = deserialized;
                }
                else
                {
                    Save(); // Create default
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
    }
}
