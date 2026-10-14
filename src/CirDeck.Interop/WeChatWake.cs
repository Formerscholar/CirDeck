using System.Runtime.InteropServices;
using System.Text;

namespace CirDeck.Interop;

public static class WeChatWake
{
    public static bool TryActivate()
    {
        var found = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (Matches(hwnd))
            {
                found = hwnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        if (found == IntPtr.Zero)
        {
            return false;
        }

        if (NativeMethods.IsIconic(found))
        {
            NativeMethods.ShowWindow(found, NativeMethods.SwRestore);
        }
        else
        {
            NativeMethods.ShowWindow(found, 5);
        }

        NativeMethods.SetForegroundWindow(found);
        return true;
    }

    private static bool Matches(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0)
        {
            return false;
        }

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

            var name = Path.GetFileName(buffer.ToString());
            return name.Equals("Weixin.exe", StringComparison.OrdinalIgnoreCase)
                || name.Equals("WeChat.exe", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }
}
