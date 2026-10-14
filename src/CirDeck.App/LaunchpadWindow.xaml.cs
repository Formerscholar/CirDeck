using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CirDeck.Core;
using CirDeck.Interop;
using Microsoft.Win32;

namespace CirDeck.App;

public partial class LaunchpadWindow : Window
{
    private readonly LibrarySession _session;
    private readonly IconBroker _icons;
    private readonly Dictionary<string, TileModel> _tiles = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<LaunchTile> _results = Array.Empty<LaunchTile>();
    private int _page;
    private int _selected;
    private bool _allowClose;
    private Point _pressPoint;
    private bool _dragged;
    private TileModel? _pressed;
    private readonly DispatcherTimer _widgets = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly AudioMeter _audio = new();
    private bool _audioOn;

    public LaunchpadWindow(LibrarySession session, IconBroker icons)
    {
        _session = session;
        _icons = icons;
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            RegisterHotkey();
            WindowChrome.ApplyLaunchpad(new WindowInteropHelper(this).Handle);
        };
        _session.Changed += Refresh;
        _icons.Ready += OnIconReady;
        ApplyColumns();
        Refresh();
        _widgets.Tick += (_, _) => UpdateWidgets();
        _widgets.Start();
        UpdateWidgets();
    }

    public event Action? ToggleRequested;

    public event Action<AppEntry>? LaunchRequested;

    public void AllowClose()
    {
        _allowClose = true;
    }

    public void Present()
    {
        Show();
        PlaceOnCursorMonitor();
        Activate();
        SearchBox.Focus();
        SearchBox.SelectAll();
        Refresh();
    }

    public void Dismiss()
    {
        Hide();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Dismiss();
        }

        base.OnClosing(e);
    }

    private void RegisterHotkey()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        HotkeyWindow.RegisterToggle(hwnd);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (HotkeyWindow.IsHotkey(msg))
        {
            ToggleRequested?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void PlaceOnCursorMonitor()
    {
        var area = MonitorLayout.WorkAreaFromCursor();
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = area.Left / dpi.DpiScaleX;
        Top = area.Top / dpi.DpiScaleY;
        Width = Math.Max(640, area.Width / dpi.DpiScaleX);
        Height = Math.Max(480, area.Height / dpi.DpiScaleY);
    }

    private void ApplyColumns()
    {
        var factory = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Primitives.UniformGrid));
        factory.SetValue(System.Windows.Controls.Primitives.UniformGrid.ColumnsProperty, _session.Settings.Columns);
        Tiles.ItemsPanel = new ItemsPanelTemplate(factory);
        Tiles.MaxWidth = _session.Settings.Columns * 116;
    }

    private void Refresh()
    {
        if (SearchBox == null || Tiles == null || EmptyText == null || Dots == null)
        {
            return;
        }
        _results = string.IsNullOrWhiteSpace(SearchBox.Text) ? _session.Board() : _session.Search(SearchBox.Text);
        var pageSize = PageLayout.PageSize(_session.Settings.Columns, _session.Settings.Rows);
        var pages = PageLayout.PageCount(_results.Count, pageSize);
        _page = PageLayout.Clamp(_page, pages);
        var start = _page * pageSize;
        var visible = Math.Max(0, Math.Min(pageSize, _results.Count - start));
        _selected = visible == 0 ? 0 : Math.Clamp(_selected, 0, visible - 1);
        var slice = new List<TileModel>();
        var end = Math.Min(_results.Count, start + pageSize);
        for (var i = start; i < end; i++)
        {
            var item = _results[i];
            if (!_tiles.TryGetValue(item.Key, out var tile))
            {
                tile = new TileModel(item);
                _tiles[item.Key] = tile;
                LoadIcon(tile);
            }
            else if (tile.Name != item.Name || tile.IsFolder != item.IsFolder)
            {
                tile = new TileModel(item);
                _tiles[item.Key] = tile;
                LoadIcon(tile);
            }

            tile.Selected = slice.Count == _selected;
            slice.Add(tile);
        }

        Tiles.ItemsSource = slice;
        EmptyText.Visibility = slice.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _session.Apps.Count == 0 ? "正在读取开始菜单…" : "没有匹配的应用";
        SearchHint?.SetCurrentValue(VisibilityProperty, string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed);
        FolderTitle.Visibility = string.IsNullOrEmpty(_session.OpenFolderId) ? Visibility.Collapsed : Visibility.Visible;
        var open = _session.Library.Folders.FirstOrDefault(folder => folder.Id == _session.OpenFolderId);
        FolderTitle.Text = open == null ? string.Empty : open.Name;
        DrawDots(pages);
        UpdateWidgets();
    }

    private void LoadIcon(TileModel tile)
    {
        if (!string.IsNullOrWhiteSpace(tile.Tile.IconFile) && File.Exists(tile.Tile.IconFile))
        {
            tile.Icon = IconBroker.LoadImage(tile.Tile.IconFile);
            return;
        }

        var path = tile.Entry?.TargetPath;
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_icons.TryCached(path, out var png))
        {
            tile.Icon = IconBroker.LoadImage(png);
        }
        else
        {
            _icons.Request(path);
        }
    }

    private void DrawDots(int pages)
    {
        Dots.Children.Clear();
        if (pages <= 1)
        {
            return;
        }

        for (var i = 0; i < pages; i++)
        {
            var index = i;
            var active = i == _page;
            var dot = new Border
            {
                Width = active ? 18 : 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(3.5, 0, 3.5, 0),
                Background = new SolidColorBrush(active ? Colors.White : Color.FromArgb(96, 255, 255, 255)),
                Cursor = Cursors.Hand
            };
            dot.MouseLeftButtonUp += (_, _) =>
            {
                _page = index;
                _selected = 0;
                Refresh();
            };
            Dots.Children.Add(dot);
        }
    }

    private void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _page = 0;
        _selected = 0;
        Refresh();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var columns = _session.Settings.Columns;
        if (e.Key == Key.Escape)
        {
            if (!string.IsNullOrEmpty(_session.OpenFolderId))
            {
                _session.OpenFolder(null);
                e.Handled = true;
                return;
            }

            Dismiss();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            LaunchSelected();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Right)
        {
            Move(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            Move(-1);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            Move(columns);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            Move(-columns);
            e.Handled = true;
        }
    }

    private void Move(int delta)
    {
        if (_results.Count == 0)
        {
            return;
        }

        var pageSize = PageLayout.PageSize(_session.Settings.Columns, _session.Settings.Rows);
        var index = _page * pageSize + _selected + delta;
        if (index < 0)
        {
            index = 0;
        }

        if (index >= _results.Count)
        {
            index = _results.Count - 1;
        }

        _page = index / pageSize;
        _selected = index % pageSize;
        Refresh();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var pages = PageLayout.PageCount(_results.Count, PageLayout.PageSize(_session.Settings.Columns, _session.Settings.Rows));
        if (pages <= 1)
        {
            return;
        }

        _page = PageLayout.Clamp(_page + (e.Delta < 0 ? 1 : -1), pages);
        _selected = 0;
        Refresh();
        e.Handled = true;
    }

    private void OnTileClick(object sender, RoutedEventArgs e)
    {
        if (_dragged)
        {
            _dragged = false;
            return;
        }

        if (sender is not Button button || button.DataContext is not TileModel tile)
        {
            return;
        }

        if (tile.IsFolder)
        {
            _session.OpenFolder(tile.Tile.Key.Substring(LibraryBoard.FolderPrefix.Length));
            _page = 0;
            return;
        }

        if (tile.Entry != null)
        {
            LaunchRequested?.Invoke(tile.Entry);
        }
    }

    private void OnTileDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button && button.DataContext is TileModel tile)
        {
            _pressed = tile;
            _pressPoint = e.GetPosition(this);
            _dragged = false;
        }
    }

    private void OnTileMove(object sender, MouseEventArgs e)
    {
        if (_pressed == null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _pressPoint.X) + Math.Abs(point.Y - _pressPoint.Y) > 12)
        {
            _dragged = true;
        }
    }

    private void OnTileMenu(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not TileModel tile)
        {
            return;
        }

        var menu = new ContextMenu();
        if (tile.IsFolder)
        {
            menu.Items.Add(MenuItem("打开", () => _session.OpenFolder(tile.Tile.Key.Substring(LibraryBoard.FolderPrefix.Length))));
        }
        else if (tile.Entry != null)
        {
            var entry = tile.Entry;
            menu.Items.Add(MenuItem("打开", () => LaunchRequested?.Invoke(entry)));
            var pinned = _session.IsPinned(entry);
            menu.Items.Add(MenuItem(pinned ? "从 Dock 移除" : "固定到 Dock", () => _session.TogglePin(entry)));
            menu.Items.Add(MenuItem("重命名", () => Rename(entry)));
            menu.Items.Add(MenuItem("更换图标", () => ChangeIcon(entry)));
            menu.Items.Add(MenuItem("隐藏", () => _session.Hide(entry)));
            menu.Items.Add(MenuItem("新建文件夹", () => CreateFolder(entry)));
            if (!string.IsNullOrEmpty(_session.OpenFolderId))
            {
                menu.Items.Add(MenuItem("移出文件夹", () => _session.RemoveFromFolder(entry)));
            }

            menu.Items.Add(MenuItem("打开文件所在位置", () => Reveal(entry)));
        }

        menu.IsOpen = true;
        e.Handled = true;
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_session);
        window.ShowDialog();
        _tiles.Clear();
        Refresh();
    }

    private void Rename(AppEntry entry)
    {
        var name = Prompt.Ask("重命名", entry.Name);
        if (name != null)
        {
            _tiles.Remove(entry.TargetPath);
            _session.Rename(entry, name);
        }
    }

    private void ChangeIcon(AppEntry entry)
    {
        var dialog = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.ico" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        Directory.CreateDirectory(AppPaths.CustomIcons);
        var destination = Path.Combine(AppPaths.CustomIcons, Guid.NewGuid().ToString("n") + Path.GetExtension(dialog.FileName));
        File.Copy(dialog.FileName, destination, true);
        _tiles.Remove(entry.TargetPath);
        _session.SetIcon(entry, destination);
    }

    private void CreateFolder(AppEntry entry)
    {
        var name = Prompt.Ask("文件夹名称", "新建文件夹");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _tiles.Clear();
        _session.CreateFolder(entry, name);
    }

    private void UpdateWidgets()
    {
        if (ClockText == null)
        {
            return;
        }

        ClockText.Visibility = _session.Settings.ShowClock ? Visibility.Visible : Visibility.Collapsed;
        ClockText.Text = DateTime.Now.ToString("HH:mm");
        MonitorText.Visibility = _session.Settings.ShowSystemMonitor ? Visibility.Visible : Visibility.Collapsed;
        if (_session.Settings.ShowSystemMonitor)
        {
            var sample = SystemSampler.Read();
            MonitorText.Text = "CPU " + sample.CpuPercent.ToString("0") + "%    " + sample.MemoryText;
        }

        Spectrum.Visibility = _session.Settings.ShowMusicSpectrum ? Visibility.Visible : Visibility.Collapsed;
        if (_session.Settings.ShowMusicSpectrum)
        {
            if (!_audioOn)
            {
                _audio.Start();
                _audioOn = true;
            }

            var levels = new float[8];
            _audio.CopyLevels(levels);
            if (Spectrum.Children.Count != levels.Length)
            {
                Spectrum.Children.Clear();
                for (var i = 0; i < levels.Length; i++)
                {
                    Spectrum.Children.Add(new System.Windows.Shapes.Rectangle
                    {
                        Width = 5,
                        Margin = new Thickness(2, 0, 2, 0),
                        Fill = Brushes.White,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        RadiusX = 1,
                        RadiusY = 1
                    });
                }
            }

            for (var i = 0; i < levels.Length; i++)
            {
                ((System.Windows.Shapes.Rectangle)Spectrum.Children[i]).Height = 4 + levels[i] * 18;
            }
        }
        else if (_audioOn)
        {
            _audio.Stop();
            _audioOn = false;
        }
    }

    private void LaunchSelected()
    {
        var pageSize = PageLayout.PageSize(_session.Settings.Columns, _session.Settings.Rows);
        var index = _page * pageSize + _selected;
        if (index < 0 || index >= _results.Count)
        {
            return;
        }

        var item = _results[index];
        if (item.IsFolder)
        {
            _session.OpenFolder(item.Key.Substring(LibraryBoard.FolderPrefix.Length));
            _page = 0;
            return;
        }

        if (item.App != null)
        {
            LaunchRequested?.Invoke(item.App);
        }
    }

    private void OnTileUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragged || _pressed == null || !string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            _pressed = null;
            return;
        }

        var element = InputHitTest(e.GetPosition(this)) as DependencyObject;
        while (element != null && element is not Button)
        {
            element = VisualTreeHelper.GetParent(element);
        }

        if (element is Button button && button.DataContext is TileModel target && !string.Equals(target.Tile.Key, _pressed.Tile.Key, StringComparison.OrdinalIgnoreCase))
        {
            _session.MoveTile(_pressed.Tile.Key, target.Tile.Key);
        }

        _pressed = null;
    }

    private void OnIconReady(string path, string png)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_tiles.TryGetValue(path, out var tile))
            {
                tile.Icon = IconBroker.LoadImage(png);
            }
        }));
    }

    private static void Reveal(AppEntry entry)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "/select,\"" + entry.TargetPath + "\"",
            UseShellExecute = true
        });
    }

    private static MenuItem MenuItem(string title, Action action)
    {
        var item = new MenuItem { Header = title };
        item.Click += (_, _) => action();
        return item;
    }
}
