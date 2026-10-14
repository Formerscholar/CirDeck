using System.Runtime.InteropServices;

namespace CirDeck.Interop;

public readonly record struct SystemSample(double CpuPercent, double MemoryPercent, string MemoryText);

public static class SystemSampler
{
    private static long _idle;
    private static long _kernel;
    private static long _user;
    private static bool _hasLast;

    public static SystemSample Read()
    {
        var cpu = ReadCpu();
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref memory) || memory.TotalPhys == 0)
        {
            return new SystemSample(cpu, 0, string.Empty);
        }

        var used = memory.TotalPhys - memory.AvailPhys;
        var percent = used * 100d / memory.TotalPhys;
        var text = (used / 1073741824d).ToString("0.0") + " / " + (memory.TotalPhys / 1073741824d).ToString("0.0") + " GB";
        return new SystemSample(cpu, percent, text);
    }

    private static double ReadCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return 0;
        }

        var idleNow = idle.ToTicks();
        var kernelNow = kernel.ToTicks();
        var userNow = user.ToTicks();
        if (!_hasLast)
        {
            _idle = idleNow;
            _kernel = kernelNow;
            _user = userNow;
            _hasLast = true;
            return 0;
        }

        var idleDelta = idleNow - _idle;
        var total = kernelNow - _kernel + userNow - _user;
        _idle = idleNow;
        _kernel = kernelNow;
        _user = userNow;
        if (total <= 0)
        {
            return 0;
        }

        var busy = total - idleDelta;
        return Math.Clamp(busy * 100d / total, 0, 100);
    }

    private static long ToTicks(this FileTime value)
    {
        return ((long)value.High << 32) | (uint)value.Low;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public int Low;
        public int High;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}
