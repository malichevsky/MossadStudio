using System;
using System.IO;
using System.Threading.Tasks;

namespace MossadStudio.Services
{
    public static class CleanerService
    {
        public static async Task RunCleanupAsync(MainWindow parent)
        {
            await Task.Run(() => 
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cleaner_log.txt");
                try { File.AppendAllText(logPath, $"\n--- Cleanup initiated at {DateTime.Now} ---\n"); } catch {}

                void LogAction(string msg)
                {
                    try { File.AppendAllText(logPath, $"[INFO] {msg}\n"); } catch {}
                    if (parent != null)
                        parent.LogCleanerAction(msg);
                }

                void SafeDeleteFolder(string folderPath)
                {
                    if (Directory.Exists(folderPath))
                    {
                        try
                        {
                            Directory.Delete(folderPath, true);
                            LogAction($"Successfully wiped directory: {folderPath}");
                        }
                        catch (Exception ex)
                        {
                            LogAction($"Failed to clear {folderPath}. Files may be locked. Error: {ex.Message}");
                        }
                    }
                    else
                    {
                        LogAction($"Directory already clean (not found): {folderPath}");
                    }
                }

                LogAction("Terminating background tasks (Roblox, SirHurt) prior to deep cleaning...");
                
                try
                {
                    foreach (var p in System.Diagnostics.Process.GetProcessesByName("RobloxPlayerBeta"))
                    {
                        p.Kill();
                        LogAction($"Killed background Roblox instance (PID: {p.Id}).");
                    }
                    foreach (var p in System.Diagnostics.Process.GetProcessesByName("sirhurt"))
                    {
                        p.Kill();
                        LogAction($"Killed background SirHurt instance (PID: {p.Id}).");
                    }
                }
                catch (Exception e) { LogAction($"Process termination encountered an issue: {e.Message}"); }

                LogAction("Scrubbing user session data and telemetry caches...");

                string robloxFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox");
                SafeDeleteFolder(robloxFolder);

                string sirhurtFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "sirhurt");
                SafeDeleteFolder(sirhurtFolder);

                string sirstrapFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sirstrap");
                SafeDeleteFolder(sirstrapFolder);

                LogAction("Complete! System cache has been purified.");
            });
        }
    }
}
