using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CirDeck.Core;
using CirDeck.Interop;

namespace CirDeck.App;

public partial class LaunchpadWindow : Window
{
    private readonly LibrarySession _session;
    private readonly IconBroker _icons;
    private readonly Dictionary<string, TileModel> _tiles = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<AppEntry> _results = Array.Empty<AppEntry>();
    private int _page;
    private int _selected;
    private bool _allowClose;

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
        _results = _session.Query(SearchBox.Text);
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
            var entry = _results[i];
            if (!_tiles.TryGetValue(entry.TargetPath, out var tile))
            {
                tile = new TileModel(entry);
                _tiles[entry.TargetPath] = tile;
                if (_icons.TryCached(entry.TargetPath, out var png))
                {
                    tile.Icon = IconBroker.LoadImage(png);
                }
                else
                {
                    _icons.Request(entry.TargetPath);
                }
            }

            tile.Selected = slice.Count == _selected;
            slice.Add(tile);
        }

        Tiles.ItemsSource = slice;
        EmptyText.Visibility = slice.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _session.Apps.Count == 0 ? "正在读取开始菜单…" : "没有匹配的应用";
        SearchHint?.SetCurrentValue(VisibilityProperty, string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed);
        DrawDots(pages);
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
        if (sender is Button button && button.DataContext is TileModel tile)
        {
            LaunchRequested?.Invoke(tile.Entry);
        }
    }

    private void OnTileMenu(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not TileModel tile)
        {
            return;
        }

        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("打开", () => LaunchRequested?.Invoke(tile.Entry)));
        var pinned = _session.IsPinned(tile.Entry);
        menu.Items.Add(MenuItem(pinned ? "从 Dock 移除" : "固定到 Dock", () => _session.TogglePin(tile.Entry)));
        menu.Items.Add(MenuItem("打开文件所在位置", () => Reveal(tile.Entry)));
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void LaunchSelected()
    {
        var pageSize = PageLayout.PageSize(_session.Settings.Columns, _session.Settings.Rows);
        var index = _page * pageSize + _selected;
        if (index >= 0 && index < _results.Count)
        {
            LaunchRequested?.Invoke(_results[index]);
        }
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
