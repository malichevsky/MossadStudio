using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
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
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MossadStudio.Services;

namespace MossadStudio;

public partial class MainWindow : Window
{
    private IHighlightingDefinition? _luauHighlighting;
    private TextEditor _outputEditor = null!;
    private int _tabCounter = 1;
    private FileSystemWatcher _sirHurtWatcher = null!;
    private long _lastLogPosition = 0;
    private DispatcherTimer _robloxProcessTimer = null!;
    private SettingsWindow _settingsWindow = null!;

    // True while we're waiting for SaveTabsAsync() to finish before shutdown
    private bool _isClosing = false;

    private bool _monacoMode => SettingsManager.Config.MonacoEditor;

    public MainWindow()
    {
        InitializeComponent();

        this.Activated   += Window_Activated;
        this.Deactivated += Window_Deactivated;
        this.Closing     += Window_Closing;

        LoadSyntax();
        InitializeTabs();
        LoadScripts();
        SetupLogTailer();
        SetupProcessChecker();
    }

    // Syntax loading
    private void LoadSyntax(string? augmentedXshd = null)
    {
        try
        {
            if (augmentedXshd != null)
            {
                using var sr = new System.IO.StringReader(augmentedXshd);
                using var xr = XmlReader.Create(sr);
                _luauHighlighting = HighlightingLoader.Load(xr, HighlightingManager.Instance);
                return;
            }

            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var s = asm.GetManifestResourceStream("MossadStudio.Luau.xshd");
            if (s != null)
            {
                using var reader = new XmlTextReader(s);
                _luauHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            }
        }
        catch { }
    }

    // Tab initialisation
    private void InitializeTabs()
    {
        _outputEditor = CreateAvalonEditor(readOnly: true);
        EditorTabs.Items.Add(CreateTabItem("Output", _outputEditor, closable: false));

        // Restore previously saved tabs
        var saved = TabPersistenceService.Load();
        if (saved.Count > 0)
        {
            foreach (var td in saved)
            {
                AddScriptTab(td.Title, td.Content);
                // Keep _tabCounter ahead of restored names like "Script3.lua"
                if (td.Title.StartsWith("Script", StringComparison.OrdinalIgnoreCase) &&
                    td.Title.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                {
                    string mid = td.Title[6..^4];
                    if (int.TryParse(mid, out int n))
                        _tabCounter = Math.Max(_tabCounter, n + 1);
                }
            }
        }
        else
        {
            AddScriptTab($"Script{_tabCounter++}.lua");
        }

        EditorTabs.SelectedIndex = 1;
    }

    // Editor factories
    private TextEditor CreateAvalonEditor(bool readOnly)
    {
        return new TextEditor
        {
            ShowLineNumbers          = !readOnly,
            Foreground               = readOnly
                                         ? Brushes.White
                                         : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#cccccc")),
            Background               = Brushes.Transparent,
            FontFamily               = new FontFamily("Consolas"),
            FontSize                 = 13,
            BorderThickness          = new Thickness(0),
            LineNumbersForeground    = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888")),
            SyntaxHighlighting       = readOnly ? null : _luauHighlighting,
            IsReadOnly               = readOnly,
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = System.Windows.Controls.ScrollBarVisibility.Auto
        };
    }

    // Tab helpers
    private TabItem CreateTabItem(string title, UIElement content, bool closable = true)
    {
        var tab = new TabItem { Content = content };

        var header = new Grid { Width = 90 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var titleText = new TextBlock
        {
            Text                = title,
            VerticalAlignment   = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            TextTrimming        = TextTrimming.CharacterEllipsis
        };
        header.Children.Add(titleText);

        if (closable)
        {
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var x = new TextBlock
            {
                Text                = "X",
                Foreground          = Brushes.Gray,
                FontSize            = 10,
                Cursor              = Cursors.Hand,
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            x.MouseEnter += (_, _) => x.Foreground = Brushes.White;
            x.MouseLeave += (_, _) => x.Foreground = Brushes.Gray;
            x.PreviewMouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                EditorTabs.Items.Remove(tab);
                _ = SaveTabsAsync();   // Persist remaining tabs after close
            };
            Grid.SetColumn(x, 1);
            header.Children.Add(x);
        }

        tab.Header = header;
        return tab;
    }

    private string GetTabTitle(TabItem tab)
    {
        if (tab.Header is Grid g)
            foreach (UIElement child in g.Children)
                if (child is TextBlock tb) return tb.Text;
        return "Script";
    }

    private WebView2? FindWebView2(TabItem tab)
    {
        if (tab.Content is Grid g)
            foreach (UIElement child in g.Children)
                if (child is WebView2 wv) return wv;
        return null;
    }

    // Public tab API
    public void AddScriptTab(string title, string content = "")
    {
        if (!_monacoMode)
        {
            var editor = CreateAvalonEditor(readOnly: false);
            editor.Text = content;
            EditorTabs.Items.Add(CreateTabItem(title, editor, closable: true));
            EditorTabs.SelectedItem = EditorTabs.Items[^1];
        }
        else
        {
            // Fire-and-forget; the tab appears immediately with a loading state
            _ = AddMonacoScriptTabAsync(title, content);
        }
    }

    /// <summary>
    /// Creates a Monaco tab where the WebView2 is added to the visual tree
    /// BEFORE EnsureCoreWebView2Async is called (required on some environments).
    /// </summary>
    private async Task AddMonacoScriptTabAsync(string title, string content)
    {
        // 1. Build the container with a loading label
        var bg    = (Color)ColorConverter.ConvertFromString("#222222");
        var grid  = new Grid { Background = new SolidColorBrush(bg) };
        var label = new TextBlock
        {
            Text                = "Initialising Monaco…",
            Foreground          = new SolidColorBrush(Color.FromArgb(0xFF, 0x55, 0x55, 0x55)),
            FontFamily          = new FontFamily("Segoe UI"),
            FontSize            = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center
        };
        var wv = new WebView2();

        grid.Children.Add(wv);
        grid.Children.Add(label);

        // 2. Add to visual tree NOW so EnsureCoreWebView2Async can work
        var tab = CreateTabItem(title, grid, closable: true);
        EditorTabs.Items.Add(tab);
        EditorTabs.SelectedItem = tab;

        try
        {
            // 3. Initialise WebView2 with user-data folder inside bin\ (avoids
            //    the "MossadStudio.exe.WebView2" clutter in the root directory)
            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: AppPaths.WebView2DataDir,
                options: null);
            await wv.EnsureCoreWebView2Async(env);

            // 4. Map virtual host → Monaco folder next to exe
            string monacoDir = MonacoExtractor.GetExtractedPath();
            wv.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "monaco.local", monacoDir,
                CoreWebView2HostResourceAccessKind.Allow);

            // 5. Wait for Monaco JS to fire "monacoReady"
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            EventHandler<CoreWebView2WebMessageReceivedEventArgs>? msgHandler = null;
            msgHandler = (_, e) =>
            {
                if (e.TryGetWebMessageAsString() == "monacoReady")
                {
                    wv.CoreWebView2.WebMessageReceived -= msgHandler;
                    ready.TrySetResult(true);
                }
            };
            wv.CoreWebView2.WebMessageReceived += msgHandler;

            wv.CoreWebView2.Navigate("https://monaco.local/editor.html");

            // 20-second safety timeout
            var winner = await Task.WhenAny(ready.Task, Task.Delay(20_000));
            if (winner != ready.Task)
                throw new TimeoutException("Monaco editor did not respond within 20 seconds.");

            // 6. Set initial content
            string textJson = JsonSerializer.Serialize(content);
            await wv.CoreWebView2.ExecuteScriptAsync($"window.monacoAPI.setText({textJson})");

            // 7. Inject any loaded .d.luau schema
            if (DluauParser.CurrentSchema.Types.Count > 0 || DluauParser.CurrentSchema.Globals.Count > 0)
            {
                string schemaJson = JsonSerializer.Serialize(DluauParser.CurrentSchema);
                await wv.CoreWebView2.ExecuteScriptAsync(
                    $"window.monacoAPI.addDluauCompletions({schemaJson})");
            }

            label.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            label.Text      = $"Monaco error:\n{ex.Message}";
            label.Foreground = new SolidColorBrush(Colors.OrangeRed);
        }
    }

    // Safe Monaco text reader
    private static async Task<string> GetMonacoTextAsync(WebView2 wv)
    {
        // encodeURIComponent converts ALL text → safe ASCII (%XX sequences).
        // getText() || '' guards against null/undefined.
        string jr = await wv.CoreWebView2.ExecuteScriptAsync(
            "encodeURIComponent(window.monacoAPI.getText() || '')");

        // ExecuteScriptAsync wraps the JS string result in JSON double-quotes.
        // Strip them manually — no Deserialize involved.
        if (jr.StartsWith('"') && jr.EndsWith('"') && jr.Length >= 2)
            jr = jr[1..^1];

        return Uri.UnescapeDataString(jr);
    }

    private async Task<string?> GetActiveScriptTextAsync()
    {
        if (EditorTabs.SelectedItem is not TabItem tab) return null;

        if (tab.Content is TextEditor te && !te.IsReadOnly)
            return te.Text;

        var wv = FindWebView2(tab);
        if (wv?.CoreWebView2 == null) return null;

        return await GetMonacoTextAsync(wv);
    }

    private async Task SetActiveScriptTextAsync(string text)
    {
        if (EditorTabs.SelectedItem is not TabItem tab) return;

        if (tab.Content is TextEditor te && !te.IsReadOnly)
        {
            te.Text = text;
            return;
        }

        var wv = FindWebView2(tab);
        if (wv?.CoreWebView2 == null) return;
        await wv.CoreWebView2.ExecuteScriptAsync(
            $"window.monacoAPI.setText({JsonSerializer.Serialize(text)})");
    }

    // Tab persistence
    private async Task SaveTabsAsync()
    {
        var list = new List<TabData>();

        foreach (TabItem t in EditorTabs.Items)
        {
            // Output tab — skip
            if (t.Content is TextEditor te && te.IsReadOnly) continue;

            string title   = GetTabTitle(t);
            string content = "";

            if (t.Content is TextEditor editor)
            {
                content = editor.Text;
            }
            else
            {
                var wv = FindWebView2(t);
                if (wv?.CoreWebView2 != null)
                {
                    try { content = await GetMonacoTextAsync(wv); }
                    catch { content = ""; }
                }
            }

            list.Add(new TabData { Title = title, Content = content });
        }

        TabPersistenceService.Save(list);
    }

    // Editor mode switching
    public async Task SwitchEditorModeAsync(bool useMonaco)
    {
        // Collect all script tab data
        var tabs = new List<(string Title, string Text)>();
        foreach (TabItem t in EditorTabs.Items)
        {
            if (t.Content is TextEditor te && te.IsReadOnly) continue;

            string title   = GetTabTitle(t);
            string content = "";

            if (t.Content is TextEditor editor)
            {
                content = editor.Text;
            }
            else
            {
                var wv = FindWebView2(t);
                if (wv?.CoreWebView2 != null)
                {
                    try { content = await GetMonacoTextAsync(wv); }
                    catch { content = ""; }
                }
            }
            tabs.Add((title, content));
        }

        // Rebuild tabs
        EditorTabs.Items.Clear();
        EditorTabs.Items.Add(CreateTabItem("Output", _outputEditor, closable: false));

        if (tabs.Count == 0) tabs.Add(($"Script{_tabCounter++}.lua", ""));

        foreach (var (title, text) in tabs)
            AddScriptTab(title, text);

        EditorTabs.SelectedIndex = EditorTabs.Items.Count > 1 ? 1 : 0;
    }

    /// <summary>Push .d.luau schema to all open Monaco tabs and rebuild AvalonEdit syntax.</summary>
    public async Task ApplyDluauCompletionsAsync(LuauTypeSchema schema)
    {
        string schemaJson = JsonSerializer.Serialize(schema);

        foreach (TabItem t in EditorTabs.Items)
        {
            var wv = FindWebView2(t);
            if (wv?.CoreWebView2 != null)
                await wv.CoreWebView2.ExecuteScriptAsync(
                    $"window.monacoAPI.addDluauCompletions({schemaJson})");
        }

        string? augmented = DluauParser.BuildAugmentedXshd(schema);
        if (augmented != null)
        {
            LoadSyntax(augmented);
            foreach (TabItem t in EditorTabs.Items)
                if (t.Content is TextEditor te && !te.IsReadOnly)
                    te.SyntaxHighlighting = _luauHighlighting;
        }
    }

    // Tab button (+)
    private void AddTabButton_Click(object sender, MouseButtonEventArgs e)
        => AddScriptTab($"Script{_tabCounter++}.lua");

    // GetActiveEditor is kept for undo/redo/find-replace (AvalonEdit only)
    public TextEditor? GetActiveEditor()
    {
        if (EditorTabs.SelectedItem is TabItem tab && tab.Content is TextEditor editor)
            return editor;
        return null;
    }

    // Bottom action buttons
    private async void BtnExecute_Click(object sender, RoutedEventArgs e)
    {
        if (!SirHurtAPI.IsRobloxRunning())
        {
            SirHurtAPI.IsInjected = false;
            BtnInject.Content   = "INJECT";
            BtnInject.IsEnabled = true;
            MessageBox.Show("RobloxPlayerBeta.exe not found.", "Mossad Studio",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!SirHurtAPI.IsInjected)
        {
            MessageBox.Show("SirHurt is not injected! Press INJECT first.", "Mossad Studio",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string? text = await GetActiveScriptTextAsync();
        if (!string.IsNullOrWhiteSpace(text))
            SirHurtAPI.Execute(text);
    }

    private async void BtnClear_Click(object sender, RoutedEventArgs e)
        => await SetActiveScriptTextAsync("");

    private void BtnOpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter           = "Lua Scripts (*.lua;*.luau;*.txt)|*.lua;*.luau;*.txt|All files (*.*)|*.*",
            InitialDirectory = Path.GetFullPath(SirHurtAPI.ScriptsDir)
        };
        if (dlg.ShowDialog() == true)
            AddScriptTab(Path.GetFileName(dlg.FileName), File.ReadAllText(dlg.FileName));
    }

    private async void BtnSaveFile_Click(object sender, RoutedEventArgs e)
    {
        string? text = await GetActiveScriptTextAsync();
        if (text == null) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter           = "Lua Scripts (*.lua;*.luau;*.txt)|*.lua;*.luau;*.txt|All files (*.*)|*.*",
            InitialDirectory = Path.GetFullPath(SirHurtAPI.ScriptsDir)
        };
        if (dlg.ShowDialog() == true)
        {
            File.WriteAllText(dlg.FileName, text);
            MessageBox.Show("Script saved!", "Mossad Studio",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void BtnInject_Click(object sender, RoutedEventArgs e)
    {
        if (BtnInject.Content.ToString() is "INJECTING" or "INJECTED") return;

        if (!SirHurtAPI.IsRobloxRunning())
        {
            MessageBox.Show("RobloxPlayerBeta.exe not found. Open Roblox first.", "Mossad Studio",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BtnInject.Content   = "INJECTING";
        BtnInject.IsEnabled = false;

        try
        {
            bool success = await SirHurtAPI.InjectAsync();
            if (success)
            {
                BtnInject.Content     = "INJECTED";
                SirHurtAPI.IsInjected = true;
            }
            else
            {
                BtnInject.Content   = "INJECT";
                BtnInject.IsEnabled = true;
            }
        }
        catch (FileNotFoundException ex)
        {
            MessageBox.Show(ex.Message, "Injector Error: Missing Files",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            BtnInject.Content   = "INJECT";
            BtnInject.IsEnabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to inject: {ex.Message}", "Fatal Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            BtnInject.Content   = "INJECT";
            BtnInject.IsEnabled = true;
        }
    }

    // ── Settings panel ────────────────────────────────────────────────────────

    private void BtnOptions_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow == null)
        {
            _settingsWindow       = new SettingsWindow(this);
            _settingsWindow.Owner = this;
            this.LocationChanged += (_, _) => SnapSettingsWindow();
            this.SizeChanged     += (_, _) => SnapSettingsWindow();
        }

        if (_settingsWindow.Visibility == Visibility.Visible)
            _settingsWindow.Hide();
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
            _settingsWindow.Top  = this.Top;
        }
    }

    // Process checker
    private void SetupProcessChecker()
    {
        _robloxProcessTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _robloxProcessTimer.Tick += (_, _) =>
        {
            if (!SirHurtAPI.IsRobloxRunning() &&
                (SirHurtAPI.IsInjected || BtnInject.Content.ToString() == "INJECTED"))
            {
                SirHurtAPI.IsInjected = false;
                BtnInject.Content     = "INJECT";
                BtnInject.IsEnabled   = true;
            }
        };
        _robloxProcessTimer.Start();
    }

    // Log tailer
    private void SetupLogTailer()
    {
        string bsLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Bootstrapper_log.txt");
        if (File.Exists(bsLog))
            _outputEditor.AppendText(File.ReadAllText(bsLog));

        string logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "sirhurt", "sirhui", "sirh_debug_log.dat");

        if (File.Exists(logPath))
        {
            using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            _lastLogPosition = fs.Length;
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.WriteAllText(logPath, "");
        }

        _sirHurtWatcher = new FileSystemWatcher
        {
            Path         = Path.GetDirectoryName(logPath)!,
            Filter       = Path.GetFileName(logPath),
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _sirHurtWatcher.Changed             += OnLogChanged;
        _sirHurtWatcher.EnableRaisingEvents  = true;
    }

    private void OnLogChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            using var fs     = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            if (fs.Length < _lastLogPosition) _lastLogPosition = 0;
            fs.Seek(_lastLogPosition, SeekOrigin.Begin);
            string newContent = reader.ReadToEnd();
            _lastLogPosition  = fs.Length;
            if (!string.IsNullOrEmpty(newContent))
            {
                Dispatcher.Invoke(() =>
                {
                    foreach (var line in newContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        _outputEditor.AppendText($"[SirHurt] {line}\n");
                    _outputEditor.ScrollToEnd();
                });
            }
        }
        catch { }
    }

    // Scripts tree
    private void LoadScripts()
    {
        ScriptsTreeView.Items.Clear();

        if (ScriptsTreeView.ContextMenu == null)
        {
            ScriptsTreeView.ContextMenu = new ContextMenu();
            var refresh = new MenuItem { Header = "Refresh List" };
            refresh.Click += (_, _) => LoadScripts();
            ScriptsTreeView.ContextMenu.Items.Add(refresh);
        }

        if (Directory.Exists(SirHurtAPI.ScriptsDir))
            PopulateTreeView(SirHurtAPI.ScriptsDir, ScriptsTreeView.Items);
    }

    private ContextMenu CreateScriptContextMenu(string path, bool isFile)
    {
        var menu = new ContextMenu();

        var openItem = new MenuItem { Header = isFile ? "Open Script" : "Open Folder" };
        openItem.Click += (_, e) =>
        {
            if (isFile) AddScriptTab(Path.GetFileName(path), File.ReadAllText(path));
            else Process.Start("explorer.exe", path);
            e.Handled = true;
        };
        menu.Items.Add(openItem);

        if (isFile)
        {
            var execItem = new MenuItem { Header = "Execute File" };
            execItem.Click += (_, e) =>
            {
                if (SirHurtAPI.IsInjected) SirHurtAPI.Execute(File.ReadAllText(path));
                e.Handled = true;
            };
            menu.Items.Add(execItem);
        }

        var renameItem = new MenuItem { Header = isFile ? "Rename File" : "Rename Folder" };
        renameItem.Click += (_, e) =>
        {
            var prompt = new PromptWindow("Rename",
                $"Enter new name for {(isFile ? "file" : "folder")}:", Path.GetFileName(path))
            { Owner = this };
            if (prompt.ShowDialog() == true && !string.IsNullOrWhiteSpace(prompt.ResponseText))
            {
                try
                {
                    string newPath = Path.Combine(Path.GetDirectoryName(path)!, prompt.ResponseText);
                    if (isFile) File.Move(path, newPath);
                    else Directory.Move(path, newPath);
                    LoadScripts();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to rename: " + ex.Message, "Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            e.Handled = true;
        };
        menu.Items.Add(renameItem);

        var deleteItem = new MenuItem { Header = isFile ? "Delete File" : "Delete Folder" };
        deleteItem.Click += (_, e) =>
        {
            if (MessageBox.Show($"Permanently delete '{Path.GetFileName(path)}'?", "Confirm Delete",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    if (isFile) File.Delete(path);
                    else Directory.Delete(path, true);
                    LoadScripts();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to delete: " + ex.Message, "Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            e.Handled = true;
        };
        menu.Items.Add(deleteItem);

        menu.Items.Add(new Separator
            { Background = (SolidColorBrush)new BrushConverter().ConvertFromString("#333333")! });
        var refreshItem = new MenuItem { Header = "Refresh List" };
        refreshItem.Click += (_, e) => { LoadScripts(); e.Handled = true; };
        menu.Items.Add(refreshItem);

        return menu;
    }

    private void PopulateTreeView(string dir, ItemCollection parent)
    {
        foreach (var d in Directory.GetDirectories(dir))
        {
            var info = new DirectoryInfo(d);
            var item = new TreeViewItem { Header = info.Name, Tag = d, Foreground = Brushes.LightGray };
            item.ContextMenu = CreateScriptContextMenu(d, false);
            PopulateTreeView(d, item.Items);
            parent.Add(item);
        }
        foreach (var f in Directory.GetFiles(dir))
        {
            var info = new FileInfo(f);
            string ext = info.Extension.ToLower();
            if (ext is ".lua" or ".luau" or ".txt")
            {
                var item = new TreeViewItem { Header = info.Name, Tag = f, Foreground = Brushes.LightGray };
                item.ContextMenu = CreateScriptContextMenu(f, true);
                parent.Add(item);
            }
        }
    }

    private void ScriptsTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ScriptsTreeView.SelectedItem is TreeViewItem sel &&
            sel.Tag is string path && File.Exists(path))
            AddScriptTab(Path.GetFileName(path), File.ReadAllText(path));
    }

    // Fade animation
    private void AnimateOpacity(double to)
        => BeginAnimation(Window.OpacityProperty,
               new DoubleAnimation(to, TimeSpan.FromMilliseconds(200)));

    private void Window_Activated(object sender, EventArgs e)   => AnimateOpacity(1.0);
    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (SettingsManager.Config.TopMost && SettingsManager.Config.FadeEffects)
            AnimateOpacity(0.4);
    }

    // Window closing
    private async void Window_Closing(object sender, CancelEventArgs e)
    {
        if (_isClosing) return;   // second call: let it through

        e.Cancel      = true;     // suspend the close
        _isClosing    = true;

        try { await SaveTabsAsync(); } catch { }

        Application.Current.Shutdown();
    }

    // Window chrome
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e)    => Application.Current.Shutdown();

    // Log helpers
    public void LogCleanerAction(string message)
        => Dispatcher.Invoke(() =>
           {
               _outputEditor.AppendText($"[Cleaner] {message}\n");
               _outputEditor.ScrollToEnd();
           });

    public void LogBootstrapperAction(string message, string tag = "Bootstrapper")
    {
        try { File.AppendAllText(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Bootstrapper_log.txt"),
            $"[{tag}] {message}\n"); }
        catch { }
        Dispatcher.Invoke(() =>
        {
            _outputEditor.AppendText($"[{tag}] {message}\n");
            _outputEditor.ScrollToEnd();
        });
    }

    // Top menu
    private void TopMenu_LeftClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.ContextMenu != null)
        {
            fe.ContextMenu.PlacementTarget = fe;
            fe.ContextMenu.Placement       =
                System.Windows.Controls.Primitives.PlacementMode.Bottom;
            fe.ContextMenu.IsOpen = true;
        }
    }

    private void TopMenu_CreditsClick(object sender, MouseButtonEventArgs e)
        => new CreditsWindow { Owner = this }.ShowDialog();

    private void TopMenu_GamesClick(object sender, MouseButtonEventArgs e)
        => new GamesWindow(this) { Owner = this }.Show();

    // Edit menu shortcuts
    private void BtnUndo_Click(object sender, RoutedEventArgs e)
    {
        var ed = GetActiveEditor();
        if (ed != null && !ed.IsReadOnly) ed.Undo();
        // Monaco: use Ctrl+Z natively
    }

    private void BtnRedo_Click(object sender, RoutedEventArgs e)
    {
        var ed = GetActiveEditor();
        if (ed != null && !ed.IsReadOnly) ed.Redo();
        // Monaco: use Ctrl+Y natively
    }

    private void BtnFindReplace_Click(object sender, RoutedEventArgs e)
    {
        var ed = GetActiveEditor();
        if (ed != null)
        {
            new FindReplaceWindow(this) { Owner = this }.Show();
        }
        else
        {
            // Monaco has built-in Ctrl+H; trigger it via JS
            if (EditorTabs.SelectedItem is TabItem t)
            {
                var wv = FindWebView2(t);
                _ = wv?.CoreWebView2.ExecuteScriptAsync(
                    "editor.trigger('keyboard','actions.find',null)");
            }
        }
    }

    // Hot-scripts
    private void BtnHotScript_InfiniteYield(object sender, RoutedEventArgs e)
    {
        if (SirHurtAPI.IsInjected)
            SirHurtAPI.Execute("loadstring(game:HttpGet('https://raw.githubusercontent.com/EdgeIY/infiniteyield/master/source'))()");
    }

    private void BtnHotScript_Dex(object sender, RoutedEventArgs e)
    {
        if (SirHurtAPI.IsInjected)
            SirHurtAPI.Execute("loadstring(game:HttpGet('https://raw.githubusercontent.com/infyiff/backup/main/dex.lua'))()");
    }

    private void BtnHotScript_OwlHub(object sender, RoutedEventArgs e)
    {
        if (SirHurtAPI.IsInjected)
            SirHurtAPI.Execute("loadstring(game:HttpGet('https://raw.githubusercontent.com/CriShoux/OwlHub/master/OwlHub.txt'))()");
    }

    private void BtnHotScript_UNC(object sender, RoutedEventArgs e)
    {
        if (SirHurtAPI.IsInjected)
            SirHurtAPI.Execute("loadstring(game:HttpGet('https://raw.githubusercontent.com/unified-naming-convention/NamingStandard/refs/heads/main/UNCCheckEnv.lua'))()");
    }

    private void BtnHotScript_SimpleSpy(object sender, RoutedEventArgs e)
    {
        if (SirHurtAPI.IsInjected)
            SirHurtAPI.Execute("loadstring(game:HttpGet('https://raw.githubusercontent.com/infyiff/backup/refs/heads/main/SimpleSpyV3/main.lua'))()");
    }

    private void BtnHotScript_UnnamedESP(object sender, RoutedEventArgs e)
    {
        if (SirHurtAPI.IsInjected)
            SirHurtAPI.Execute("pcall(function() loadstring(game:HttpGet('https://raw.githubusercontent.com/ic3w0lf22/Unnamed-ESP/master/UnnamedESP.lua'))() end)");
    }

    // Others menu
    private void BtnOthers_Dashboard(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo(
               "https://sirhurt.net/login/dashboard.php/") { UseShellExecute = true });

    private void BtnOthers_Discord(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo(
               "https://discord.gg/sirhurt") { UseShellExecute = true });
}