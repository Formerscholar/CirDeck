using System.Runtime.InteropServices;
using System.Text;

namespace CirDeck.Interop;

internal static class NativeMethods
{
    public const int MonitorDefaultToNearest = 2;
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;
    public const int WmHotkey = 0x0312;
    public const int SwRestore = 9;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PrivateExtractIconsW")]
    public static extern int PrivateExtractIcons(
        string fileName,
        int iconIndex,
        int cxIcon,
        int cyIcon,
        IntPtr[] icons,
        int[] ids,
        int count,
        int flags);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr icon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint infoSize, uint flags);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(Point point, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hwnd);

    [DllImport("kernel32.dll")]
    public static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetStdHandle(int handle);

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ShFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }
}

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

public static class MonitorLayout
{
    public static PixelRect WorkAreaFromCursor()
    {
        if (!NativeMethods.GetCursorPos(out var point))
        {
            point = default;
        }

        var monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return new PixelRect(0, 0, NativeMethods.GetSystemMetrics(0), NativeMethods.GetSystemMetrics(1));
        }

        return new PixelRect(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
    }
}

public static class WindowChrome
{
    public static void Round(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var preference = 2;
        NativeMethods.DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int));
    }
}

public static class HotkeyWindow
{
    public const int Id = 1;

    public static bool RegisterToggle(IntPtr hwnd)
    {
        return NativeMethods.RegisterHotKey(
            hwnd,
            Id,
            NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat,
            0x20);
    }

    public static void Unregister(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(hwnd, Id);
        }
    }

    public static bool IsHotkey(int message)
    {
        return message == NativeMethods.WmHotkey;
    }
}

public static class IconExtractor
{
    private const uint IconFlag = 0x100;
    private const uint LargeIcon = 0x0;

    public static IntPtr Extract(string path, int size)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return IntPtr.Zero;
        }

        var icons = new IntPtr[1];
        var ids = new int[1];
        var count = NativeMethods.PrivateExtractIcons(path, 0, size, size, icons, ids, 1, 0);
        if (count > 0 && icons[0] != IntPtr.Zero)
        {
            return icons[0];
        }

        var info = new NativeMethods.ShFileInfo();
        NativeMethods.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<NativeMethods.ShFileInfo>(), IconFlag | LargeIcon);
        return info.Icon;
    }

    public static void Destroy(IntPtr icon)
    {
        if (icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(icon);
        }
    }
}

public static class ConsoleAttach
{
    public static void Parent()
    {
        if (!NativeMethods.AttachConsole(-1))
        {
            return;
        }

        var handle = NativeMethods.GetStdHandle(-11);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            return;
        }

        var stream = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(handle, ownsHandle: false), FileAccess.Write);
        Console.SetOut(new StreamWriter(stream) { AutoFlush = true });
    }
}
