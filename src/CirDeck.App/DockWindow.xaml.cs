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
        if (_entries.Count == 0)
        {
            Hide();
            return;
        }

        Show();
        Place();
        WindowChrome.KeepAbove(new WindowInteropHelper(this).Handle);
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

    private const double DockIcon = 56;
    private const double DockGap = 12;
    private const double DockPadX = 18;
    private const double DockPadY = 12;
    private const double DockGrow = 1.46;

    private void Rebuild()
    {
        var entries = _session.DockEntries();
        var signature = string.Join("|", entries.Select(entry => entry.TargetPath));
        if (signature == _signature && IsVisible)
        {
            return;
        }

        _signature = signature;
        _entries.Clear();
        _entries.AddRange(entries);
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
        }

        var settings = _session.Settings;
        var magnify = settings.DockMagnify ? DockGrow : 1;
        var request = new DockRequest(_entries.Count, DockIcon, DockGap, DockPadX, DockPadY, magnify, double.NaN, settings.DockMagnify);
        var plateHeight = DockPadY * 2 + DockIcon;
        Width = Math.Max(160, DockGeometry.ReserveWidth(_entries.Count, DockIcon, DockGap, DockPadX, magnify));
        Height = plateHeight + DockIcon * (magnify - 1) + 12;
        Plate.Height = plateHeight;
        _frame = DockGeometry.Place(request, _scale);
        ApplyFrame();
        if (IsVisible)
        {
            Place();
        }
    }

    private FrameworkElement CreateVisual(AppEntry entry)
    {
        var size = 56d;
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
            56,
            12,
            18,
            12,
            _session.Settings.DockMagnify ? 1.46 : 1,
            pointerX,
            _session.Settings.DockMagnify);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
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
        EnsureTick();
    }

    private void ShowPreview(int index)
    {
        if (index < 0 || index >= _entries.Count || _preview.IsVisible)
        {
            return;
        }

        var source = LivePreview.FindMainWindow(_entries[index].TargetPath);
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
        Top = area.Top / dpi.DpiScaleY + area.Height / dpi.DpiScaleY - Height - 18;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        var index = Hit(e.GetPosition(Surface));
        if (index >= 0)
        {
            LaunchRequested?.Invoke(_entries[index]);
        }
    }

    private void OnBackgroundMenu(object sender, MouseButtonEventArgs e)
    {
        var index = Hit(e.GetPosition(Surface));
        var menu = new ContextMenu();
        if (index >= 0)
        {
            var entry = _entries[index];
            menu.Items.Add(Item("打开", () => LaunchRequested?.Invoke(entry)));
            menu.Items.Add(Item("从 Dock 移除", () => _session.TogglePin(entry)));
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
            var top = Height - Plate.Height + DockPadY + DockIcon * (1 - pose.Scale);
            var bottom = Height - Plate.Height + DockPadY + DockIcon;
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
