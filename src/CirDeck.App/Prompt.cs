using System.Windows;
using System.Windows.Controls;

namespace CirDeck.App;

internal static class Prompt
{
    public static string? Ask(string title, string initial)
    {
        var box = new TextBox
        {
            Text = initial,
            Margin = new Thickness(16, 12, 16, 0),
            FontSize = 15,
            Padding = new Thickness(8)
        };
        var ok = new Button { Content = "确定", Width = 88, Height = 32, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "取消", Width = 88, Height = 32, IsCancel = true };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16)
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(box);
        var window = new Window
        {
            Title = title,
            Width = 360,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Content = panel,
            Background = System.Windows.Media.Brushes.White,
            Topmost = true
        };
        string? value = null;
        ok.Click += (_, _) =>
        {
            value = box.Text;
            window.DialogResult = true;
        };
        window.ShowDialog();
        return value;
    }
}
