using System.Runtime.InteropServices;
using System.Text;

namespace CirDeck.Interop;

public static class LivePreview
{
    public static IntPtr FindMainWindow(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || executablePath.StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
        {
            return IntPtr.Zero;
        }

        IntPtr best = IntPtr.Zero;
        var bestArea = 0;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
            {
                return true;
            }

            if (!SameExecutable(hwnd, executablePath))
            {
                return true;
            }

            if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            {
                return true;
            }

            var area = Math.Max(0, rect.Right - rect.Left) * Math.Max(0, rect.Bottom - rect.Top);
            if (area > bestArea)
            {
                bestArea = area;
                best = hwnd;
            }

            return true;
        }, IntPtr.Zero);
        return best;
    }

    public static IntPtr Register(IntPtr destination, IntPtr source)
    {
        if (destination == IntPtr.Zero || source == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        return NativeMethods.DwmRegisterThumbnail(destination, source, out var thumb) == 0 ? thumb : IntPtr.Zero;
    }

    public static void Update(IntPtr thumbnail, int width, int height)
    {
        if (thumbnail == IntPtr.Zero || width < 1 || height < 1)
        {
            return;
        }

        var properties = new NativeMethods.ThumbnailProperties
        {
            Flags = 0x1 | 0x4 | 0x8,
            Destination = new NativeMethods.Rect { Left = 0, Top = 0, Right = width, Bottom = height },
            Opacity = 255,
            Visible = true
        };
        NativeMethods.DwmUpdateThumbnailProperties(thumbnail, ref properties);
    }

    public static void Close(IntPtr thumbnail)
    {
        if (thumbnail != IntPtr.Zero)
        {
            NativeMethods.DwmUnregisterThumbnail(thumbnail);
        }
    }

    private static bool SameExecutable(IntPtr hwnd, string executablePath)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        var process = NativeMethods.OpenProcess(0x1000, false, processId);
        if (process == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            if (!NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref size))
            {
                return false;
            }

            return string.Equals(Path.GetFullPath(buffer.ToString()), Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }
}
