using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using MossadStudio.Services;

namespace MossadStudio
{
    public partial class Bootstrapper : Window
    {
        private DispatcherTimer _timer;
        private Stopwatch _stopwatch;

        public Bootstrapper()
        {
            InitializeComponent();
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _stopwatch = new Stopwatch();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            TxtElapsed.Text = $"Elapsed Time: {_stopwatch.Elapsed.Minutes:D2}:{_stopwatch.Elapsed.Seconds:D2}";
        }

        private void Log(string msg)
        {
            TxtStatus.Text = msg;
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Bootstrapper_log.txt"), $"[Bootstrapper] {DateTime.Now:HH:mm:ss} - {msg}\n"); } catch {}
        }

        private async Task<bool> CheckForUpdatesAsync()
        {
            if (SettingsManager.Config.HiddenFlags.SkipStudioUpdateCheck)
            {
                Log("[Hidden Flag] SkipStudioUpdateCheck active — GitHub update check skipped.");
                return true;
            }

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "MossadStudioUpdater");
                string url = "https://api.github.com/repos/malichevsky/MossadStudio/releases/latest";
                using var resp = await client.GetAsync(url);
                string json = await resp.Content.ReadAsStringAsync();

                if (SettingsManager.Config.HiddenFlags.VerboseLogging)
                {
                    Log($"[Verbose] HTTP {(int)resp.StatusCode} ({json.Length} chars) for {url}");
                }

                using JsonDocument doc = JsonDocument.Parse(json);
                string? tagName = doc.RootElement.GetProperty("tag_name").GetString();
                
                Version appVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 1, 2);
                string currentTag = $"v{appVersion.ToString(3)}";
                
                Log($"Current Version: {currentTag} | Latest Release: {tagName}");
                
                if (!string.IsNullOrEmpty(tagName) && tagName != currentTag)
                {
                    Log($"New Studio Update Detected: {tagName}");
                    var result = MessageBox.Show($"A new version of Mossad Studio ({tagName}) is available.\nWould you like to install it now?", "Mossad Studio Update", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        Log("Downloading new executable...");
                        string downloadUrl = "";
                        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
                        {
                            if (asset.GetProperty("name").GetString() == "MossadStudio.exe")
                            {
                                downloadUrl = asset.GetProperty("browser_download_url").GetString();
                                break;
                            }
                        }
                        
                        if (!string.IsNullOrEmpty(downloadUrl))
                        {
                            string newExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MossadStudio_New.exe");
                            byte[] exeData = await client.GetByteArrayAsync(downloadUrl);
                            await File.WriteAllBytesAsync(newExePath, exeData);
                            
                            string currentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                            string batPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update.bat");
                            
                            string batContext = $@"@echo off
timeout /t 2 /nobreak >nul
del ""{currentExePath}""
move /Y ""{newExePath}"" ""{currentExePath}""
start """" ""{currentExePath}""
del ""%~f0""
";
                            await File.WriteAllTextAsync(batPath, batContext);
                            
                            ProcessStartInfo psi = new ProcessStartInfo { FileName = batPath, CreateNoWindow = true, UseShellExecute = false };
                            Process.Start(psi);
                            Environment.Exit(0);
                            return false;
                        }
                        else
                        {
                            Log("ERROR: Update asset 'MossadStudio.exe' not found on GitHub.");
                            Log("Please ensure the maintainer has uploaded the executable asset.");
                            await Task.Delay(3000);
                        }
                    }
                }
                Log("Applying local Studio caching rules...");
            }
            catch (Exception ex)
            {
                Log($"Update check failed: {ex.Message}");
            }
            return true;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _stopwatch.Start();
            _timer.Start();

            // Load Configuration
            SettingsManager.Load();

            // Clear old logs
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Bootstrapper_log.txt"), ""); } catch {}

            // Hidden flags: combo check + general warning
            if (!HiddenFlagsWarning.CheckAndPrompt())
            {
                Application.Current.Shutdown();
                return;
            }

            // SkipBootstrapper: jump straight to MainWindow
            if (SettingsManager.Config.HiddenFlags.SkipBootstrapper)
            {
                Log("[Hidden Flag] SkipBootstrapper active — bypassing core download and all checks.");
                Log("WARNING: SirHurt files have NOT been verified. Injection may fail.");
                await Task.Delay(500);

                MainWindow mainWindow = new MainWindow();
                mainWindow.Show();
                this.Close();
                return;
            }

            Log("Checking if Mossad Studio is up-to-date...");
            bool shouldContinue = await CheckForUpdatesAsync();
            if (!shouldContinue) return;
            
            // Minimal artificial delay for polish
            await Task.Delay(500);

            // Execute the API download logic (honour ForceRedownload flag)
            bool forceRedownload = SettingsManager.Config.HiddenFlags.ForceRedownload;
            if (forceRedownload)
                Log("[Hidden Flag] ForceRedownload active — ignoring version cache.");

            bool success = await SirHurtAPI.DownloadCoreAsync(msg => 
            {
                Dispatcher.Invoke(() => Log(msg));
            }, forceRedownload: forceRedownload);

            _stopwatch.Stop();
            _timer.Stop();

            if (success)
            {
                Log("Starting Mossad Studio...");
                await Task.Delay(500);

                MainWindow mainWindow = new MainWindow();
                mainWindow.Show();
                this.Close();
            }
            else
            {
                Log("Installation aborted due to error.");
                MessageBox.Show("Bootstrapper encountered an error and cannot proceed. Please verify your connection or check your antivirus.", "Bootstrapper Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
