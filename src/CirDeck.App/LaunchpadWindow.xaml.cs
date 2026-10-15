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
    private int _selected = -1;
    private bool _allowClose;
    private Point _pressPoint;
    private bool _dragged;
    private TileModel? _pressed;
    private readonly DispatcherTimer _widgets = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly AudioMeter _audio = new();
    private bool _audioOn;
    private readonly TranslateTransform _slide = new();
    private double _slideTarget;
    private double _slideAtPress;
    private bool _sliding;
    private bool _ignoreGesture;
    private bool _settingsOpen;
    private bool _pinDrag;
    private int _visible;
    private int _laidColumns = -1;
    private bool _slideExit;
    private int _slideDir;
    private TimeSpan _slideClock;
    private const double CellWidth = 168;
    private const double CellHeight = 152;
    private double _cellW = CellWidth;
    private double _cellH = CellHeight;

    public LaunchpadWindow(LibrarySession session, IconBroker icons)
    {
        _session = session;
        _icons = icons;
        InitializeComponent();
        Tiles.RenderTransform = _slide;
        SizeChanged += (_, _) => ApplyColumns();
        SourceInitialized += (_, _) =>
        {
            RegisterHotkey();
            WindowChrome.ApplyLaunchpad(new WindowInteropHelper(this).Handle);
            Backdrop.Source = DesktopBackdrop.Load(_session.Settings.BlurRadius);
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
        WindowChrome.HideFromSwitcher(new WindowInteropHelper(this).Handle);
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
        Topmost = !_session.Settings.KeepOnDesktop;
        var area = _session.Settings.DisplayScreen == "Primary"
            ? MonitorLayout.WorkAreaPrimary()
            : MonitorLayout.WorkAreaFromCursor();
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = area.Left / dpi.DpiScaleX;
        Top = area.Top / dpi.DpiScaleY;
        Width = Math.Max(640, area.Width / dpi.DpiScaleX);
        Height = Math.Max(480, area.Height / dpi.DpiScaleY);
    }

    private void ApplyColumns()
    {
        if (Tiles == null)
        {
            return;
        }

        var columns = Math.Max(1, _session.Settings.Columns);
        var rows = Math.Max(1, _session.Settings.Rows);
        var width = ActualWidth > 240 ? ActualWidth : 1600;
        var height = ActualHeight > 240 ? ActualHeight : 1000;
        var settings = _session.Settings;
        var gap = settings.DockSpacing;
        _cellW = Math.Min(220, Math.Max(108, (width - settings.ContentPadX) / columns)) + gap * 0.35;
        var block = 68 * settings.IconScale + (settings.ShowAppNames ? settings.NameFontSize + 14 : 6);
        var minCell = block + 48;
        var room = (height - 400 - settings.ContentPadTop) / rows;
        _cellH = Math.Max(minCell, Math.Min(room, block + 84));
        FitTiles();
    }

    private void FitTiles()
    {
        var columns = Math.Max(1, _session.Settings.Columns);
        var rows = Math.Max(1, _session.Settings.Rows);
        if (_laidColumns != columns)
        {
            var factory = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Primitives.UniformGrid));
            factory.SetValue(System.Windows.Controls.Primitives.UniformGrid.ColumnsProperty, columns);
            Tiles.ItemsPanel = new ItemsPanelTemplate(factory);
            _laidColumns = columns;
        }

        Tiles.Width = columns * _cellW;
        Tiles.Height = rows * _cellH;
        Tiles.HorizontalAlignment = HorizontalAlignment.Center;
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
        if (visible == 0)
        {
            _selected = -1;
        }
        else if (_selected >= visible)
        {
            _selected = visible - 1;
        }
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

            tile.Selected = _selected >= 0 && slice.Count == _selected;
            slice.Add(tile);
        }

        Tiles.ItemsSource = slice;
        EmptyText.Visibility = slice.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _session.Apps.Count == 0 ? "正在读取开始菜单…" : "没有匹配的应用";
        SearchHint?.SetCurrentValue(VisibilityProperty, string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed);
        FolderTitle.Visibility = string.IsNullOrEmpty(_session.OpenFolderId) ? Visibility.Collapsed : Visibility.Visible;
        var open = _session.Library.Folders.FirstOrDefault(folder => folder.Id == _session.OpenFolderId);
        FolderTitle.Text = open == null ? string.Empty : open.Name;
        _visible = visible;
        if (!_sliding)
        {
            _slide.X = 0;
        }

        FitTiles();
        ApplyLook();
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
                Width = active ? 7 : 5,
                Height = 5,
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(4, 0, 4, 0),
                Background = new SolidColorBrush(active ? Color.FromArgb(230, 255, 255, 255) : Color.FromArgb(70, 255, 255, 255)),
                Cursor = Cursors.Hand
            };
            dot.MouseLeftButtonUp += (_, _) => NudgeTo(index);
            Dots.Children.Add(dot);
        }
    }

    private void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        StopSlide();
        _slide.X = 0;
        _page = 0;
        _selected = 0;
        Refresh();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var columns = _session.Settings.Columns;
        if (e.Key == Key.L && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _session.Settings.LockIcons = !_session.Settings.LockIcons;
            _session.CommitSettings();
            e.Handled = true;
            return;
        }

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
        var origin = _selected < 0 ? (_page * pageSize) : (_page * pageSize + _selected);
        var index = _selected < 0 ? (delta > 0 ? origin : origin + pageSize - 1) : origin + delta;
        if (index < 0)
        {
            index = 0;
        }

        if (index >= _results.Count)
        {
            index = _results.Count - 1;
        }

        var nextPage = index / pageSize;
        var pageChanged = nextPage != _page;
        _page = nextPage;
        _selected = index % pageSize;
        if (pageChanged)
        {
            StopSlide();
            _slide.X = delta > 0 ? 132 : -132;
            Refresh();
            StartSlide(0, false, 0);
            return;
        }

        Refresh();
    }

    private void NudgeTo(int page)
    {
        var pages = PageLayout.PageCount(_results.Count, PageLayout.PageSize(_session.Settings.Columns, _session.Settings.Rows));
        page = PageLayout.Clamp(page, pages);
        if (page == _page || pages <= 1)
        {
            return;
        }

        StopSlide();
        var dir = page > _page ? 1 : -1;
        _page = page;
        _selected = -1;
        _slide.X = dir * 136;
        Refresh();
        StartSlide(0, false, 0);
    }

    private void StartSlide(double target, bool exit, int direction)
    {
        _slideTarget = target;
        _slideExit = exit;
        _slideDir = direction;
        if (_sliding)
        {
            return;
        }

        _sliding = true;
        _slideClock = TimeSpan.Zero;
        CompositionTarget.Rendering += OnSlide;
    }

    private void StopSlide()
    {
        if (_sliding)
        {
            CompositionTarget.Rendering -= OnSlide;
            _sliding = false;
        }

        _slideExit = false;
        _slideClock = TimeSpan.Zero;
    }

    private void OnSlide(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs args ? args.RenderingTime : TimeSpan.Zero;
        var dt = _slideClock == TimeSpan.Zero ? 1d / 60 : (now - _slideClock).TotalSeconds;
        _slideClock = now;
        if (dt <= 0 || dt > 0.05)
        {
            dt = 1d / 60;
        }

        var pace = _session.Settings.PageSpeed switch
        {
            "Slow" => 0.65,
            "Normal" => 0.85,
            _ => 1d
        };
        var omega = (_slideExit ? 26d : 18d) * pace;
        _slide.X += (_slideTarget - _slide.X) * (1 - Math.Exp(-omega * dt));
        if (Math.Abs(_slideTarget - _slide.X) > 0.8)
        {
            return;
        }

        _slide.X = _slideTarget;
        if (!_slideExit)
        {
            StopSlide();
            return;
        }

        var pages = PageLayout.PageCount(_results.Count, PageLayout.PageSize(_session.Settings.Columns, _session.Settings.Rows));
        var next = PageLayout.Clamp(_page + _slideDir, pages);
        _slideExit = false;
        if (next == _page)
        {
            _slideTarget = 0;
            return;
        }

        _page = next;
        _selected = -1;
        _pressed = null;
        _slide.X = _slideDir > 0 ? 148 : -148;
        _slideTarget = 0;
        Refresh();
    }

    private static double Rubber(double value)
    {
        const double limit = 300;
        var distance = Math.Abs(value);
        if (distance <= limit)
        {
            return value;
        }

        return Math.Sign(value) * (limit + (distance - limit) * 0.2);
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var pages = PageLayout.PageCount(_results.Count, PageLayout.PageSize(_session.Settings.Columns, _session.Settings.Rows));
        if (pages <= 1)
        {
            return;
        }

        NudgeTo(PageLayout.Clamp(_page + (e.Delta < 0 ? 1 : -1), pages));
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
            if (_session.Settings.ClickBounce)
            {
                button.RenderTransformOrigin = new Point(0.5, 0.5);
                button.RenderTransform = new ScaleTransform(0.9, 0.9);
            }

            LaunchRequested?.Invoke(tile.Entry);
        }
    }

    private void OnTileLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        ApplyTile(button);
    }

    private void OnTileEnter(object sender, MouseEventArgs e)
    {
        if (!_session.Settings.HoverTilt || sender is not Button button)
        {
            return;
        }

        button.RenderTransformOrigin = new Point(0.5, 0.8);
        button.RenderTransform = new RotateTransform(-8);
    }

    private void OnTileLeave(object sender, MouseEventArgs e)
    {
        if (sender is Button button)
        {
            button.RenderTransform = Transform.Identity;
        }
    }

    private void ApplyLook()
    {
        if (Stage == null || Veil == null || SearchChrome == null)
        {
            return;
        }

        var settings = _session.Settings;
        Stage.Margin = new Thickness(settings.PagePadX, 52 + settings.ContentPadTop, settings.PagePadX, 100 + settings.PagePadY);
        Veil.Opacity = Math.Clamp(0.16 + settings.VeilOpacity * 0.35, 0, 0.55);
        var blur = settings.BlurRadius <= 1 ? 0 : Math.Max(settings.BlurRadius, 72);
        Backdrop.Source = DesktopBackdrop.Load(blur);
        SettingsButton.Visibility = settings.HideSettingsButton ? Visibility.Collapsed : Visibility.Visible;
        SearchChrome.Visibility = settings.SearchMode == "TopHover" && Mouse.GetPosition(this).Y > 72
            ? Visibility.Collapsed
            : Visibility.Visible;
        DateText.Visibility = settings.ShowClockDate ? Visibility.Visible : Visibility.Collapsed;
        SecondCard.Visibility = settings.ShowSeconds ? Visibility.Visible : Visibility.Collapsed;
        var clockFill = settings.ClockTransparent ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(0xF0, 0x14, 0x14, 0x18));
        HourCard.Background = clockFill;
        MinuteCard.Background = clockFill;
        SecondCard.Background = clockFill;
        HourText.FontSize = settings.ClockFontSize;
        MinuteText.FontSize = settings.ClockFontSize;
        SecondText.FontSize = settings.ClockFontSize;
        var nameBrush = settings.NameColor == "Black" ? Brushes.Black : Brushes.White;
        foreach (var button in FindVisuals<Button>(Tiles))
        {
            ApplyTile(button, nameBrush);
        }
    }

    private void ApplyTile(Button button, Brush? nameBrush = null)
    {
        var settings = _session.Settings;
        nameBrush ??= settings.NameColor == "Black" ? Brushes.Black : Brushes.White;
        var well = FindVisuals<Border>(button).FirstOrDefault();
        var image = FindVisuals<Image>(button).FirstOrDefault();
        if (well != null)
        {
            var wellSize = 68 * settings.IconScale;
            well.Width = wellSize;
            well.Height = wellSize;
            well.CornerRadius = settings.IconShape switch
            {
                "Circle" => new CornerRadius(wellSize / 2),
                "Square" => new CornerRadius(6),
                _ => new CornerRadius(18 * settings.IconScale)
            };
        }

        if (image != null)
        {
            var glyph = 42 * settings.IconScale;
            image.Width = glyph;
            image.Height = glyph;
            image.Clip = null;
        }

        var label = FindVisuals<TextBlock>(button).FirstOrDefault(item => item.Text != null && item.FontFamily.Source != "Segoe MDL2 Assets");
        if (label == null)
        {
            return;
        }

        label.Visibility = settings.ShowAppNames ? Visibility.Visible : Visibility.Collapsed;
        label.FontSize = settings.NameFontSize;
        label.Foreground = nameBrush;
        label.Effect = settings.NameShadow
            ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Opacity = 0.8, Color = Colors.Black }
            : null;
    }

    private static IEnumerable<T> FindVisuals<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindVisuals<T>(child))
            {
                yield return nested;
            }
        }
    }

    private void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && (IsInside(source, typeof(TextBox)) || IsInside(source, SettingsButton)))
        {
            _ignoreGesture = true;
            return;
        }

        _ignoreGesture = false;

        StopSlide();
        _pressPoint = e.GetPosition(this);
        _slideAtPress = _slide.X;
        _dragged = false;
        _pressed = TileUnder(e.GetPosition(this));
        Mouse.Capture(this);
    }

    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (_session.Settings.SearchMode == "TopHover" && SearchChrome != null)
        {
            SearchChrome.Visibility = e.GetPosition(this).Y < 72 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (!IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var point = e.GetPosition(this);
        var dx = point.X - _pressPoint.X;
        var dy = point.Y - _pressPoint.Y;
        if (Math.Abs(dx) < 8 && Math.Abs(dy) < 8)
        {
            return;
        }

        _dragged = true;
        if (_pressed?.Entry != null && dy > 48 && dy > Math.Abs(dx))
        {
            var path = _pressed.Entry.TargetPath;
            _pressed = null;
            _pinDrag = true;
            Mouse.Capture(null);
            DragDrop.DoDragDrop(this, new DataObject(LibrarySession.PinDragFormat, path), DragDropEffects.Move);
            return;
        }

        if (Math.Abs(dx) > Math.Abs(dy) && string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            _slide.X = LimitEdge(Rubber(_slideAtPress + dx));
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
        e.Handled = true;
        if (_settingsOpen)
        {
            return;
        }

        _settingsOpen = true;
        _ignoreGesture = true;
        Mouse.Capture(null);
        OpenSettingsDialog();
    }

    private void OpenSettingsDialog()
    {
        var topmost = Topmost;
        Topmost = false;
        try
        {
            var window = new SettingsWindow(_session)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Topmost = true
            };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            Log.Write(ex);
        }
        finally
        {
            _settingsOpen = false;
            Topmost = topmost;
        }

        _tiles.Clear();
        ApplyColumns();
        PlaceOnCursorMonitor();
        Refresh();
    }

    private double LimitEdge(double offset)
    {
        var pages = PageLayout.PageCount(_results.Count, PageLayout.PageSize(_session.Settings.Columns, _session.Settings.Rows));
        if (!_session.Settings.ShortSwipeBounce)
        {
            if (_page <= 0 && offset > 0)
            {
                return 0;
            }

            if (_page >= pages - 1 && offset < 0)
            {
                return 0;
            }
        }

        if (_page <= 0 && offset > 0)
        {
            return Math.Min(offset, 24);
        }

        if (_page >= pages - 1 && offset < 0)
        {
            return Math.Max(offset, -24);
        }

        return offset;
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
        if (HourText == null)
        {
            return;
        }

        var now = DateTime.Now;
        var showClock = _session.Settings.ShowClock;
        ClockPanel.Visibility = showClock ? Visibility.Visible : Visibility.Collapsed;
        if (showClock)
        {
            DateText.Text = now.ToString("yyyy年M月d日 · dddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
            HourText.Text = now.ToString("HH");
            MinuteText.Text = now.ToString("mm");
            SecondText.Text = now.ToString("ss");
        }

        var showMonitor = _session.Settings.ShowSystemMonitor;
        MonitorPill.Visibility = showMonitor ? Visibility.Visible : Visibility.Collapsed;
        if (showMonitor)
        {
            var sample = SystemSampler.Read();
            CpuText.Text = sample.CpuPercent.ToString("0") + "%";
            RamText.Text = sample.MemoryPercent.ToString("0") + "%";
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
                ((System.Windows.Shapes.Rectangle)Spectrum.Children[i]).Height = 3 + levels[i] * 14;
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
        if (_selected < 0)
        {
            return;
        }

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
        if (_ignoreGesture || _pinDrag)
        {
            _ignoreGesture = false;
            _pinDrag = false;
            Mouse.Capture(null);
            return;
        }

        var point = e.GetPosition(this);
        var dx = point.X - _pressPoint.X;
        var dy = point.Y - _pressPoint.Y;
        var horizontal = _dragged && Math.Abs(dx) > _session.Settings.SwipeThreshold && Math.Abs(dx) > Math.Abs(dy);
        Mouse.Capture(null);

        if (horizontal && string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            var pages = PageLayout.PageCount(_results.Count, PageLayout.PageSize(_session.Settings.Columns, _session.Settings.Rows));
            var dir = dx < 0 ? 1 : -1;
            var next = PageLayout.Clamp(_page + dir, pages);
            _pressed = null;
            if (next == _page)
            {
                StartSlide(0, false, 0);
            }
            else
            {
                StartSlide(dir > 0 ? -340 : 340, true, dir);
            }

            return;
        }

        if (Math.Abs(_slide.X) > 0.5)
        {
            StartSlide(0, false, 0);
        }

        if (!_dragged || _pressed == null || !string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            _pressed = null;
            return;
        }

        var target = TileUnder(point);
        if (!_session.Settings.LockIcons && target != null && !string.Equals(target.Tile.Key, _pressed.Tile.Key, StringComparison.OrdinalIgnoreCase))
        {
            _session.MoveTile(_pressed.Tile.Key, target.Tile.Key);
        }

        _pressed = null;
    }

    private TileModel? TileUnder(Point point)
    {
        var element = InputHitTest(point) as DependencyObject;
        while (element != null && element is not Button)
        {
            element = VisualTreeHelper.GetParent(element);
        }

        return element is Button button ? button.DataContext as TileModel : null;
    }

    private static bool IsInside(DependencyObject source, Type type)
    {
        while (source != null)
        {
            if (type.IsInstanceOfType(source))
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static bool IsInside(DependencyObject source, DependencyObject target)
    {
        while (source != null)
        {
            if (ReferenceEquals(source, target))
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
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
