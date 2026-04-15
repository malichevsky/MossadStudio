using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace MossadStudio.Services
{
    public static class SirHurtAPI
    {
        private static readonly HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        
        // Maps directly to the executable path so files are locally accessible
        public static readonly string CoreDir = AppDomain.CurrentDomain.BaseDirectory;
        
        // This maps to `dirs::data_dir().join("sirhurt").join("sirhui")` from `scripts.rs`
        public static readonly string SirHurtDatDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "sirhurt", "sirhui");
        
        public static readonly string CoreExePath = Path.Combine(CoreDir, "sirhurt.exe");

        public static readonly string WorkspaceDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workspace");
        public static readonly string ScriptsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts");
        public static readonly string AutoexecDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "autoexe");

        public static string shExploitVersion = "Unknown";
        public static string RobloxLiveVersion = "Unknown";

        static SirHurtAPI()
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Sentinel/1.0");
            if (!Directory.Exists(CoreDir))
                Directory.CreateDirectory(CoreDir);
            if (!Directory.Exists(SirHurtDatDir))
                Directory.CreateDirectory(SirHurtDatDir);
            
            if (!Directory.Exists(WorkspaceDir)) Directory.CreateDirectory(WorkspaceDir);
            if (!Directory.Exists(ScriptsDir)) Directory.CreateDirectory(ScriptsDir);
            if (!Directory.Exists(AutoexecDir)) Directory.CreateDirectory(AutoexecDir);
        }

        public static async Task<bool> DownloadCoreAsync(Action<string> logCallback, bool forceRedownload = false)
        {
            try
            {
                logCallback("Fetching LIVE Roblox version hash...");
                string rbText = await client.GetStringAsync("https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer");
                using JsonDocument rbDoc = JsonDocument.Parse(rbText);
                string robloxLive = rbDoc.RootElement.GetProperty("clientVersionUpload").GetString();
                RobloxLiveVersion = robloxLive ?? "Unknown";
                logCallback($"Live Roblox Version: {robloxLive}");

                logCallback("Checking SirHurt API status...");
                string shText = await client.GetStringAsync("https://sirhurt.net/status/fetch.php?exploit=SirHurt%20V5");
                using JsonDocument shDoc = JsonDocument.Parse(shText);
                var shNode = shDoc.RootElement[0].GetProperty("SirHurt V5");
                shExploitVersion = shNode.GetProperty("exploit_version").GetString();
                string shRobloxVersion = shNode.GetProperty("roblox_version").GetString();
                bool shUpdated = shNode.GetProperty("updated").GetBoolean();

                logCallback($"SirHurt Supports: {shRobloxVersion}");

                if (!shUpdated || shRobloxVersion != robloxLive)
                {
                    logCallback("WARNING: SirHurt is currently UNPATCHED for the live Roblox version!");
                }
                else
                {
                    logCallback("SirHurt and Roblox are synced.");
                }

                string versionFile = Path.Combine(CoreDir, "version.txt");
                string shdllPath = Path.Combine(SirHurtDatDir, "shdllname.dat");
                
                string targetDllName = "sirhurt.dll";
                if (File.Exists(shdllPath))
                {
                    string readName = (await File.ReadAllTextAsync(shdllPath)).Trim();
                    if (!string.IsNullOrEmpty(readName) && File.Exists(Path.Combine(CoreDir, readName)))
                    {
                        targetDllName = readName;
                    }
                }

                if (!forceRedownload && File.Exists(versionFile) && File.Exists(CoreExePath) && File.Exists(Path.Combine(CoreDir, targetDllName)))
                {
                    string localVersion = (await File.ReadAllTextAsync(versionFile)).Trim();
                    if (localVersion == shExploitVersion)
                    {
                        logCallback("SirHurt files match local cache. Skipping heavy download.");
                        return true;
                    }
                }

                logCallback("Downloading SirHurt Core archive (this may take a moment)...");
                string hexResponse = await client.GetStringAsync("https://sirhurt.net/asshurt/update/v5/ProtectFile.php?customversion=LIVE&file=sirhurt.zip");
                byte[] zipBytes = DecodeSirHurtZip(hexResponse);

                string tempZipFile = Path.Combine(CoreDir, "sirhurt_archive.zip");
                await File.WriteAllBytesAsync(tempZipFile, zipBytes);

                logCallback("Extracting core executables...");
                using (ZipArchive archive = ZipFile.OpenRead(tempZipFile))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string destinationPath = Path.Combine(CoreDir, entry.FullName);
                        if (string.IsNullOrEmpty(entry.Name)) 
                        {
                            Directory.CreateDirectory(destinationPath);
                        }
                        else
                        {
                            entry.ExtractToFile(destinationPath, true);
                        }
                    }
                }
                
                File.Delete(tempZipFile);

                logCallback("Mapping payload DLL...");
                
                byte[] dllBytes = null;
                string lastError = "";
                
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        string dllUrlResponse = await client.GetStringAsync("https://sirhurt.net/asshurt/update/v5/fetch_version.php?customversion=LIVE");
                        string dllUrl = dllUrlResponse.Trim();
                        if (string.IsNullOrEmpty(dllUrl)) throw new Exception("DLL URL empty.");
                        
                        dllBytes = await client.GetByteArrayAsync(dllUrl);
                        if (dllBytes.Length < 1024) throw new Exception("DLL too small.");
                        
                        break; // Success
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                        if (attempt < 3)
                        {
                            logCallback($"DLL download attempt {attempt} failed, retrying...");
                            await Task.Delay(2000);
                        }
                    }
                }

                if (dllBytes != null)
                {
                    await File.WriteAllBytesAsync(Path.Combine(CoreDir, "sirhurt.dll"), dllBytes);
                    
                    logCallback("Saving version cache...");
                    await File.WriteAllTextAsync(versionFile, shExploitVersion);
                    
                    logCallback("SirHurt updated successfully!");
                    return true;
                }
                else
                {
                    throw new Exception($"Failed to download DLL after 3 attempts: {lastError}");
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to download SirHurt core:\n{ex.Message}", "API Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                Debug.WriteLine($"Failed to download SirHurt core: {ex.Message}");
                logCallback($"API Check Failed: {ex.Message}");
            }
            return false;
        }

        private static byte[] DecodeSirHurtZip(string hexText)
        {
            string cleanHex = hexText.Trim();
            char[] arr = cleanHex.ToCharArray();
            Array.Reverse(arr);
            string reversed = new string(arr);

            if (reversed.Length % 2 != 0)
                throw new Exception("Invalid hex data format.");

            byte[] bytes = new byte[reversed.Length / 2];
            for (int i = 0; i < reversed.Length; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(reversed.Substring(i, 2), 16);
            }
            return bytes;
        }

        public static bool IsInjected { get; set; } = false;

        public static bool IsRobloxRunning()
        {
            return Process.GetProcessesByName("RobloxPlayerBeta").Length > 0;
        }

        public static async Task<bool> InjectAsync()
        {
            if (!File.Exists(CoreExePath))
                throw new FileNotFoundException("Injector and DLL files are missing. Note: This will be handled by the Bootstrapper prior to UI launch.");

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = CoreExePath,
                WorkingDirectory = CoreDir,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            var proc = Process.Start(psi);
            if (proc == null) return false;

            try
            {
                using var cts = new System.Threading.CancellationTokenSource(3000);
                await proc.WaitForExitAsync(cts.Token);
                return proc.ExitCode == 0;
            }
            catch (TaskCanceledException)
            {
                return true;
            }
        }

        public static void Execute(string script)
        {
            if (string.IsNullOrWhiteSpace(script)) 
                return;

            string path = Path.Combine(SirHurtDatDir, "sirhurt.dat");
            File.WriteAllText(path, script);
        }
    }
}
