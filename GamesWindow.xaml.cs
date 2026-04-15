using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MossadStudio.Services;

namespace MossadStudio
{
    public class RScriptEntry
    {
        public string? _id { get; set; }
        public string? title { get; set; }
        public string? description { get; set; }
        public string? rawScript { get; set; }
        
        private string? _image;
        public string? image 
        { 
            get => _image; 
            set 
            {
                // Explicitly strip WebP encodings from Roblox endpoints; WPF native decoders silently fail on WebP arrays.
                _image = value?.Replace("/Image/Webp", "/Image/Jpeg")?.Replace("Image/Png", "Image/Jpeg");
            }
        }
    }

    public class RScriptInfo
    {
        public int currentPage { get; set; }
        public int maxPages { get; set; }
    }

    public class RScriptResponse
    {
        public RScriptInfo? info { get; set; }
        public List<RScriptEntry>? scripts { get; set; }
    }

    public partial class GamesWindow : Window
    {
        private static readonly HttpClient client = new HttpClient();
        private int _currentPage = 1;
        private MainWindow _main;

        public GamesWindow(MainWindow parent)
        {
            InitializeComponent();
            _main = parent;
            _ = LoadScriptsAsync("https://rscripts.net/api/v2/scripts?page=1");
        }

        private async Task LoadScriptsAsync(string url)
        {
            try
            {
                StatusOverlay.Visibility = Visibility.Visible;
                StatusText.Text = "Loading scripts...";

                // Important: Need to bypass cloudflare or use the correct API endpoint natively
                // Note: The docs state `https://rscripts.net/api/v2/scripts`
                string response = await client.GetStringAsync(url);
                var data = JsonSerializer.Deserialize<RScriptResponse>(response);

                if (data?.scripts != null)
                {
                    ScriptsList.ItemsSource = data.scripts;
                    StatusOverlay.Visibility = Visibility.Collapsed;
                }
                else
                {
                    StatusText.Text = "No scripts found.";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Failed to load API.\n" + ex.Message;
            }
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            string query = TxtSearch.Text.Trim();
            if (string.IsNullOrEmpty(query)) return;

            string url = $"https://rscripts.net/api/v2/scripts?q={Uri.EscapeDataString(query)}";
            _ = LoadScriptsAsync(url);
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtSearchPlaceholder != null)
                TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void BtnExecuteScript_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string scriptUrl && !string.IsNullOrEmpty(scriptUrl))
            {
                if (!SirHurtAPI.IsInjected)
                {
                    MessageBox.Show("Please INJECT SirHurt before executing.", "Mossad Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                btn.IsEnabled = false;
                btn.Content = "...";

                try
                {
                    string rawCode = await client.GetStringAsync(scriptUrl);
                    SirHurtAPI.Execute(rawCode);
                    btn.Content = "SENT";
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to download raw script:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    btn.Content = "EXECUTE";
                }

                await Task.Delay(2000); // Visual reset delay
                if (btn.Content.ToString() == "SENT")
                {
                    btn.Content = "EXECUTE";
                    btn.IsEnabled = true;
                }
            }
        }

        private async void BtnOpenScriptTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is RScriptEntry entry && !string.IsNullOrEmpty(entry.rawScript))
            {
                btn.IsEnabled = false;
                btn.Content = "FETCHING";

                try
                {
                    string rawCode = await client.GetStringAsync(entry.rawScript);
                    _main.AddScriptTab(entry.title ?? "New Script", rawCode);
                    btn.Content = "OPENED";
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to download script:\n" + ex.Message, "Mossad Studio", MessageBoxButton.OK, MessageBoxImage.Error);
                    btn.Content = "OPEN TAB";
                }

                await Task.Delay(1000);
                if (btn.Content.ToString() == "OPENED") 
                {
                    btn.Content = "OPEN TAB";
                    btn.IsEnabled = true;
                }
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
