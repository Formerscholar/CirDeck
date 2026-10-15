using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CirDeck.Core;
using CirDeck.Interop;

namespace CirDeck.App;

public partial class TrayPanelWindow : Window
{
    private readonly LibrarySession _session;
    private readonly IconBroker _icons;
    private bool _ready;
    private bool _muted;
    private bool _menuOpen;

    public TrayPanelWindow(LibrarySession session, IconBroker icons)
    {
        _session = session;
        _icons = icons;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowChrome.HideFromSwitcher(new WindowInteropHelper(this).Handle);
        _icons.Ready += OnIconReady;
        TaskbarBox.IsChecked = session.Settings.HideTaskbar;
        RefreshIcons();
        RefreshStatus();
        _ready = true;
    }

    private void RefreshIcons()
    {
        Icons.Children.Clear();
        IReadOnlyList<TrayIcon> icons;
        try
        {
            icons = TrayIcons.List();
        }
        catch (Exception)
        {
            icons = Array.Empty<TrayIcon>();
        }

        var hidden = new HashSet<string>(_session.Settings.TrayHidden ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        var visible = icons.Where(icon => !hidden.Contains(icon.Name)).OrderBy(icon => icon.Name).ToList();
        EmptyText.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var icon in visible)
        {
            Icons.Children.Add(CreateIcon(icon));
        }
    }

    private Button CreateIcon(TrayIcon icon)
    {
        var image = new Image { Width = 28, Height = 28, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        if (!string.IsNullOrWhiteSpace(icon.Path) && _icons.TryCached(icon.Path, out var png))
        {
            image.Source = IconBroker.LoadImage(png);
        }
        else if (!string.IsNullOrWhiteSpace(icon.Path))
        {
            image.Tag = icon.Path;
            _icons.Request(icon.Path);
        }

        var button = new Button
        {
            Width = 48,
            Height = 48,
            Margin = new Thickness(2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = icon.Name,
            Focusable = false,
            Content = image,
            Tag = icon
        };
        button.Click += (_, _) => icon.Click(false);
        button.MouseRightButtonUp += (_, args) =>
        {
            var menu = new ContextMenu();
            var open = new MenuItem { Header = "打开" };
            open.Click += (_, _) => icon.Click(false);
            var hide = new MenuItem { Header = "从托盘面板隐藏" };
            hide.Click += (_, _) =>
            {
                _session.Settings.TrayHidden ??= new List<string>();
                if (!_session.Settings.TrayHidden.Any(item => string.Equals(item, icon.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    _session.Settings.TrayHidden.Add(icon.Name);
                    _session.CommitSettings();
                }

                RefreshIcons();
            };
            menu.Items.Add(open);
            menu.Items.Add(hide);
            menu.IsOpen = true;
            args.Handled = true;
        };
        return button;
    }

    private void RefreshStatus()
    {
        InputButton.Content = SystemStatus.InputLabel();
        var online = SystemStatus.NetworkUp();
        NetButton.Content = new TextBlock
        {
            Text = SystemStatus.WifiUp() ? "\uE701" : online ? "\uE839" : "\uE870",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 16,
            Foreground = online ? Brushes.Black : new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (!SystemStatus.TryGetVolume(out var level, out _muted))
        {
            return;
        }

        VolumeSlider.Value = level * 100;
        VolumeText.Text = ((int)Math.Round(level * 100)).ToString() + "%";
        UpdateSpeaker();
    }

    private void UpdateSpeaker()
    {
        MuteButton.Content = new TextBlock
        {
            Text = _muted || VolumeSlider.Value < 1 ? "\uE198" : "\uE767",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 15,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        RefreshIcons();
        RefreshStatus();
    }

    private void OnTaskbar(object sender, RoutedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        _session.Settings.HideTaskbar = TaskbarBox.IsChecked == true;
        _session.CommitSettings();
        TaskbarGate.Apply(_session.Settings.HideTaskbar);
    }

    private void OnInput(object sender, RoutedEventArgs e)
    {
        SystemStatus.NextInput();
        Dispatcher.BeginInvoke(new Action(() => InputButton.Content = SystemStatus.InputLabel()));
    }

    private void OnNetwork(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo { FileName = "ms-settings:network", UseShellExecute = true });
    }

    private void OnMute(object sender, RoutedEventArgs e)
    {
        _muted = !_muted;
        SystemStatus.SetVolume(_muted ? 0 : Math.Max(0.05f, (float)VolumeSlider.Value / 100f));
        if (_muted)
        {
            VolumeSlider.Value = 0;
        }

        UpdateSpeaker();
    }

    private void OnVolume(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || VolumeText == null)
        {
            return;
        }

        VolumeText.Text = ((int)Math.Round(VolumeSlider.Value)).ToString() + "%";
        _muted = VolumeSlider.Value < 1;
        SystemStatus.SetVolume((float)VolumeSlider.Value / 100f);
        UpdateSpeaker();
    }

    private void OnPower(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("睡眠", SystemStatus.Sleep));
        menu.Items.Add(Item("重启", () => Process.Start(new ProcessStartInfo { FileName = "shutdown", Arguments = "/r /t 0", UseShellExecute = true })));
        menu.Items.Add(Item("关机", () => Process.Start(new ProcessStartInfo { FileName = "shutdown", Arguments = "/s /t 0", UseShellExecute = true })));
        _menuOpen = true;
        menu.Closed += (_, _) => _menuOpen = false;
        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
    }

    private void OnDeactivated(object sender, EventArgs e)
    {
        if (_menuOpen)
        {
            return;
        }

        Close();
    }

    private void OnIconReady(string path, string png)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            foreach (var child in Icons.Children)
            {
                if (child is Button button && button.Content is Image image && image.Tag is string source && string.Equals(source, path, StringComparison.OrdinalIgnoreCase))
                {
                    image.Source = IconBroker.LoadImage(png);
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
