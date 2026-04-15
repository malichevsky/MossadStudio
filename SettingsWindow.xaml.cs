using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MossadStudio.Services;

namespace MossadStudio
{
    public partial class SettingsWindow : Window
    {
        private MainWindow _parent;
        private bool _isLoaded = false;

        public SettingsWindow(MainWindow parent)
        {
            InitializeComponent();
            _parent = parent;

            LoadSettingsIntoUI();
            _isLoaded = true;
            
            // Initial Apply
            this.Topmost = SettingsManager.Config.TopMost;
        }

        private void LoadSettingsIntoUI()
        {
            ChkAutoAttach.IsChecked = SettingsManager.Config.AutoAttach;
            ChkTopMost.IsChecked = SettingsManager.Config.TopMost;
            ChkFadeEffects.IsChecked = SettingsManager.Config.FadeEffects;
            ChkAutoExecute.IsChecked = SettingsManager.Config.AutoExecute;
            ChkDiscordRPC.IsChecked = SettingsManager.Config.DiscordRPC;
            ChkFpsUnlocker.IsChecked = SettingsManager.Config.FpsUnlocker;

            Version appVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            TxtAppVersion.Text = $"MossadStudio version: v{appVersion.ToString(3)}";
            TxtSirHurtVersion.Text = $"SirHurt version: {SirHurtAPI.shExploitVersion}";
            TxtRobloxVersion.Text = $"Roblox Client version: {SirHurtAPI.RobloxLiveVersion}";
        }

        private void SettingChanged(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            SettingsManager.Config.AutoAttach = ChkAutoAttach.IsChecked ?? false;
            SettingsManager.Config.TopMost = ChkTopMost.IsChecked ?? false;
            SettingsManager.Config.FadeEffects = ChkFadeEffects.IsChecked ?? false;
            SettingsManager.Config.AutoExecute = ChkAutoExecute.IsChecked ?? false;
            SettingsManager.Config.DiscordRPC = ChkDiscordRPC.IsChecked ?? false;
            SettingsManager.Config.FpsUnlocker = ChkFpsUnlocker.IsChecked ?? false;

            SettingsManager.Save();

            // Apply TopMost immediately
            _parent.Topmost = SettingsManager.Config.TopMost;
            this.Topmost = SettingsManager.Config.TopMost;

            if (sender == ChkDiscordRPC)
            {
                DiscordRpcManager.UpdateState();
            }
            else if (sender == ChkFpsUnlocker)
            {
                FPSUnlocker.Apply();
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Hide(); 
        }

        private async void BtnCleaner_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will aggressively wipe all SirHurt storage and Roblox configuration temp folders. Are you sure you want to proceed?", 
                "Confirm Cleaner", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                
            if (result == MessageBoxResult.Yes)
            {
                await CleanerService.RunCleanupAsync(_parent);
            }
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Force a complete re-download of SirHurt core files? The studio will freeze while downloading.", "Confirm Download", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (result == MessageBoxResult.Yes)
            {
                await SirHurtAPI.DownloadCoreAsync(msg => 
                {
                    Dispatcher.Invoke(() => _parent.LogBootstrapperAction(msg, "Manual Install"));
                }, forceRedownload: true);
                MessageBox.Show("Redownload completed. You may now inject.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
