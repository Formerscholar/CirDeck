using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace CirDeck.Interop;

public static class SystemStatus
{
    public static bool TryGetVolume(out float level, out bool muted)
    {
        level = 0;
        muted = false;
        try
        {
            var volume = Endpoint();
            if (volume == null)
            {
                return false;
            }

            volume.GetMasterVolumeLevelScalar(out level);
            volume.GetMute(out muted);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
    }

    public static void SetVolume(float level)
    {
        try
        {
            var volume = Endpoint();
            if (volume == null)
            {
                return;
            }

            var context = Guid.Empty;
            level = Math.Clamp(level, 0, 1);
            volume.SetMasterVolumeLevelScalar(level, ref context);
            if (level > 0.01f)
            {
                volume.SetMute(false, ref context);
            }
        }
        catch (COMException)
        {
        }
    }

    public static string InputLabel()
    {
        var layout = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero));
        var lang = (ushort)(layout.ToInt64() & 0xFFFF);
        return lang is 0x0804 or 0x0404 or 0x0C04 ? "中" : "EN";
    }

    public static void NextInput()
    {
        keybd_event(0x5B, 0, 0, UIntPtr.Zero);
        keybd_event(0x20, 0, 0, UIntPtr.Zero);
        keybd_event(0x20, 0, 2, UIntPtr.Zero);
        keybd_event(0x5B, 0, 2, UIntPtr.Zero);
    }

    public static bool NetworkUp()
    {
        return NetworkInterface.GetAllNetworkInterfaces().Any(item =>
            item.OperationalStatus == OperationalStatus.Up
            && item.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel);
    }

    public static bool WifiUp()
    {
        return NetworkInterface.GetAllNetworkInterfaces().Any(item =>
            item.OperationalStatus == OperationalStatus.Up && item.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
    }

    public static void Sleep()
    {
        SetSuspendState(false, false, false);
    }

    private static IAudioEndpointVolume? Endpoint()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        if (enumerator.GetDefaultAudioEndpoint(0, 1, out var device) != 0 || device == null)
        {
            return null;
        }

        var iid = typeof(IAudioEndpointVolume).GUID;
        if (device.Activate(ref iid, 23, IntPtr.Zero, out var instance) != 0 || instance == null)
        {
            return null;
        }

        return (IAudioEndpointVolume)instance;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

    [DllImport("powrprof.dll")]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWake);

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr client);

        int UnregisterControlChangeNotify(IntPtr client);

        int GetChannelCount(out int count);

        int SetMasterVolumeLevel(float level, ref Guid context);

        int SetMasterVolumeLevelScalar(float level, ref Guid context);

        int GetMasterVolumeLevel(out float level);

        int GetMasterVolumeLevelScalar(out float level);

        int SetChannelVolumeLevel(int channel, float level, ref Guid context);

        int SetChannelVolumeLevelScalar(int channel, float level, ref Guid context);

        int GetChannelVolumeLevel(int channel, out float level);

        int GetChannelVolumeLevelScalar(int channel, out float level);

        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);

        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);

        int GetVolumeStepInfo(out int step, out int stepCount);

        int VolumeStepUp(ref Guid context);

        int VolumeStepDown(ref Guid context);

        int QueryHardwareSupport(out uint mask);

        int GetVolumeRange(out float min, out float max, out float increment);
    }
}
