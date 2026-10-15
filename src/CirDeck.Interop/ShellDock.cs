using System.Runtime.InteropServices;
using System.Text;

namespace CirDeck.Interop;

public readonly record struct DesktopWindow(string Path, string Name, long Handle);

public static class DesktopWindows
{
    public static IReadOnlyList<DesktopWindow> List()
    {
        var found = new List<DesktopWindow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!Include(hwnd))
            {
                return true;
            }

            var path = Executable(hwnd);
            if (string.IsNullOrWhiteSpace(path) || !seen.Add(path))
            {
                return true;
            }

            var length = NativeMethods.GetWindowTextLength(hwnd);
            var title = new StringBuilder(Math.Max(1, length + 1));
            NativeMethods.GetWindowText(hwnd, title, title.Capacity);
            var name = title.ToString().Trim();
            if (name.Length == 0)
            {
                name = Path.GetFileNameWithoutExtension(path);
            }

            found.Add(new DesktopWindow(path, name, hwnd.ToInt64()));
            return found.Count < 12;
        }, IntPtr.Zero);
        return found;
    }

    public static void Activate(long handle)
    {
        var hwnd = new IntPtr(handle);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SwRestore);
        }

        NativeMethods.SetForegroundWindow(hwnd);
    }

    public static bool ForegroundIsFullscreen()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        var className = new StringBuilder(64);
        NativeMethods.GetClassName(hwnd, className, className.Capacity);
        if (className.ToString() is "Shell_TrayWnd" or "Progman" or "WorkerW")
        {
            return false;
        }

        var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.Point { X = rect.Left + 1, Y = rect.Top + 1 }, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        return rect.Left <= info.Monitor.Left && rect.Top <= info.Monitor.Top && rect.Right >= info.Monitor.Right && rect.Bottom >= info.Monitor.Bottom;
    }

    private static bool Include(IntPtr hwnd)
    {
        if (NativeMethods.GetWindowTextLength(hwnd) == 0)
        {
            return false;
        }

        var iconic = NativeMethods.IsIconic(hwnd);
        if (!NativeMethods.IsWindowVisible(hwnd) && !iconic)
        {
            return false;
        }

        var className = new StringBuilder(64);
        NativeMethods.GetClassName(hwnd, className, className.Capacity);
        if (className.ToString() is "Shell_TrayWnd" or "Progman" or "WorkerW" or "CirDeck" )
        {
            return false;
        }

        var style = ExStyle(hwnd);
        var tool = (style & 0x80) != 0;
        var app = (style & 0x40000) != 0;
        return !tool || app;
    }

    private static long ExStyle(IntPtr hwnd)
    {
        return IntPtr.Size == 8
            ? NativeMethods.GetWindowLongPtr64(hwnd, -20).ToInt64()
            : NativeMethods.GetWindowLong32(hwnd, -20);
    }

    private static string Executable(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        var process = NativeMethods.OpenProcess(0x1000, false, processId);
        if (process == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref size) ? buffer.ToString() : string.Empty;
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }
}

public static class TaskbarGate
{
    public static void Apply(bool hide)
    {
        var hwnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.ShowWindow(hwnd, hide ? 0 : 5);
    }
}

public static class RecycleBin
{
    public static void Open()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "shell:RecycleBinFolder",
            UseShellExecute = true
        });
    }

    public static void Empty()
    {
        SHEmptyRecycleBin(IntPtr.Zero, null, 0x00000001 | 0x00000002 | 0x00000004);
    }

    public static void MoveToBin(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var from = path + "\0\0";
        var operation = new ShFileOp
        {
            Function = 3,
            From = from,
            Flags = 0x0040 | 0x0010 | 0x0400
        };
        SHFileOperation(ref operation);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref ShFileOp operation);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOp
    {
        public IntPtr Hwnd;
        public uint Function;
        public string From;
        public string? To;
        public ushort Flags;
        public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }
}
