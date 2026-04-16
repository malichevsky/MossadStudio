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
            bool suspectedBlock = false;
            try
            {
                bool verbose = SettingsManager.Config.HiddenFlags.VerboseLogging;

                // SkipSirHurtUpdateCheck: bypass API checks, use placeholder versions, nuke cache
                if (SettingsManager.Config.HiddenFlags.SkipSirHurtUpdateCheck)
                {
                    logCallback("[Hidden Flag] SkipSirHurtUpdateCheck active — skipping SirHurt/Roblox version checks.");
                    shExploitVersion = "N/A (skipped)";
                    RobloxLiveVersion = "N/A (skipped)";

                    // Delete version.txt so the next normal launch re-validates everything
                    string versionFilePath = Path.Combine(CoreDir, "version.txt");
                    if (File.Exists(versionFilePath))
                    {
                        try { File.Delete(versionFilePath); } catch { }
                        logCallback("[Hidden Flag] version.txt deleted — cache will be rebuilt on next normal launch.");
                    }

                    // Still ensure core files exist (but don't re-download unless forced)
                    if (!forceRedownload && File.Exists(CoreExePath))
                    {
                        logCallback("Core files present. Skipping download.");
                        return true;
                    }
                    // Fall through to download if files are missing or forceRedownload
                }

                if (!SettingsManager.Config.HiddenFlags.SkipSirHurtUpdateCheck)
                {
                    logCallback("Fetching LIVE Roblox version hash...");
                    string rbUrl = "https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer";
                    using var rbResp = await client.GetAsync(rbUrl);
                    string rbText = await rbResp.Content.ReadAsStringAsync();
                    
                    if (verbose) logCallback($"[Verbose] HTTP {(int)rbResp.StatusCode} ({rbText.Length} chars) for {rbUrl}");
                    if (!rbResp.IsSuccessStatusCode) suspectedBlock |= DetectAndLogBlock(rbResp, logCallback);

                    using JsonDocument rbDoc = JsonDocument.Parse(rbText);
                    string robloxLive = rbDoc.RootElement.GetProperty("clientVersionUpload").GetString();
                    RobloxLiveVersion = robloxLive ?? "Unknown";
                    logCallback($"Live Roblox Version: {robloxLive}");

                    logCallback("Checking SirHurt API status...");
                    string shUrl = "https://sirhurt.net/status/fetch.php?exploit=SirHurt%20V5";
                    using var shResp = await client.GetAsync(shUrl);
                    string shText = await shResp.Content.ReadAsStringAsync();

                    if (verbose) logCallback($"[Verbose] HTTP {(int)shResp.StatusCode} ({shText.Length} chars) for {shUrl}");
                    if (!shResp.IsSuccessStatusCode) suspectedBlock |= DetectAndLogBlock(shResp, logCallback);

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
                        if (verbose) logCallback($"[Verbose] Resolved DLL name from shdllname.dat: {targetDllName}");
                    }
                }

                if (!forceRedownload && File.Exists(versionFile) && File.Exists(CoreExePath) && File.Exists(Path.Combine(CoreDir, targetDllName)))
                {
                    string localVersion = (await File.ReadAllTextAsync(versionFile)).Trim();
                    if (verbose) logCallback($"[Verbose] Local cached version: {localVersion} | Remote: {shExploitVersion}");
                    if (localVersion == shExploitVersion)
                    {
                        logCallback("SirHurt files match local cache. Skipping heavy download.");
                        return true;
                    }
                }

                logCallback("Downloading SirHurt Core archive (this may take a moment)...");
                string archiveUrl = "https://sirhurt.net/asshurt/update/v5/ProtectFile.php?customversion=LIVE&file=sirhurt.zip";
                using var archResp = await client.GetAsync(archiveUrl);
                string hexResponse = await archResp.Content.ReadAsStringAsync();
                
                if (verbose) logCallback($"[Verbose] HTTP {(int)archResp.StatusCode} ({hexResponse.Length} chars) for {archiveUrl}");
                if (!archResp.IsSuccessStatusCode) suspectedBlock |= DetectAndLogBlock(archResp, logCallback);

                byte[] zipBytes = DecodeSirHurtZip(hexResponse);
                if (verbose) logCallback($"[Verbose] Decoded ZIP size: {zipBytes.Length / 1024} KB");

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
                        string verUrl = "https://sirhurt.net/asshurt/update/v5/fetch_version.php?customversion=LIVE";
                        using var verResp = await client.GetAsync(verUrl);
                        string dllUrl = (await verResp.Content.ReadAsStringAsync()).Trim();

                        if (verbose) logCallback($"[Verbose] HTTP {(int)verResp.StatusCode} ({dllUrl.Length} chars) for {verUrl}");
                        if (!verResp.IsSuccessStatusCode) suspectedBlock |= DetectAndLogBlock(verResp, logCallback);

                        if (string.IsNullOrEmpty(dllUrl)) throw new Exception("DLL URL empty.");
                        
                        using var dllResp = await client.GetAsync(dllUrl);
                        dllBytes = await dllResp.Content.ReadAsByteArrayAsync();

                        if (verbose) logCallback($"[Verbose] HTTP {(int)dllResp.StatusCode} ({dllBytes.Length} bytes) for {dllUrl}");
                        if (!dllResp.IsSuccessStatusCode) suspectedBlock |= DetectAndLogBlock(dllResp, logCallback);

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
                string errorMessage = ex.Message;
                if (ex is HttpRequestException || ex is TaskCanceledException || suspectedBlock)
                {
                    logCallback("NETWORK ERROR: Connection failed, timed out, or block detected.");
                    logCallback("ADVICE: If you are on a restricted network (University/Work), Roblox may be blocked.");
                    logCallback("ADVICE: Try using a VPN or Cloudflare WARP to bypass network restrictions.");
                    
                    if (!errorMessage.Contains("ADVICE"))
                    {
                        errorMessage += "\n\nADVICE: If you are on a restricted network (University/Work), Roblox may be blocked. Try using a VPN or Cloudflare WARP.";
                    }
                }

                System.Windows.MessageBox.Show($"Failed to download SirHurt core:\n{errorMessage}", "API Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                Debug.WriteLine($"Failed to download SirHurt core: {ex.Message}");
                logCallback($"API Check Failed: {ex.Message}");
            }
            return false;
        }

        private static bool DetectAndLogBlock(HttpResponseMessage response, Action<string> logCallback)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden || 
                response.StatusCode == System.Net.HttpStatusCode.ProxyAuthenticationRequired)
            {
                logCallback("SUSPECTED BLOCK: The server returned a 403 Forbidden or Proxy error.");
                logCallback("ADVICE: This network likely blocks Roblox/SirHurt. Please use a VPN or Cloudflare WARP.");
                return true;
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
