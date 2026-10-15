using System.Runtime.InteropServices;
using System.Text;

namespace CirDeck.Interop;

public sealed class TrayIcon
{
    public string Name { get; init; } = "";

    public string Path { get; init; } = "";

    public IntPtr Toolbar { get; init; }

    public int Index { get; init; }

    public void Click(bool right)
    {
        if (Toolbar == IntPtr.Zero)
        {
            return;
        }

        var remote = TrayIcons.Allocate(Toolbar, 32);
        if (remote == IntPtr.Zero)
        {
            return;
        }

        try
        {
            if (TrayIcons.Send(Toolbar, 0x041D, (IntPtr)Index, remote) == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
            {
                return;
            }

            var rect = new int[4];
            if (!TrayIcons.Read(Toolbar, remote, rect))
            {
                return;
            }

            var x = (rect[0] + rect[2]) / 2;
            var y = (rect[1] + rect[3]) / 2;
            var point = (IntPtr)((y << 16) | (x & 0xFFFF));
            var down = right ? 0x0204 : 0x0201;
            var up = right ? 0x0205 : 0x0202;
            TrayIcons.Send(Toolbar, down, (IntPtr)1, point);
            TrayIcons.Send(Toolbar, up, IntPtr.Zero, point);
        }
        finally
        {
            TrayIcons.Release(Toolbar, remote);
        }
    }
}

public static class TrayIcons
{
    public static IReadOnlyList<TrayIcon> List()
    {
        var icons = new List<TrayIcon>();
        Collect(FindToolbar("Shell_TrayWnd", "TrayNotifyWnd"), icons);
        var overflow = FindWindow("NotifyIconOverflowWindow", null);
        Collect(FindChild(overflow, "ToolbarWindow32"), icons);
        return icons;
    }

    internal static IntPtr Send(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam)
    {
        return SendMessage(hwnd, message, wparam, lparam);
    }

    internal static IntPtr Allocate(IntPtr hwnd, int size)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        var process = OpenProcess(0x1438, false, pid);
        if (process == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var memory = VirtualAllocEx(process, IntPtr.Zero, (uint)size, 0x1000, 0x04);
        CloseHandle(process);
        return memory;
    }

    internal static void Release(IntPtr hwnd, IntPtr memory)
    {
        if (memory == IntPtr.Zero)
        {
            return;
        }

        GetWindowThreadProcessId(hwnd, out var pid);
        var process = OpenProcess(0x1438, false, pid);
        if (process == IntPtr.Zero)
        {
            return;
        }

        VirtualFreeEx(process, memory, 0, 0x8000);
        CloseHandle(process);
    }

    internal static bool Read(IntPtr hwnd, IntPtr address, int[] values)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        var process = OpenProcess(0x1438, false, pid);
        if (process == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var bytes = new byte[values.Length * 4];
            if (!ReadProcessMemory(process, address, bytes, bytes.Length, out _))
            {
                return false;
            }

            Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            return true;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static void Collect(IntPtr toolbar, List<TrayIcon> icons)
    {
        if (toolbar == IntPtr.Zero || IntPtr.Size != 8)
        {
            return;
        }

        var count = SendMessage(toolbar, 0x0418, IntPtr.Zero, IntPtr.Zero).ToInt32();
        count = Math.Min(count, 24);
        for (var i = 0; i < count; i++)
        {
            var button = ReadButton(toolbar, i);
            if (button == null)
            {
                continue;
            }

            var owner = FindOwner(toolbar, button.Value.Data);
            var path = owner == IntPtr.Zero ? "" : Executable(owner);
            var name = Tooltip(toolbar, button.Value.Data);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = string.IsNullOrWhiteSpace(path) ? "托盘图标" : Path.GetFileNameWithoutExtension(path);
            }

            icons.Add(new TrayIcon { Name = name, Path = path, Toolbar = toolbar, Index = i });
        }
    }

    private static ButtonInfo? ReadButton(IntPtr toolbar, int index)
    {
        var remote = Allocate(toolbar, 32);
        if (remote == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            SendMessage(toolbar, 0x0417, (IntPtr)index, remote);
            GetWindowThreadProcessId(toolbar, out var pid);
            var process = OpenProcess(0x1438, false, pid);
            if (process == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var bytes = new byte[32];
                if (!ReadProcessMemory(process, remote, bytes, bytes.Length, out _))
                {
                    return null;
                }

                return new ButtonInfo(BitConverter.ToInt64(bytes, 16));
            }
            finally
            {
                CloseHandle(process);
            }
        }
        finally
        {
            Release(toolbar, remote);
        }
    }

    private static IntPtr FindOwner(IntPtr toolbar, long data)
    {
        if (data == 0)
        {
            return IntPtr.Zero;
        }

        GetWindowThreadProcessId(toolbar, out var pid);
        var process = OpenProcess(0x1438, false, pid);
        if (process == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        try
        {
            var bytes = new byte[256];
            if (!ReadProcessMemory(process, new IntPtr(data), bytes, bytes.Length, out _))
            {
                return IntPtr.Zero;
            }

            for (var offset = 0; offset <= bytes.Length - 8; offset += 8)
            {
                var value = new IntPtr(BitConverter.ToInt64(bytes, offset));
                if (value != IntPtr.Zero && IsWindow(value))
                {
                    return value;
                }
            }

            return IntPtr.Zero;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static string Tooltip(IntPtr toolbar, long data)
    {
        if (data == 0)
        {
            return "";
        }

        GetWindowThreadProcessId(toolbar, out var pid);
        var process = OpenProcess(0x1438, false, pid);
        if (process == IntPtr.Zero)
        {
            return "";
        }

        try
        {
            var bytes = new byte[512];
            if (!ReadProcessMemory(process, new IntPtr(data), bytes, bytes.Length, out _))
            {
                return "";
            }

            var text = Encoding.Unicode.GetString(bytes);
            var best = "";
            var current = new StringBuilder();
            foreach (var character in text)
            {
                if (character is >= ' ' and not (char)0xFFFD)
                {
                    current.Append(character);
                    continue;
                }

                if (current.Length > best.Length && current.Length >= 2)
                {
                    best = current.ToString();
                }

                current.Clear();
            }

            return best.Trim();
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static string Executable(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var processId);
        var process = OpenProcess(0x1000, false, processId);
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

    private static IntPtr FindToolbar(string rootClass, string childClass)
    {
        var root = FindWindow(rootClass, null);
        var child = FindChild(root, childClass);
        return FindChild(FindChild(child, "SysPager"), "ToolbarWindow32");
    }

    private static IntPtr FindChild(IntPtr parent, string className)
    {
        return parent == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(parent, IntPtr.Zero, className, null);
    }

    private readonly record struct ButtonInfo(long Data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, uint size, uint type, uint protect);

    [DllImport("kernel32.dll")]
    private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, uint size, uint freeType);

    [DllImport("kernel32.dll")]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out int read);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder buffer, ref int size);
}
