using System;
using System.IO;
using System.Linq;

namespace MossadStudio.Services
{
    public static class FPSUnlocker
    {
        public static void Apply()
        {
            if (!SettingsManager.Config.FpsUnlocker) return;

            try
            {
                string versionsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions");
                if (!Directory.Exists(versionsFolder)) return;

                // Find valid Roblox installation dir
                foreach (string dir in Directory.GetDirectories(versionsFolder))
                {
                    if (File.Exists(Path.Combine(dir, "RobloxPlayerBeta.exe")))
                    {
                        string clientSettingsDir = Path.Combine(dir, "ClientSettings");
                        if (!Directory.Exists(clientSettingsDir)) Directory.CreateDirectory(clientSettingsDir);

                        string appSettingsFile = Path.Combine(clientSettingsDir, "ClientAppSettings.json");
                        
                        string payload = "{\n  \"DFIntTaskSchedulerTargetFps\": 1000\n}";
                        
                        // Overwrite completely for simplicity, avoiding complex JSON merges if unneeded
                        File.WriteAllText(appSettingsFile, payload);
                    }
                }
            }
            catch { }
        }
    }

    public static class DiscordRpcManager
    {
        // For a deep robust RPC, we would typically bring in DiscordRPC C# wrapper via nuget.
        // As a minimal stub, we sync the state here.
        public static void UpdateState()
        {
            if (!SettingsManager.Config.DiscordRPC)
            {
                Disable();
            }
            else
            {
                Enable();
            }
        }

        private static void Enable()
        {
            // Placeholder: Initialize Discord IPC Pipe and send 'Playing Mossad Studio' Activity
        }

        private static void Disable()
        {
            // Placeholder: Teardown Discord IPC Pipe
        }
    }
}
