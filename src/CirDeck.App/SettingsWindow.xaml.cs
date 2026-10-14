using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CirDeck.Core;
using Microsoft.Win32;

namespace CirDeck.App;

public partial class SettingsWindow : Window
{
    private readonly LibrarySession _session;
    private bool _ready;

    public SettingsWindow(LibrarySession session)
    {
        _session = session;
        InitializeComponent();
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
        CornerCombo.ItemsSource = new[] { "TopLeft", "TopRight", "BottomLeft", "BottomRight" };
        CornerCombo.SelectedItem = settings.HotCorner;
        ReloadLayouts();
        ReloadHidden();
        ShowPage(NavGeneral);
        _ready = true;
        foreach (var box in new[] { StartupBox, OpenAtStartupBox, CloseAfterBox, ShowDockBox, MagnifyBox, RunningBox, ClockBox, MonitorBox, MusicBox, SteamBox, CornerBox })
        {
            box.Checked += (_, _) => SaveFlags();
            box.Unchecked += (_, _) => SaveFlags();
        }

        CornerCombo.SelectionChanged += (_, _) => SaveFlags();
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

        selected.Background = new SolidColorBrush(Color.FromRgb(47, 128, 255));
        selected.Foreground = Brushes.White;
        PageGeneral.Visibility = selected == NavGeneral ? Visibility.Visible : Visibility.Collapsed;
        PageDock.Visibility = selected == NavDock ? Visibility.Visible : Visibility.Collapsed;
        PageLayout.Visibility = selected == NavLayout ? Visibility.Visible : Visibility.Collapsed;
        PageWidgets.Visibility = selected == NavWidgets ? Visibility.Visible : Visibility.Collapsed;
        PageMotion.Visibility = selected == NavMotion ? Visibility.Visible : Visibility.Collapsed;
        PageApps.Visibility = selected == NavApps ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = selected.Content?.ToString() ?? "通用";
        PageSubtitle.Text = selected == NavGeneral
            ? "开机自启、全局快捷键、多显示器行为"
            : "更改后会自动保存";
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
        settings.ShowClock = ClockBox.IsChecked == true;
        settings.ShowSystemMonitor = MonitorBox.IsChecked == true;
        settings.ShowMusicSpectrum = MusicBox.IsChecked == true;
        settings.ScanSteam = SteamBox.IsChecked == true;
        settings.EnableHotCorner = CornerBox.IsChecked == true;
        if (CornerCombo.SelectedItem is string corner)
        {
            settings.HotCorner = corner;
        }

        _session.CommitSettings();
        SavedText.Text = "已自动保存";
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
        settings.ShowClock = fresh.ShowClock;
        settings.ShowSystemMonitor = fresh.ShowSystemMonitor;
        settings.ShowMusicSpectrum = fresh.ShowMusicSpectrum;
        settings.ScanSteam = fresh.ScanSteam;
        settings.EnableHotCorner = fresh.EnableHotCorner;
        settings.HotCorner = fresh.HotCorner;
        settings.Columns = fresh.Columns;
        settings.Rows = fresh.Rows;
        _ready = false;
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
        CornerCombo.SelectedItem = settings.HotCorner;
        _ready = true;
        _session.CommitSettings();
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
