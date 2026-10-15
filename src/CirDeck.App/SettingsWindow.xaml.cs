using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using CirDeck.Core;
using CirDeck.Interop;
using Microsoft.Win32;

namespace CirDeck.App;

public partial class SettingsWindow : Window
{
    private readonly LibrarySession _session;
    private bool _ready;
    private sealed record Choice(string Value, string Label);

    private static readonly Choice[] Screens =
    {
        new("Cursor", "跟随鼠标所在屏幕"),
        new("Primary", "主显示器")
    };

    private static readonly Choice[] Corners =
    {
        new("TopLeft", "左上角"),
        new("TopRight", "右上角"),
        new("BottomLeft", "左下角"),
        new("BottomRight", "右下角")
    };

    private static readonly Choice[] DockCorners =
    {
        new("TopLeft", "左上"),
        new("TopRight", "右上"),
        new("BottomLeft", "左下"),
        new("BottomRight", "右下")
    };

    private static readonly Choice[] Materials =
    {
        new("Glass", "轻透玻璃"),
        new("Dark", "深色")
    };

    public SettingsWindow(LibrarySession session)
    {
        _session = session;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowChrome.HideFromSwitcher(new WindowInteropHelper(this).Handle);
        var settings = session.Settings;
        StartupBox.IsChecked = settings.LaunchAtStartup;
        OpenAtStartupBox.IsChecked = settings.OpenLaunchpadAtStartup;
        CloseAfterBox.IsChecked = settings.CloseAfterLaunch;
        ShowDockBox.IsChecked = settings.ShowDock;
        MagnifyBox.IsChecked = settings.DockMagnify;
        RunningBox.IsChecked = settings.ShowRunningIndicators;
        ClockBox.IsChecked = settings.ShowClock;
        MonitorBox.IsChecked = settings.ShowSystemMonitor;
        MusicBox.IsChecked = settings.ShowMusicSpectrum;
        SteamBox.IsChecked = settings.ScanSteam;
        CornerBox.IsChecked = settings.EnableHotCorner;
        DesktopBox.IsChecked = settings.KeepOnDesktop;
        AutoUpdateBox.IsChecked = settings.AutoCheckUpdates;
        ScreenCombo.ItemsSource = Screens;
        ScreenCombo.SelectedValue = settings.DisplayScreen;
        CornerCombo.ItemsSource = Corners;
        CornerCombo.SelectedValue = settings.HotCorner;
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = "当前版本 " + (version?.ToString(3) ?? "1.0.0");
        CheckText.Text = string.IsNullOrWhiteSpace(settings.LastUpdateCheck) ? "尚未检查更新" : "上次检查 " + settings.LastUpdateCheck;
        ResidentBox.IsChecked = settings.KeepDockOnDesktop;
        RunningAppsBox.IsChecked = settings.ShowRunningApps;
        MergeBox.IsChecked = settings.MergeRunningApps;
        DockClockBox.IsChecked = settings.ShowDockClock;
        RecycleBox.IsChecked = settings.ShowRecycleBin;
        HideTaskbarBox.IsChecked = settings.HideTaskbar;
        DockCornerBox.IsChecked = settings.EnableDockHotCorner;
        LaunchCornerBox.IsChecked = settings.EnableHotCorner;
        FullscreenPauseBox.IsChecked = settings.PauseHotCornerWhenFullscreen;
        BounceBox.IsChecked = settings.DockBounce;
        MaterialCombo.ItemsSource = Materials;
        MaterialCombo.SelectedValue = settings.DockMaterial;
        DockCornerCombo.ItemsSource = DockCorners;
        DockCornerCombo.SelectedValue = settings.DockHotCorner;
        LaunchCornerCombo.ItemsSource = DockCorners;
        LaunchCornerCombo.SelectedValue = settings.HotCorner;
        IconSizeSlider.Value = settings.DockIconSize;
        GapSlider.Value = settings.DockSpacing;
        GrowSlider.Value = settings.DockMagnifyScale;
        BottomSlider.Value = settings.DockBottomGap;
        WriteDockLabels();
        ReloadLayouts();
        ReloadHidden();
        ShowPage(NavGeneral);
        _ready = true;
        foreach (var box in new[] { StartupBox, OpenAtStartupBox, CloseAfterBox, ShowDockBox, MagnifyBox, RunningBox, ClockBox, MonitorBox, MusicBox, SteamBox, CornerBox, DesktopBox, AutoUpdateBox, ResidentBox, RunningAppsBox, MergeBox, DockClockBox, RecycleBox, HideTaskbarBox, DockCornerBox, LaunchCornerBox, FullscreenPauseBox, BounceBox })
        {
            box.Checked += (_, _) => SaveFlags();
            box.Unchecked += (_, _) => SaveFlags();
        }

        CornerCombo.SelectionChanged += (_, _) => SaveFlags();
        ScreenCombo.SelectionChanged += (_, _) => SaveFlags();
        MaterialCombo.SelectionChanged += (_, _) => SaveFlags();
        DockCornerCombo.SelectionChanged += (_, _) => SaveFlags();
        LaunchCornerCombo.SelectionChanged += (_, _) => SaveFlags();
    }

    private void OnDockMetric(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        WriteDockLabels();
        SaveFlags();
    }

    private void WriteDockLabels()
    {
        if (IconSizeText == null)
        {
            return;
        }

        IconSizeText.Text = ((int)Math.Round(IconSizeSlider.Value)).ToString() + " pt";
        GapText.Text = ((int)Math.Round(GapSlider.Value)).ToString() + " pt";
        GrowText.Text = GrowSlider.Value.ToString("0.00") + "x";
        BottomText.Text = ((int)Math.Round(BottomSlider.Value)).ToString() + " pt";
    }

    private void OnNav(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            ShowPage(button);
        }
    }

    private void ShowPage(Button selected)
    {
        foreach (var button in new[] { NavGeneral, NavDock, NavLayout, NavWidgets, NavMotion, NavApps })
        {
            button.Background = Brushes.Transparent;
            button.Foreground = new SolidColorBrush(Color.FromRgb(242, 242, 247));
        }

        selected.Background = new SolidColorBrush(Color.FromRgb(10, 132, 255));
        selected.Foreground = Brushes.White;
        PageGeneral.Visibility = selected == NavGeneral ? Visibility.Visible : Visibility.Collapsed;
        PageDock.Visibility = selected == NavDock ? Visibility.Visible : Visibility.Collapsed;
        PageLayout.Visibility = selected == NavLayout ? Visibility.Visible : Visibility.Collapsed;
        PageWidgets.Visibility = selected == NavWidgets ? Visibility.Visible : Visibility.Collapsed;
        PageMotion.Visibility = selected == NavMotion ? Visibility.Visible : Visibility.Collapsed;
        PageApps.Visibility = selected == NavApps ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = selected.Tag as string ?? "通用";
        PageSubtitle.Text = selected.Name switch
        {
            nameof(NavGeneral) => "开机自启、全局快捷键、多显示器行为与检查更新",
            nameof(NavDock) => "桌面 Dock、触发角与图标外观",
            nameof(NavLayout) => "切换、保存、导入和导出布局",
            nameof(NavWidgets) => "时钟、系统占用、频谱和 Steam",
            nameof(NavMotion) => "用屏幕热角唤出启动台",
            nameof(NavApps) => "恢复已经隐藏的应用",
            _ => "更改后会自动保存"
        };
    }

    private void SaveFlags()
    {
        if (!_ready)
        {
            return;
        }

        var settings = _session.Settings;
        settings.LaunchAtStartup = StartupBox.IsChecked == true;
        settings.OpenLaunchpadAtStartup = OpenAtStartupBox.IsChecked == true;
        settings.CloseAfterLaunch = CloseAfterBox.IsChecked == true;
        settings.ShowDock = ShowDockBox.IsChecked == true;
        settings.DockMagnify = MagnifyBox.IsChecked == true;
        settings.ShowRunningIndicators = RunningBox.IsChecked == true;
        settings.KeepDockOnDesktop = ResidentBox.IsChecked == true;
        settings.ShowRunningApps = RunningAppsBox.IsChecked == true;
        settings.MergeRunningApps = MergeBox.IsChecked == true;
        settings.ShowDockClock = DockClockBox.IsChecked == true;
        settings.ShowRecycleBin = RecycleBox.IsChecked == true;
        settings.HideTaskbar = HideTaskbarBox.IsChecked == true;
        settings.EnableDockHotCorner = DockCornerBox.IsChecked == true;
        settings.EnableHotCorner = LaunchCornerBox.IsChecked == true || CornerBox.IsChecked == true;
        settings.PauseHotCornerWhenFullscreen = FullscreenPauseBox.IsChecked == true;
        settings.DockBounce = BounceBox.IsChecked == true;
        settings.DockIconSize = IconSizeSlider.Value;
        settings.DockSpacing = GapSlider.Value;
        settings.DockMagnifyScale = GrowSlider.Value;
        settings.DockBottomGap = BottomSlider.Value;
        if (MaterialCombo.SelectedValue is string material)
        {
            settings.DockMaterial = material;
        }

        if (DockCornerCombo.SelectedValue is string dockCorner)
        {
            settings.DockHotCorner = dockCorner;
        }

        if (LaunchCornerCombo.SelectedValue is string launchCorner)
        {
            settings.HotCorner = launchCorner;
        }
        settings.ShowClock = ClockBox.IsChecked == true;
        settings.ShowSystemMonitor = MonitorBox.IsChecked == true;
        settings.ShowMusicSpectrum = MusicBox.IsChecked == true;
        settings.ScanSteam = SteamBox.IsChecked == true;
        settings.KeepOnDesktop = DesktopBox.IsChecked == true;
        settings.AutoCheckUpdates = AutoUpdateBox.IsChecked == true;
        if (ScreenCombo.SelectedValue is string screen)
        {
            settings.DisplayScreen = screen;
        }

        _session.CommitSettings();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        var fresh = new AppSettings();
        var settings = _session.Settings;
        settings.LaunchAtStartup = fresh.LaunchAtStartup;
        settings.OpenLaunchpadAtStartup = fresh.OpenLaunchpadAtStartup;
        settings.CloseAfterLaunch = fresh.CloseAfterLaunch;
        settings.ShowDock = fresh.ShowDock;
        settings.DockMagnify = fresh.DockMagnify;
        settings.ShowRunningIndicators = fresh.ShowRunningIndicators;
        settings.KeepDockOnDesktop = fresh.KeepDockOnDesktop;
        settings.ShowRunningApps = fresh.ShowRunningApps;
        settings.MergeRunningApps = fresh.MergeRunningApps;
        settings.ShowDockClock = fresh.ShowDockClock;
        settings.ShowRecycleBin = fresh.ShowRecycleBin;
        settings.HideTaskbar = fresh.HideTaskbar;
        settings.EnableDockHotCorner = fresh.EnableDockHotCorner;
        settings.DockHotCorner = fresh.DockHotCorner;
        settings.PauseHotCornerWhenFullscreen = fresh.PauseHotCornerWhenFullscreen;
        settings.DockBounce = fresh.DockBounce;
        settings.DockMaterial = fresh.DockMaterial;
        settings.DockIconSize = fresh.DockIconSize;
        settings.DockSpacing = fresh.DockSpacing;
        settings.DockMagnifyScale = fresh.DockMagnifyScale;
        settings.DockBottomGap = fresh.DockBottomGap;
        settings.ShowClock = fresh.ShowClock;
        settings.ShowSystemMonitor = fresh.ShowSystemMonitor;
        settings.ShowMusicSpectrum = fresh.ShowMusicSpectrum;
        settings.ScanSteam = fresh.ScanSteam;
        settings.EnableHotCorner = fresh.EnableHotCorner;
        settings.HotCorner = fresh.HotCorner;
        settings.DisplayScreen = fresh.DisplayScreen;
        settings.KeepOnDesktop = fresh.KeepOnDesktop;
        settings.AutoCheckUpdates = fresh.AutoCheckUpdates;
        settings.Columns = fresh.Columns;
        settings.Rows = fresh.Rows;
        _ready = false;
        StartupBox.IsChecked = settings.LaunchAtStartup;
        OpenAtStartupBox.IsChecked = settings.OpenLaunchpadAtStartup;
        CloseAfterBox.IsChecked = settings.CloseAfterLaunch;
        ShowDockBox.IsChecked = settings.ShowDock;
        MagnifyBox.IsChecked = settings.DockMagnify;
        RunningBox.IsChecked = settings.ShowRunningIndicators;
        ResidentBox.IsChecked = settings.KeepDockOnDesktop;
        RunningAppsBox.IsChecked = settings.ShowRunningApps;
        MergeBox.IsChecked = settings.MergeRunningApps;
        DockClockBox.IsChecked = settings.ShowDockClock;
        RecycleBox.IsChecked = settings.ShowRecycleBin;
        HideTaskbarBox.IsChecked = settings.HideTaskbar;
        DockCornerBox.IsChecked = settings.EnableDockHotCorner;
        LaunchCornerBox.IsChecked = settings.EnableHotCorner;
        FullscreenPauseBox.IsChecked = settings.PauseHotCornerWhenFullscreen;
        BounceBox.IsChecked = settings.DockBounce;
        ClockBox.IsChecked = settings.ShowClock;
        MonitorBox.IsChecked = settings.ShowSystemMonitor;
        MusicBox.IsChecked = settings.ShowMusicSpectrum;
        SteamBox.IsChecked = settings.ScanSteam;
        CornerBox.IsChecked = settings.EnableHotCorner;
        DesktopBox.IsChecked = settings.KeepOnDesktop;
        AutoUpdateBox.IsChecked = settings.AutoCheckUpdates;
        CornerCombo.SelectedValue = settings.HotCorner;
        LaunchCornerCombo.SelectedValue = settings.HotCorner;
        DockCornerCombo.SelectedValue = settings.DockHotCorner;
        MaterialCombo.SelectedValue = settings.DockMaterial;
        IconSizeSlider.Value = settings.DockIconSize;
        GapSlider.Value = settings.DockSpacing;
        GrowSlider.Value = settings.DockMagnifyScale;
        BottomSlider.Value = settings.DockBottomGap;
        WriteDockLabels();
        ScreenCombo.SelectedValue = settings.DisplayScreen;
        _ready = true;
        _session.CommitSettings();
    }

    private void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        var stamp = DateTime.Now.ToString("M-d HH:mm");
        _session.Settings.LastUpdateCheck = stamp;
        _session.CommitSettings();
        CheckText.Text = "上次检查 " + stamp;
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/Formerscholar/CirDeck/releases",
            UseShellExecute = true
        });
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ReloadLayouts()
    {
        var choices = _session.LayoutChoices().ToList();
        LayoutCombo.ItemsSource = choices.Select(item => item.Name).ToList();
        var index = choices.FindIndex(item => item.Id == _session.Settings.ActiveLayoutId);
        if (index >= 0)
        {
            LayoutCombo.SelectedIndex = index;
        }
    }

    private void ReloadHidden()
    {
        HiddenList.ItemsSource = _session.HiddenApps().Select(app => app.Name).ToList();
    }

    private void OnSwitchLayout(object sender, RoutedEventArgs e)
    {
        var choices = _session.LayoutChoices().ToList();
        if (LayoutCombo.SelectedIndex < 0 || LayoutCombo.SelectedIndex >= choices.Count)
        {
            return;
        }

        _session.SwitchLayout(choices[LayoutCombo.SelectedIndex].Id);
        Close();
    }

    private void OnSaveLayout(object sender, RoutedEventArgs e)
    {
        var name = Prompt.Ask("布局名称", "我的布局");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _session.SaveLayout(name);
        ReloadLayouts();
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "CirDeck 布局|*.json", FileName = "cirdeck-layout.json" };
        if (dialog.ShowDialog() == true)
        {
            _session.ExportLayout(dialog.FileName);
        }
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "CirDeck 布局|*.json" };
        if (dialog.ShowDialog() == true)
        {
            _session.ImportLayout(dialog.FileName);
            Close();
        }
    }

    private void OnUnhide(object sender, RoutedEventArgs e)
    {
        var hidden = _session.HiddenApps().ToList();
        if (HiddenList.SelectedIndex < 0 || HiddenList.SelectedIndex >= hidden.Count)
        {
            return;
        }

        _session.Unhide(hidden[HiddenList.SelectedIndex].TargetPath);
        ReloadHidden();
    }
}
