using System;
using System.IO;
using System.Threading.Tasks;
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

            // Initial apply
            this.Topmost = SettingsManager.Config.TopMost;
        }

        private void LoadSettingsIntoUI()
        {
            ChkAutoAttach.IsChecked   = SettingsManager.Config.AutoAttach;
            ChkTopMost.IsChecked      = SettingsManager.Config.TopMost;
            ChkFadeEffects.IsChecked  = SettingsManager.Config.FadeEffects;
            ChkMonacoEditor.IsChecked = SettingsManager.Config.MonacoEditor;

            Version appVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 1, 0);
            TxtAppVersion.Text    = $"MossadStudio version: v{appVersion.ToString(3)}";
            TxtSirHurtVersion.Text = $"SirHurt version: {SirHurtAPI.shExploitVersion}";
            TxtRobloxVersion.Text  = $"Roblox Client version: {SirHurtAPI.RobloxLiveVersion}";
        }

        private async void SettingChanged(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            bool prevMonaco = SettingsManager.Config.MonacoEditor;

            SettingsManager.Config.AutoAttach   = ChkAutoAttach.IsChecked   ?? false;
            SettingsManager.Config.TopMost      = ChkTopMost.IsChecked      ?? false;
            SettingsManager.Config.FadeEffects  = ChkFadeEffects.IsChecked  ?? false;
            SettingsManager.Config.MonacoEditor = ChkMonacoEditor.IsChecked ?? false;

            SettingsManager.Save();

            // Apply top-most immediately
            _parent.Topmost = SettingsManager.Config.TopMost;
            this.Topmost    = SettingsManager.Config.TopMost;

            // If Monaco mode was toggled, rebuild all open editor tabs
            if (sender == ChkMonacoEditor && prevMonaco != SettingsManager.Config.MonacoEditor)
            {
                ChkMonacoEditor.IsEnabled = false;  // Prevent double-click during switch
                try
                {
                    await _parent.SwitchEditorModeAsync(SettingsManager.Config.MonacoEditor);
                }
                finally
                {
                    ChkMonacoEditor.IsEnabled = true;
                }
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

        // Load Custom .d.luau
        private async void BtnLoadDluau_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title  = "Load Custom .d.luau Type Definitions",
                Filter = "Luau Declaration Files (*.d.luau;*.luau)|*.d.luau;*.luau|All Files (*.*)|*.*"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var schema = DluauParser.ParseFile(dlg.FileName);
                await _parent.ApplyDluauCompletionsAsync(schema);

                MessageBox.Show(
                    $"Loaded {schema.Types.Count + schema.Globals.Count} declarations from:\n{Path.GetFileName(dlg.FileName)}\n\n" +
                    "Monaco Editor: completions injected.\n" +
                    "AvalonEdit: identifiers highlighted in light-blue.",
                    "Custom .d.luau Loaded",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load .d.luau file:\n{ex.Message}",
                    "Load Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // Action buttons
        private async void BtnCleaner_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will aggressively wipe all SirHurt storage and Roblox configuration temp folders. Are you sure you want to proceed?",
                "Confirm Cleaner", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
                await CleanerService.RunCleanupAsync(_parent);
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Force a complete re-download of SirHurt core files? The studio will freeze while downloading.",
                "Confirm Download", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                await SirHurtAPI.DownloadCoreAsync(msg =>
                {
                    Dispatcher.Invoke(() => _parent.LogBootstrapperAction(msg, "Manual Install"));
                }, forceRedownload: true);
                MessageBox.Show("Redownload completed. You may now inject.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
