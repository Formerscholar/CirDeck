using System.Windows;
using CirDeck.Core;
using Microsoft.Win32;

namespace CirDeck.App;

public partial class SettingsWindow : Window
{
    private readonly LibrarySession _session;

    public SettingsWindow(LibrarySession session)
    {
        _session = session;
        InitializeComponent();
        StartupBox.IsChecked = session.Settings.LaunchAtStartup;
        CornerBox.IsChecked = session.Settings.EnableHotCorner;
        ClockBox.IsChecked = session.Settings.ShowClock;
        MonitorBox.IsChecked = session.Settings.ShowSystemMonitor;
        MusicBox.IsChecked = session.Settings.ShowMusicSpectrum;
        SteamBox.IsChecked = session.Settings.ScanSteam;
        CornerCombo.ItemsSource = new[] { "TopLeft", "TopRight", "BottomLeft", "BottomRight" };
        CornerCombo.SelectedItem = session.Settings.HotCorner;
        ReloadLayouts();
        ReloadHidden();
        StartupBox.Checked += (_, _) => SaveFlags();
        StartupBox.Unchecked += (_, _) => SaveFlags();
        CornerBox.Checked += (_, _) => SaveFlags();
        CornerBox.Unchecked += (_, _) => SaveFlags();
        ClockBox.Checked += (_, _) => SaveFlags();
        ClockBox.Unchecked += (_, _) => SaveFlags();
        MonitorBox.Checked += (_, _) => SaveFlags();
        MonitorBox.Unchecked += (_, _) => SaveFlags();
        MusicBox.Checked += (_, _) => SaveFlags();
        MusicBox.Unchecked += (_, _) => SaveFlags();
        SteamBox.Checked += (_, _) => SaveFlags();
        SteamBox.Unchecked += (_, _) => SaveFlags();
        CornerCombo.SelectionChanged += (_, _) => SaveFlags();
    }

    private void SaveFlags()
    {
        _session.Settings.LaunchAtStartup = StartupBox.IsChecked == true;
        _session.Settings.EnableHotCorner = CornerBox.IsChecked == true;
        _session.Settings.ShowClock = ClockBox.IsChecked == true;
        _session.Settings.ShowSystemMonitor = MonitorBox.IsChecked == true;
        _session.Settings.ShowMusicSpectrum = MusicBox.IsChecked == true;
        _session.Settings.ScanSteam = SteamBox.IsChecked == true;
        if (CornerCombo.SelectedItem is string corner)
        {
            _session.Settings.HotCorner = corner;
        }

        _session.CommitSettings();
    }

    private void ReloadLayouts()
    {
        LayoutCombo.ItemsSource = _session.LayoutChoices().Select(item => item.Name + "  ·  " + item.Id).ToList();
        var index = _session.LayoutChoices().ToList().FindIndex(item => item.Id == _session.Settings.ActiveLayoutId);
        if (index >= 0)
        {
            LayoutCombo.SelectedIndex = index;
        }
    }

    private void ReloadHidden()
    {
        HiddenList.ItemsSource = _session.HiddenApps().Select(app => app.Name + "  ·  " + app.TargetPath).ToList();
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
