using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CirDeck.Core;
using CirDeck.Interop;

namespace CirDeck.App;

public partial class DockWindow : Window
{
    private readonly LibrarySession _session;
    private readonly IconBroker _icons;
    private readonly List<AppEntry> _entries = new();
    private readonly List<long> _hwnds = new();
    private readonly List<bool> _bins = new();
    private IReadOnlyList<DesktopWindow> _desktop = Array.Empty<DesktopWindow>();
    private bool _cornerReveal;
    private bool _withLaunchpad;
    private Point _pressPoint;
    private int _pressIndex = -1;
    private bool _suppressClick;
    private TrayPanelWindow? _tray;
    private readonly System.Windows.Threading.DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<ScaleTransform> _transforms = new();
    private readonly List<Ellipse> _dots = new();
    private readonly List<Image> _images = new();
    private double[] _scale = Array.Empty<double>();
    private double[] _velocity = Array.Empty<double>();
    private DockFrame _frame = new(0, 0, Array.Empty<DockPose>());
    private bool _ticking;
    private bool _allowClose;
    private string _signature = string.Empty;
    private int _hoverIndex = -1;
    private DateTime _hoverSince;
    private readonly Window _preview = new()
    {
        Width = 280,
        Height = 168,
        WindowStyle = WindowStyle.None,
        ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false,
        ShowActivated = false,
        Topmost = true,
        Background = System.Windows.Media.Brushes.Black,
        AllowsTransparency = false
    };
    private IntPtr _thumbnail;

    public DockWindow(LibrarySession session, IconBroker icons)
    {
        _session = session;
        _icons = icons;
        InitializeComponent();
        SizeChanged += (_, _) => { };
        _session.Changed += Rebuild;
        _icons.Ready += OnIconReady;
        SourceInitialized += (_, _) => WindowChrome.HideFromSwitcher(new WindowInteropHelper(this).Handle);
        _preview.SourceInitialized += (_, _) => WindowChrome.HideFromSwitcher(new WindowInteropHelper(_preview).Handle);
        _clock.Tick += (_, _) => UpdateClock();
        _clock.Start();
        UpdateClock();
    }

    public event Action<AppEntry>? LaunchRequested;

    public event Action? ExitRequested;

    public event Action? RescanRequested;

    public void AllowClose()
    {
        _allowClose = true;
    }

    public void ShowDock()
    {
        Rebuild();
        if (_entries.Count == 0 && !_session.Settings.ShowDockClock)
        {
            Hide();
            return;
        }

        Show();
        WindowChrome.HideFromSwitcher(new WindowInteropHelper(this).Handle);
        Place();
        ApplyZOrder();
    }

    public bool ShouldStayOpen => _session.Settings.ShowDock && (_withLaunchpad || _session.Settings.KeepDockOnDesktop || !_session.Settings.EnableDockHotCorner || _cornerReveal);

    public void SetLaunchpadOpen(bool open)
    {
        _withLaunchpad = open;
    }

    public void RevealFromCorner()
    {
        _cornerReveal = true;
        ShowDock();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private const double DockPadX = 18;
    private const double DockPadY = 12;

    private double IconSize => _session.Settings.DockIconSize;

    private double IconGap => _session.Settings.DockSpacing;

    private double IconGrow => _session.Settings.DockMagnify ? _session.Settings.DockMagnifyScale : 1;

    public void SetDesktopWindows(IReadOnlyList<DesktopWindow> windows)
    {
        var next = string.Join("|", windows.Select(item => item.Path));
        var current = string.Join("|", _desktop.Select(item => item.Path));
        if (next == current)
        {
            return;
        }

        _desktop = windows;
        _signature = string.Empty;
        Rebuild();
    }

    private void Rebuild()
    {
        var settings = _session.Settings;
        var entries = Compose(settings);
        var signature = string.Join("|", entries.Select(entry => entry.App.TargetPath + ":" + entry.Handle)) + ":" + settings.DockIconSize + ":" + settings.DockSpacing + ":" + settings.DockMagnifyScale + ":" + settings.DockMaterial + ":" + settings.ShowDockClock;
        if (signature == _signature && IsVisible)
        {
            return;
        }

        _signature = signature;
        _entries.Clear();
        _hwnds.Clear();
        _bins.Clear();
        foreach (var entry in entries)
        {
            _entries.Add(entry.App);
            _hwnds.Add(entry.Handle);
            _bins.Add(entry.Bin);
        }
        _transforms.Clear();
        _dots.Clear();
        _images.Clear();
        Surface.Children.Clear();
        _scale = new double[_entries.Count];
        _velocity = new double[_entries.Count];
        for (var i = 0; i < _entries.Count; i++)
        {
            _scale[i] = 1;
            Surface.Children.Add(CreateVisual(_entries[i]));
            if (i < _dots.Count)
            {
                _dots[i].Visibility = _hwnds[i] != 0 && settings.ShowRunningIndicators ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        var magnify = IconGrow;
        var request = new DockRequest(_entries.Count, IconSize, IconGap, DockPadX, DockPadY, magnify, double.NaN, settings.DockMagnify);
        var plateHeight = DockPadY * 2 + IconSize;
        var iconWidth = Math.Max(120, DockGeometry.ReserveWidth(_entries.Count, IconSize, IconGap, DockPadX, magnify));
        LayoutTail(settings, plateHeight);
        Tail.Measure(new Size(double.PositiveInfinity, plateHeight));
        var tailWidth = Tail.Visibility == Visibility.Visible ? Tail.DesiredSize.Width + 8 : 0;
        Surface.Width = iconWidth;
        Width = iconWidth + tailWidth;
        Height = plateHeight + IconSize * (magnify - 1) + 12;
        Plate.Height = plateHeight;
        Plate.Background = settings.DockMaterial == "Dark" ? new SolidColorBrush(Color.FromArgb(0x88, 0x1C, 0x1C, 0x1E)) : new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
        _frame = DockGeometry.Place(request, _scale);
        ApplyFrame();
        if (IsVisible)
        {
            Place();
        }
    }

    private readonly record struct DockItem(AppEntry App, long Handle, bool Bin);

    private List<DockItem> Compose(AppSettings settings)
    {
        var windows = _desktop;
        var pinned = _session.DockEntries();
        var pinnedPaths = new HashSet<string>(pinned.Select(app => app.TargetPath), StringComparer.OrdinalIgnoreCase);
        var list = new List<DockItem>();
        foreach (var app in pinned)
        {
            var match = windows.FirstOrDefault(item => string.Equals(item.Path, app.TargetPath, StringComparison.OrdinalIgnoreCase));
            list.Add(new DockItem(app, match.Path == null ? 0 : match.Handle, false));
        }

        if (settings.ShowRunningApps)
        {
            foreach (var window in windows)
            {
                if (settings.MergeRunningApps && pinnedPaths.Contains(window.Path))
                {
                    continue;
                }

                if (list.Any(item => string.Equals(item.App.TargetPath, window.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                list.Add(new DockItem(new AppEntry(window.Name, window.Path, string.Empty, string.Empty, "Window"), window.Handle, false));
            }
        }

        if (settings.ShowRecycleBin)
        {
            list.Add(new DockItem(new AppEntry("回收站", "shell:RecycleBinFolder", string.Empty, string.Empty, "Recycle"), 0, true));
        }

        return list;
    }

    private void LayoutTail(AppSettings settings, double plateHeight)
    {
        Tail.Visibility = Visibility.Visible;
        Tail.Height = plateHeight;
        TailLine.Height = Math.Max(16, IconSize * 0.45);
        Chevron.Visibility = Visibility.Visible;
        ClockBlock.Visibility = settings.ShowDockClock ? Visibility.Visible : Visibility.Collapsed;
        UpdateClock();
    }

    private void UpdateClock()
    {
        if (DockTime == null)
        {
            return;
        }

        var now = DateTime.Now;
        DockTime.Text = now.ToString("HH:mm");
        DockDate.Text = now.ToString("yyyy/M/d");
    }

    private void ApplyZOrder()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (_withLaunchpad || _cornerReveal || !_session.Settings.KeepDockOnDesktop)
        {
            WindowChrome.KeepAbove(hwnd);
            return;
        }

        WindowChrome.LetCover(hwnd);
    }

    private void OnChevron(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_tray != null)
        {
            _tray.Close();
            _tray = null;
            return;
        }

        var origin = Chevron.PointToScreen(new Point(Chevron.ActualWidth / 2, 0));
        var dpi = VisualTreeHelper.GetDpi(this);
        var x = origin.X / dpi.DpiScaleX;
        var y = origin.Y / dpi.DpiScaleY;
        _tray = new TrayPanelWindow(_session, _icons);
        _tray.Closed += (_, _) => _tray = null;
        _tray.Show();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (_tray == null)
            {
                return;
            }

            _tray.Left = x - _tray.ActualWidth / 2;
            _tray.Top = y - _tray.ActualHeight - 8;
        }));
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(LibrarySession.PinDragFormat))
        {
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Move : DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void OnDropFiles(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(LibrarySession.PinDragFormat) is string path)
        {
            var app = _session.Apps.FirstOrDefault(item => string.Equals(item.TargetPath, path, StringComparison.OrdinalIgnoreCase))
                ?? _entries.FirstOrDefault(item => string.Equals(item.TargetPath, path, StringComparison.OrdinalIgnoreCase));
            if (app != null)
            {
                _session.Pin(app);
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        if (!_session.Settings.ShowRecycleBin || e.Data.GetData(DataFormats.FileDrop) is not string[] files)
        {
            return;
        }

        foreach (var file in files)
        {
            RecycleBin.MoveToBin(file);
        }
    }

    private FrameworkElement CreateVisual(AppEntry entry)
    {
        var size = IconSize;
        if (entry.TargetPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            var glyph = new TextBlock
            {
                Text = "\uE74D",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = size * 0.46,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            var bin = new Grid
            {
                Width = size,
                Height = size,
                ToolTip = entry.Name,
                RenderTransformOrigin = new Point(0.5, 1),
                Background = Brushes.Transparent
            };
            bin.Children.Add(glyph);
            var binTransform = new ScaleTransform(1, 1);
            bin.RenderTransform = binTransform;
            _images.Add(new Image());
            _dots.Add(new Ellipse { Visibility = Visibility.Collapsed });
            _transforms.Add(binTransform);
            return bin;
        }

        var image = new Image
        {
            Width = size - 8,
            Height = size - 8,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        if (_icons.TryCached(entry.TargetPath, out var png))
        {
            image.Source = IconBroker.LoadImage(png);
        }
        else
        {
            _icons.Request(entry.TargetPath);
        }

        var dot = new Ellipse
        {
            Width = 4,
            Height = 4,
            Fill = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 1),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        var grid = new Grid
        {
            Width = size,
            Height = size,
            ToolTip = entry.Name,
            RenderTransformOrigin = new Point(0.5, 1),
            Background = Brushes.Transparent
        };
        grid.Children.Add(image);
        grid.Children.Add(dot);
        var transform = new ScaleTransform(1, 1);
        grid.RenderTransform = transform;
        _images.Add(image);
        _dots.Add(dot);
        _transforms.Add(transform);
        return grid;
    }

    public void SetRunning(IReadOnlyCollection<string> processNames)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(_entries[i].TargetPath);
            _dots[i].Visibility = processNames.Contains(name) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private DockRequest Request(double pointerX)
    {
        return new DockRequest(
            _entries.Count,
            IconSize,
            IconGap,
            DockPadX,
            DockPadY,
            IconGrow,
            pointerX,
            _session.Settings.DockMagnify);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressPoint = e.GetPosition(this);
        _pressIndex = Hit(e.GetPosition(Surface));
        _suppressClick = false;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _pressIndex >= 0 && !_suppressClick)
        {
            var moved = e.GetPosition(this) - _pressPoint;
            if (Math.Abs(moved.X) + Math.Abs(moved.Y) > 14 && _pressIndex < _bins.Count && !_bins[_pressIndex])
            {
                var entry = _entries[_pressIndex];
                _pressIndex = -1;
                _suppressClick = true;
                HidePreview();
                var effect = DragDrop.DoDragDrop(this, new DataObject(LibrarySession.PinDragFormat, entry.TargetPath), DragDropEffects.Move);
                if (effect == DragDropEffects.None)
                {
                    _session.Unpin(entry);
                }

                return;
            }
        }

        var index = Hit(e.GetPosition(Surface));
        if (index >= 0)
        {
            if (_hoverIndex != index)
            {
                _hoverIndex = index;
                _hoverSince = DateTime.UtcNow;
                HidePreview();
            }
            else if ((DateTime.UtcNow - _hoverSince).TotalMilliseconds > 280)
            {
                ShowPreview(index);
            }
        }
        else
        {
            _hoverIndex = -1;
            HidePreview();
        }

        EnsureTick();
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        _hoverIndex = -1;
        HidePreview();
        if (_tray != null)
        {
            EnsureTick();
            return;
        }

        if (_cornerReveal && !_session.Settings.KeepDockOnDesktop)
        {
            _cornerReveal = false;
            Hide();
            return;
        }

        _cornerReveal = false;
        ApplyZOrder();
        EnsureTick();
    }

    private void ShowPreview(int index)
    {
        if (index < 0 || index >= _entries.Count || _preview.IsVisible)
        {
            return;
        }

        var source = _hwnds[index] != 0 ? new IntPtr(_hwnds[index]) : LivePreview.FindMainWindow(_entries[index].TargetPath);
        if (source == IntPtr.Zero)
        {
            return;
        }

        var pose = _frame.Poses[index];
        var origin = Surface.PointToScreen(new Point(pose.Left, pose.Top));
        var dpi = VisualTreeHelper.GetDpi(this);
        _preview.Left = origin.X / dpi.DpiScaleX;
        _preview.Top = origin.Y / dpi.DpiScaleY - _preview.Height - 12;
        _preview.Show();
        var hwnd = new WindowInteropHelper(_preview).EnsureHandle();
        LivePreview.Close(_thumbnail);
        _thumbnail = LivePreview.Register(hwnd, source);
        LivePreview.Update(_thumbnail, (int)(_preview.Width * dpi.DpiScaleX), (int)(_preview.Height * dpi.DpiScaleY));
    }

    private void HidePreview()
    {
        LivePreview.Close(_thumbnail);
        _thumbnail = IntPtr.Zero;
        if (_preview.IsVisible)
        {
            _preview.Hide();
        }
    }

    private void EnsureTick()
    {
        if (_ticking || _entries.Count == 0)
        {
            return;
        }

        _ticking = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private TimeSpan _lastRender;

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_entries.Count == 0)
        {
            StopTick();
            return;
        }

        var now = e is RenderingEventArgs args ? args.RenderingTime : TimeSpan.Zero;
        var dt = _lastRender == TimeSpan.Zero ? 1d / 60 : (now - _lastRender).TotalSeconds;
        _lastRender = now;
        if (dt <= 0 || dt > 0.05)
        {
            dt = 1d / 60;
        }

        var pointer = double.NaN;
        var hovering = IsMouseOver;
        if (hovering)
        {
            pointer = Mouse.GetPosition(Surface).X;
        }

        var request = Request(pointer);
        var target = DockGeometry.TargetScales(request);
        var settled = !hovering;
        for (var i = 0; i < target.Length; i++)
        {
            var velocity = _velocity[i];
            _scale[i] = DockMotion.Follow(_scale[i], target[i], ref velocity, dt, 14);
            _velocity[i] = velocity;
            if (Math.Abs(_scale[i] - target[i]) > 0.004 || Math.Abs(velocity) > 0.01)
            {
                settled = false;
            }
        }

        _frame = DockGeometry.Place(request, _scale);
        ApplyFrame();
        if (settled)
        {
            _lastRender = TimeSpan.Zero;
            StopTick();
        }
    }

    private void StopTick()
    {
        if (!_ticking)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        _ticking = false;
    }

    private void ApplyFrame()
    {
        for (var i = 0; i < _frame.Poses.Count && i < _transforms.Count; i++)
        {
            var pose = _frame.Poses[i];
            var element = (FrameworkElement)Surface.Children[i];
            var plateTop = Height - Plate.Height;
            Canvas.SetLeft(element, pose.Left + pose.Size / 2 - element.Width / 2);
            Canvas.SetTop(element, plateTop + DockPadY);
            _transforms[i].ScaleX = pose.Scale;
            _transforms[i].ScaleY = pose.Scale;
        }
    }

    private void Place()
    {
        var area = MonitorLayout.WorkAreaFromCursor();
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = area.Left / dpi.DpiScaleX + (area.Width / dpi.DpiScaleX - Width) / 2;
        Top = area.Top / dpi.DpiScaleY + area.Height / dpi.DpiScaleY - Height - _session.Settings.DockBottomGap;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_suppressClick)
        {
            _suppressClick = false;
            _pressIndex = -1;
            return;
        }

        var index = Hit(e.GetPosition(Surface));
        if (index < 0)
        {
            return;
        }

        if (_session.Settings.DockBounce && index < _scale.Length)
        {
            _scale[index] = Math.Min(1.35, IconGrow);
            EnsureTick();
        }

        if (_bins[index])
        {
            RecycleBin.Open();
            return;
        }

        if (_hwnds[index] != 0)
        {
            DesktopWindows.Activate(_hwnds[index]);
            return;
        }

        LaunchRequested?.Invoke(_entries[index]);
    }

    private void OnBackgroundMenu(object sender, MouseButtonEventArgs e)
    {
        var index = Hit(e.GetPosition(Surface));
        var menu = new ContextMenu();
        if (index >= 0 && _bins[index])
        {
            menu.Items.Add(Item("打开回收站", RecycleBin.Open));
            menu.Items.Add(Item("清空回收站", RecycleBin.Empty));
        }
        else if (index >= 0)
        {
            var entry = _entries[index];
            var handle = _hwnds[index];
            menu.Items.Add(Item("打开", () =>
            {
                if (handle != 0)
                {
                    DesktopWindows.Activate(handle);
                }
                else
                {
                    LaunchRequested?.Invoke(entry);
                }
            }));
            if (_session.IsPinned(entry))
            {
                menu.Items.Add(Item("从 Dock 移除", () => _session.TogglePin(entry)));
            }
            else if (!entry.TargetPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                menu.Items.Add(Item("固定到 Dock", () => _session.TogglePin(entry)));
            }
        }

        menu.Items.Add(Item("重新扫描", () => RescanRequested?.Invoke()));
        menu.Items.Add(Item("退出", () => ExitRequested?.Invoke()));
        menu.IsOpen = true;
        e.Handled = true;
    }

    private int Hit(Point point)
    {
        var best = -1;
        var bestScale = -1d;
        for (var i = 0; i < _frame.Poses.Count; i++)
        {
            var pose = _frame.Poses[i];
            var top = Height - Plate.Height + DockPadY + IconSize * (1 - pose.Scale);
            var bottom = Height - Plate.Height + DockPadY + IconSize;
            if (point.X < pose.Left || point.X > pose.Left + pose.Size || point.Y < top || point.Y > bottom)
            {
                continue;
            }

            if (pose.Scale >= bestScale)
            {
                best = i;
                bestScale = pose.Scale;
            }
        }

        return best;
    }

    private void OnIconReady(string path, string png)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var image = IconBroker.LoadImage(png);
            if (image == null)
            {
                return;
            }

            for (var i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i].TargetPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    _images[i].Source = image;
                }
            }
        }));
    }

    private static MenuItem Item(string title, Action action)
    {
        var item = new MenuItem { Header = title };
        item.Click += (_, _) => action();
        return item;
    }
}
