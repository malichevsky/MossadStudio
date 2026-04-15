using System;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using MossadStudio.Services;

namespace MossadStudio;

public partial class MainWindow : Window
{
    private IHighlightingDefinition _luauHighlighting;
    private TextEditor _outputEditor;
    private int _tabCounter = 1;
    private FileSystemWatcher _sirHurtWatcher;
    private long _lastLogPosition = 0;
    private DispatcherTimer _robloxProcessTimer;
    private SettingsWindow _settingsWindow;

    public MainWindow()
    {
        InitializeComponent();
        
        this.Activated += Window_Activated;
        this.Deactivated += Window_Deactivated;
        
        LoadSyntax();
        InitializeTabs();
        LoadScripts();
        SetupLogTailer();
        SetupProcessChecker();
    }

    private void LoadSyntax()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            // The embedded resource name includes the default namespace
            string resourceName = "MossadStudio.Luau.xshd";
            
            using (var s = assembly.GetManifestResourceStream(resourceName))
            {
                if (s != null)
                {
                    using (var reader = new XmlTextReader(s))
                    {
                        _luauHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                    }
                }
            }
        } catch {}
    }

    private void InitializeTabs()
    {
        // 1. Output Tab
        _outputEditor = CreateEditor(true);
        var outputTab = CreateTabItem("Output", _outputEditor, false);
        EditorTabs.Items.Add(outputTab);

        // 2. Initial Script Tab
        AddScriptTab($"Script{_tabCounter++}.lua");

        EditorTabs.SelectedIndex = 1; // Select Script1.lua
    }

    private TextEditor CreateEditor(bool readOnly)
    {
        var editor = new TextEditor
        {
            ShowLineNumbers = true,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#cccccc")),
            Background = Brushes.Transparent,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            BorderThickness = new Thickness(0),
            LineNumbersForeground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
            SyntaxHighlighting = _luauHighlighting,
            IsReadOnly = readOnly,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        if (readOnly)
        {
            editor.ShowLineNumbers = false;
            editor.SyntaxHighlighting = null;
            editor.Foreground = Brushes.White;
        }
        return editor;
    }

    private TabItem CreateTabItem(string title, TextEditor editor, bool closable = true)
    {
        var tab = new TabItem { Content = editor };
        
        var headerPanel = new Grid { Width = 90 }; // ~100px total width with padding
        headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        
        var titleText = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, TextTrimming = TextTrimming.CharacterEllipsis };
        headerPanel.Children.Add(titleText);

        if (closable)
        {
            headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var closeText = new TextBlock { Text = "X", Foreground = Brushes.Gray, FontSize = 10, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            closeText.MouseEnter += (s, e) => closeText.Foreground = Brushes.White;
            closeText.MouseLeave += (s, e) => closeText.Foreground = Brushes.Gray;
            closeText.PreviewMouseLeftButtonDown += (s, e) => 
            {
                e.Handled = true;
                EditorTabs.Items.Remove(tab);
            };
            Grid.SetColumn(closeText, 1);
            headerPanel.Children.Add(closeText);
        }

        tab.Header = headerPanel;
        return tab;
    }

    public void AddScriptTab(string title, string content = "")
    {
        var editor = CreateEditor(false);
        editor.Text = content;
        var newTab = CreateTabItem(title, editor, true);
        
        EditorTabs.Items.Add(newTab);
        EditorTabs.SelectedItem = newTab;
    }

    private void AddTabButton_Click(object sender, MouseButtonEventArgs e)
    {
        AddScriptTab($"Script{_tabCounter++}.lua");
    }

    private void SetupProcessChecker()
    {
        _robloxProcessTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _robloxProcessTimer.Tick += (s, e) => 
        {
            if (!SirHurtAPI.IsRobloxRunning())
            {
                if (SirHurtAPI.IsInjected || BtnInject.Content.ToString() == "INJECTED")
                {
                    SirHurtAPI.IsInjected = false;
                    BtnInject.Content = "INJECT";
                    BtnInject.IsEnabled = true;
                }
            }
        };
        _robloxProcessTimer.Start();
    }

    private void SetupLogTailer()
    {
        string bsLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Bootstrapper_log.txt");
        if (File.Exists(bsLog))
        {
            _outputEditor.AppendText(File.ReadAllText(bsLog));
        }

        string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "sirhurt", "sirhui", "sirh_debug_log.dat");
        if (File.Exists(logPath))
        {
             using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
             {
                 _lastLogPosition = fs.Length;
             }
        }
        else 
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            File.WriteAllText(logPath, "");
        }

        _sirHurtWatcher = new FileSystemWatcher
        {
            Path = Path.GetDirectoryName(logPath),
            Filter = Path.GetFileName(logPath),
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _sirHurtWatcher.Changed += OnLogChanged;
        _sirHurtWatcher.EnableRaisingEvents = true;
    }

    private void OnLogChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            using (var fs = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs))
            {
                if (fs.Length < _lastLogPosition)
                    _lastLogPosition = 0; // File was cleared

                fs.Seek(_lastLogPosition, SeekOrigin.Begin);
                string newContent = reader.ReadToEnd();
                _lastLogPosition = fs.Length;

                if (!string.IsNullOrEmpty(newContent))
                {
                    Dispatcher.Invoke(() => {
                        var lines = newContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var l in lines) _outputEditor.AppendText($"[SirHurt] {l}\n");
                        _outputEditor.ScrollToEnd();
                    });
                }
            }
        } catch { } // Catch ReadWrite locked race condition
    }

    private void LoadScripts()
    {
        ScriptsTreeView.Items.Clear();
        
        // Setup empty-space ContextMenu for the TreeView wrapper
        if (ScriptsTreeView.ContextMenu == null)
        {
            ScriptsTreeView.ContextMenu = new ContextMenu();
            var genericRefresh = new MenuItem { Header = "Refresh List" };
            genericRefresh.Click += (s, e) => LoadScripts();
            ScriptsTreeView.ContextMenu.Items.Add(genericRefresh);
        }

        if (Directory.Exists(SirHurtAPI.ScriptsDir))
        {
            PopulateTreeView(SirHurtAPI.ScriptsDir, ScriptsTreeView.Items);
        }
    }

    private ContextMenu CreateScriptContextMenu(string targetPath, bool isFile)
    {
        var menu = new ContextMenu();
        
        var openItem = new MenuItem { Header = isFile ? "Open Script" : "Open Folder" };
        openItem.Click += (s, e) => {
            if (isFile) AddScriptTab(Path.GetFileName(targetPath), File.ReadAllText(targetPath));
            else Process.Start("explorer.exe", targetPath);
            e.Handled = true;
        };
        menu.Items.Add(openItem);

        if (isFile)
        {
            var executeItem = new MenuItem { Header = "Execute File" };
            executeItem.Click += (s, e) => {
                if (SirHurtAPI.IsInjected) SirHurtAPI.Execute(File.ReadAllText(targetPath));
                e.Handled = true;
            };
            menu.Items.Add(executeItem);
        }

        var renameItem = new MenuItem { Header = isFile ? "Rename File" : "Rename Folder" };
        renameItem.Click += (s, e) => {
            var prompt = new PromptWindow("Rename", $"Enter new name for {(isFile ? "file" : "folder")}:", Path.GetFileName(targetPath)) { Owner = this };
            if (prompt.ShowDialog() == true && !string.IsNullOrWhiteSpace(prompt.ResponseText))
            {
                try
                {
                    string newPath = Path.Combine(Path.GetDirectoryName(targetPath)!, prompt.ResponseText);
                    if (isFile) File.Move(targetPath, newPath);
                    else Directory.Move(targetPath, newPath);
                    LoadScripts();
                }
                catch (Exception ex) { MessageBox.Show("Failed to rename target: " + ex.Message, "System Error", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }
            e.Handled = true;
        };
        menu.Items.Add(renameItem);

        var deleteItem = new MenuItem { Header = isFile ? "Delete File" : "Delete Folder" };
        deleteItem.Click += (s, e) => {
            if (MessageBox.Show($"Are you sure you want to permanently delete the {(isFile ? "file" : "folder")} '{Path.GetFileName(targetPath)}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    if (isFile) File.Delete(targetPath);
                    else Directory.Delete(targetPath, true);
                    LoadScripts();
                }
                catch (Exception ex) { MessageBox.Show("Failed to delete target: " + ex.Message, "System Error", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }
            e.Handled = true;
        };
        menu.Items.Add(deleteItem);

        var sep = new Separator();
        sep.Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#333333");
        menu.Items.Add(sep);
        
        var refreshItem = new MenuItem { Header = "Refresh List" };
        refreshItem.Click += (s, e) => { LoadScripts(); e.Handled = true; };
        menu.Items.Add(refreshItem);
        
        return menu;
    }

    private void PopulateTreeView(string directoryPath, ItemCollection parentItems)
    {
        foreach (var dir in Directory.GetDirectories(directoryPath))
        {
            var dirInfo = new DirectoryInfo(dir);
            var item = new TreeViewItem { Header = dirInfo.Name, Tag = dir, Foreground = Brushes.LightGray };
            item.ContextMenu = CreateScriptContextMenu(dir, false);
            PopulateTreeView(dir, item.Items);
            parentItems.Add(item);
        }

        foreach (var file in Directory.GetFiles(directoryPath))
        {
            var fileInfo = new FileInfo(file);
            if (fileInfo.Extension.ToLower() == ".lua" || fileInfo.Extension.ToLower() == ".luau" || fileInfo.Extension.ToLower() == ".txt")
            {
                var item = new TreeViewItem { Header = fileInfo.Name, Tag = file, Foreground = Brushes.LightGray };
                item.ContextMenu = CreateScriptContextMenu(file, true);
                parentItems.Add(item);
            }
        }
    }

    private void ScriptsTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ScriptsTreeView.SelectedItem is TreeViewItem selectedItem && selectedItem.Tag is string path)
        {
            if (File.Exists(path))
            {
                AddScriptTab(Path.GetFileName(path), File.ReadAllText(path));
            }
        }
    }

    public TextEditor GetActiveEditor()
    {
        if (EditorTabs.SelectedItem is TabItem tab && tab.Content is TextEditor editor)
            return editor;
        return null;
    }

    private void BtnExecute_Click(object sender, RoutedEventArgs e)
    {
        if (!SirHurtAPI.IsRobloxRunning())
        {
            SirHurtAPI.IsInjected = false;
            if (BtnInject.Content.ToString() == "INJECTED")
            {
                BtnInject.Content = "INJECT";
                BtnInject.IsEnabled = true;
            }
            MessageBox.Show("RobloxPlayerBeta.exe is not found and is not likely injected.", "Mossad Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!SirHurtAPI.IsInjected)
        {
            MessageBox.Show("SirHurt is not injected! To execute a script, please press the INJECT button first.", "Mossad Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var editor = GetActiveEditor();
        if (editor != null && !editor.IsReadOnly)
             SirHurtAPI.Execute(editor.Text);
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        var editor = GetActiveEditor();
        if (editor != null && !editor.IsReadOnly)
             editor.Text = string.Empty;
    }

    private void BtnOpenFile_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Lua Scripts (*.lua;*.luau;*.txt)|*.lua;*.luau;*.txt|All files (*.*)|*.*",
            InitialDirectory = Path.GetFullPath(SirHurtAPI.ScriptsDir)
        };
        if (openFileDialog.ShowDialog() == true)
        {
            AddScriptTab(Path.GetFileName(openFileDialog.FileName), File.ReadAllText(openFileDialog.FileName));
        }
    }

    private void BtnSaveFile_Click(object sender, RoutedEventArgs e)
    {
        var editor = GetActiveEditor();
        if (editor != null && !editor.IsReadOnly)
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Lua Scripts (*.lua;*.luau;*.txt)|*.lua;*.luau;*.txt|All files (*.*)|*.*",
                InitialDirectory = Path.GetFullPath(SirHurtAPI.ScriptsDir)
            };
            if (saveFileDialog.ShowDialog() == true)
            {
                File.WriteAllText(saveFileDialog.FileName, editor.Text);
                MessageBox.Show("Script successfully saved!", "Mossad Studio", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private async void BtnInject_Click(object sender, RoutedEventArgs e)
    {
        if (BtnInject.Content.ToString() == "INJECTING" || BtnInject.Content.ToString() == "INJECTED")
            return;

        if (!SirHurtAPI.IsRobloxRunning())
        {
            MessageBox.Show("RobloxPlayerBeta.exe is not present. Please open your Roblox client first.", "Mossad Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BtnInject.Content = "INJECTING";
        BtnInject.IsEnabled = false;

        try
        {
            bool success = await SirHurtAPI.InjectAsync();
            if (success)
            {
                BtnInject.Content = "INJECTED";
                SirHurtAPI.IsInjected = true;

                // Handle Auto-Execute if active
                if (SettingsManager.Config.AutoExecute)
                {
                    await Task.Delay(500); // Give injection a half second to stabilize
                    if (Directory.Exists(SirHurtAPI.AutoexecDir))
                    {
                        foreach (var file in Directory.GetFiles(SirHurtAPI.AutoexecDir))
                        {
                            if (file.EndsWith(".lua") || file.EndsWith(".txt") || file.EndsWith(".luau"))
                            {
                                SirHurtAPI.Execute(File.ReadAllText(file));
                                await Task.Delay(200); // Stagger executions
                            }
                        }
                    }
                }
            }
            else
            {
                BtnInject.Content = "INJECT";
                BtnInject.IsEnabled = true;
            }
        }
        catch (FileNotFoundException ex)
        {
            MessageBox.Show(ex.Message, "Injector Error: Missing Files", MessageBoxButton.OK, MessageBoxImage.Warning);
            BtnInject.Content = "INJECT";
            BtnInject.IsEnabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to inject: {ex.Message}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            BtnInject.Content = "INJECT";
            BtnInject.IsEnabled = true;
        }
    }

    private void BtnOptions_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Owner = this;
            
            // Re-snap to main window movements
            this.LocationChanged += (s, ev) => { SnapSettingsWindow(); };
            this.SizeChanged += (s, ev) => { SnapSettingsWindow(); };
        }

        if (_settingsWindow.Visibility == Visibility.Visible)
        {
            _settingsWindow.Hide();
        }
        else
        {
            SnapSettingsWindow();
            _settingsWindow.Show();
        }
    }

    private void SnapSettingsWindow()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Left = this.Left + this.ActualWidth + 2;
            _settingsWindow.Top = this.Top;
        }
    }

    public void LogCleanerAction(string message)
    {
        Dispatcher.Invoke(() => 
        {
            _outputEditor.AppendText($"[Cleaner] {message}\n");
            _outputEditor.ScrollToEnd();
        });
    }

    public void LogBootstrapperAction(string message, string tag = "Bootstrapper")
    {
        try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Bootstrapper_log.txt"), $"[{tag}] {message}\n"); } catch {}
        Dispatcher.Invoke(() => 
        {
            _outputEditor.AppendText($"[{tag}] {message}\n");
            _outputEditor.ScrollToEnd();
        });
    }

    private void AnimateOpacity(double toValue)
    {
        DoubleAnimation anim = new DoubleAnimation(toValue, TimeSpan.FromMilliseconds(200));
        this.BeginAnimation(Window.OpacityProperty, anim);
    }

    private void Window_Activated(object sender, EventArgs e)
    {
        AnimateOpacity(1.0);
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (SettingsManager.Config.TopMost && SettingsManager.Config.FadeEffects)
        {
            AnimateOpacity(0.4);
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

    // --- Top Menu Context Routes ---

    private void TopMenu_LeftClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.ContextMenu != null)
        {
            element.ContextMenu.PlacementTarget = element;
            element.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            element.ContextMenu.IsOpen = true;
        }
    }

    private void TopMenu_CreditsClick(object sender, MouseButtonEventArgs e)
    {
        var window = new CreditsWindow { Owner = this };
        window.ShowDialog();
    }

    private void TopMenu_GamesClick(object sender, MouseButtonEventArgs e)
    {
        var window = new GamesWindow(this) { Owner = this };
        window.Show();
    }

    private void BtnUndo_Click(object sender, RoutedEventArgs e)
    {
        var editor = GetActiveEditor();
        if (editor != null && !editor.IsReadOnly) editor.Undo();
    }

    private void BtnRedo_Click(object sender, RoutedEventArgs e)
    {
        var editor = GetActiveEditor();
        if (editor != null && !editor.IsReadOnly) editor.Redo();
    }

    private void BtnFindReplace_Click(object sender, RoutedEventArgs e)
    {
        var window = new FindReplaceWindow(this) { Owner = this };
        window.Show();
    }

    private void BtnHotScript_InfiniteYield(object sender, RoutedEventArgs e)
    {
        if (SirHurtAPI.IsInjected) SirHurtAPI.Execute("loadstring(game:HttpGet('https://raw.githubusercontent.com/EdgeIY/infiniteyield/master/source'))()");
    }

    private void BtnHotScript_Dex(object sender, RoutedEventArgs e)
    {
        if (SirHurtAPI.IsInjected) SirHurtAPI.Execute("loadstring(game:HttpGet('https://raw.githubusercontent.com/infyiff/backup/main/dex.lua'))()");
    }

    private void BtnHotScript_OwlHub(object sender, RoutedEventArgs e)
    {
        if (SirHurtAPI.IsInjected) SirHurtAPI.Execute("loadstring(game:HttpGet('https://raw.githubusercontent.com/CriShoux/OwlHub/master/OwlHub.txt'))()");
    }

    private void BtnHotScript_UNC(object sender, RoutedEventArgs e)
    {
        if (SirHurtAPI.IsInjected) SirHurtAPI.Execute("loadstring(game:HttpGet('https://raw.githubusercontent.com/unified-naming-convention/NamingStandard/refs/heads/main/UNCCheckEnv.lua'))()");
    }

    private void BtnHotScript_SimpleSpy(object sender, RoutedEventArgs e)
    {
        if (SirHurtAPI.IsInjected) SirHurtAPI.Execute("loadstring(game:HttpGet('https://raw.githubusercontent.com/infyiff/backup/refs/heads/main/SimpleSpyV3/main.lua'))()");
    }

    private void BtnHotScript_UnnamedESP(object sender, RoutedEventArgs e)
    {
        if (SirHurtAPI.IsInjected) SirHurtAPI.Execute("pcall(function() loadstring(game:HttpGet('https://raw.githubusercontent.com/ic3w0lf22/Unnamed-ESP/master/UnnamedESP.lua'))() end)");
    }

    private void BtnOthers_Dashboard(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://sirhurt.net/login/dashboard.php/") { UseShellExecute = true });
    }

    private void BtnOthers_Discord(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://discord.gg/sirhurt") { UseShellExecute = true });
    }
}