using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace CirDeck.Interop;

public sealed class TrayIcon
{
    private readonly AutomationElement _element;

    public TrayIcon(string name, string path, AutomationElement element)
    {
        Name = name;
        Path = path;
        _element = element;
    }

    public string Name { get; }

    public string Path { get; }

    public void Click(bool right)
    {
        try
        {
            if (!right && _element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
            {
                ((InvokePattern)pattern).Invoke();
                return;
            }

            if (!_element.TryGetClickablePoint(out var point))
            {
                return;
            }

            TrayIcons.ClickAt((int)point.X, (int)point.Y, right);
        }
        catch (ElementNotAvailableException)
        {
        }
    }
}

public static class TrayIcons
{
    internal static void ClickAt(int x, int y, bool right)
    {
        SetCursorPos(x, y);
        var down = right ? 0x0008u : 0x0002u;
        var up = right ? 0x0010u : 0x0004u;
        mouse_event(down, 0, 0, 0, UIntPtr.Zero);
        mouse_event(up, 0, 0, 0, UIntPtr.Zero);
    }

    public static IReadOnlyList<TrayIcon> List()
    {
        var icons = new List<TrayIcon>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var programs = Programs();
        Collect(FindWindow("TopLevelWindowForOverflowXamlIsland", null), icons, seen, programs, true);
        Collect(FindWindow("Shell_TrayWnd", null), icons, seen, programs, false);
        return icons;
    }

    private static void Collect(IntPtr root, List<TrayIcon> icons, HashSet<string> seen, Dictionary<string, string> programs, bool overflow)
    {
        if (root == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AddButtons(AutomationElement.FromHandle(root), icons, seen, programs, overflow);
        }
        catch (ElementNotAvailableException)
        {
        }

        for (var child = FindWindowEx(root, IntPtr.Zero, null, null); child != IntPtr.Zero; child = FindWindowEx(root, child, null, null))
        {
            Collect(child, icons, seen, programs, overflow);
        }
    }

    private static void AddButtons(AutomationElement element, List<TrayIcon> icons, HashSet<string> seen, Dictionary<string, string> programs, bool overflow)
    {
        AutomationElementCollection buttons;
        try
        {
            buttons = element.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        }
        catch (ElementNotAvailableException)
        {
            return;
        }

        var tray = overflow;
        foreach (AutomationElement button in buttons)
        {
            string name;
            try
            {
                name = button.Current.Name?.Trim() ?? "";
            }
            catch (ElementNotAvailableException)
            {
                continue;
            }

            if (!overflow && name.Contains("隐藏的图标", StringComparison.Ordinal))
            {
                tray = true;
                continue;
            }

            if (!tray || !Keep(name, overflow))
            {
                continue;
            }

            if (!seen.Add(name))
            {
                continue;
            }

            icons.Add(new TrayIcon(name, Resolve(name, programs), button));
            if (icons.Count >= 40)
            {
                return;
            }
        }
    }

    private static bool Keep(string name, bool overflow)
    {
        if (name.Length < 2)
        {
            return false;
        }

        if (!overflow && (name.Contains("已固定", StringComparison.Ordinal) || name.Contains("运行窗口", StringComparison.Ordinal)))
        {
            return false;
        }

        return name is not ("开始" or "搜索" or "任务视图" or "显示桌面" or "时钟" or "系统时钟" or "日期和时间" or "网络" or "音量" or "扬声器" or "操作中心" or "通知中心" or "电池" or "输入指示器");
    }

    private static string Resolve(string name, Dictionary<string, string> programs)
    {
        if (programs.TryGetValue(name, out var exact))
        {
            return exact;
        }

        string? best = null;
        var bestLength = 0;
        foreach (var pair in programs)
        {
            if (pair.Key.Length < 3)
            {
                continue;
            }

            if (name.Contains(pair.Key, StringComparison.OrdinalIgnoreCase) && pair.Key.Length > bestLength)
            {
                best = pair.Value;
                bestLength = pair.Key.Length;
            }
        }

        return best ?? "";
    }

    private static Dictionary<string, string> Programs()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in DesktopWindows.List())
        {
            AddProgram(map, Path.GetFileNameWithoutExtension(window.Path), window.Path);
            AddProgram(map, window.Name.Trim(), window.Path);
        }

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var path = ImagePath(process.Id);
                if (path.Length > 0)
                {
                    AddProgram(map, Path.GetFileNameWithoutExtension(path), path);
                }
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return map;
    }

    private static void AddProgram(Dictionary<string, string> map, string name, string path)
    {
        if (name.Length == 0 || path.Length == 0 || map.ContainsKey(name))
        {
            return;
        }

        map[name] = path;
    }

    private static string ImagePath(int processId)
    {
        var process = OpenProcess(0x1000, false, (uint)processId);
        if (process == IntPtr.Zero)
        {
            return "";
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? buffer.ToString() : "";
        }
        finally
        {
            CloseHandle(process);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder buffer, ref int size);
}
