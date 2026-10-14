using System.Runtime.InteropServices;

namespace CirDeck.Interop;

public sealed class AudioMeter : IDisposable
{
    private readonly float[] _levels = new float[8];
    private readonly object _gate = new();
    private Thread? _thread;
    private volatile bool _run;
    private object? _client;
    private object? _capture;

    public void CopyLevels(float[] destination)
    {
        lock (_gate)
        {
            Array.Copy(_levels, destination, Math.Min(destination.Length, _levels.Length));
        }
    }

    public void Start()
    {
        if (_run)
        {
            return;
        }

        _run = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "CirDeck.Audio" };
        _thread.Start();
    }

    public void Stop()
    {
        _run = false;
    }

    public void Dispose()
    {
        Stop();
    }

    private void Loop()
    {
        try
        {
            if (!TryOpen(out var client, out var capture, out var format))
            {
                return;
            }

            _client = client;
            _capture = capture;
            client.Start();
            var samples = new double[256];
            while (_run)
            {
                if (capture.GetNextPacketSize(out var frames) != 0 || frames <= 0)
                {
                    Thread.Sleep(20);
                    continue;
                }

                if (capture.GetBuffer(out var data, out frames, out _, out _, out _) != 0 || data == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    ReadMono(data, frames, format, samples);
                    var next = Bands(samples, Math.Min(samples.Length, frames));
                    lock (_gate)
                    {
                        for (var i = 0; i < _levels.Length; i++)
                        {
                            _levels[i] = Math.Max(next[i], _levels[i] * 0.72f);
                        }
                    }
                }
                finally
                {
                    capture.ReleaseBuffer(frames);
                }
            }

            client.Stop();
        }
        catch (Exception)
        {
        }
        finally
        {
            Release(_capture);
            Release(_client);
            _capture = null;
            _client = null;
        }
    }

    private static bool TryOpen(out IAudioClient client, out IAudioCaptureClient capture, out WaveFormat format)
    {
        client = null!;
        capture = null!;
        format = default;
        var enumerator = (IDeviceEnumerator)new DeviceEnumerator();
        if (enumerator.GetDefaultAudioEndpoint(0, 0, out var device) != 0 || device == null)
        {
            return false;
        }

        var iid = typeof(IAudioClient).GUID;
        if (device.Activate(ref iid, 23, IntPtr.Zero, out var clientObject) != 0 || clientObject == null)
        {
            return false;
        }

        client = (IAudioClient)clientObject;
        if (client.GetMixFormat(out var formatPointer) != 0 || formatPointer == IntPtr.Zero)
        {
            Release(client);
            return false;
        }

        format = Marshal.PtrToStructure<WaveFormat>(formatPointer);
        var started = client.Initialize(0, 0x00020000, 10000000, 0, formatPointer, IntPtr.Zero);
        Marshal.FreeCoTaskMem(formatPointer);
        if (started != 0)
        {
            Release(client);
            return false;
        }

        var captureId = typeof(IAudioCaptureClient).GUID;
        if (client.GetService(ref captureId, out var captureObject) != 0 || captureObject == null)
        {
            Release(client);
            return false;
        }

        capture = (IAudioCaptureClient)captureObject;
        return format.Channels > 0 && (format.Bits == 16 || format.Bits == 32);
    }

    private static void ReadMono(IntPtr data, int frames, WaveFormat format, double[] samples)
    {
        var count = Math.Min(frames, samples.Length);
        var channels = Math.Max(1, (int)format.Channels);
        if (format.Bits == 32 && format.Tag != 1)
        {
            var span = new float[count * channels];
            Marshal.Copy(data, span, 0, span.Length);
            for (var i = 0; i < count; i++)
            {
                double sum = 0;
                for (var channel = 0; channel < channels; channel++)
                {
                    sum += span[i * channels + channel];
                }

                samples[i] = sum / channels;
            }

            return;
        }

        var bytes = new byte[count * format.BlockAlign];
        Marshal.Copy(data, bytes, 0, bytes.Length);
        for (var i = 0; i < count; i++)
        {
            double sum = 0;
            for (var channel = 0; channel < channels; channel++)
            {
                var offset = i * format.BlockAlign + channel * 2;
                if (offset + 1 < bytes.Length)
                {
                    sum += BitConverter.ToInt16(bytes, offset) / 32768d;
                }
            }

            samples[i] = sum / channels;
        }
    }

    private static float[] Bands(double[] samples, int count)
    {
        var levels = new float[8];
        if (count < 32)
        {
            return levels;
        }

        for (var band = 1; band <= 8; band++)
        {
            double real = 0;
            double imag = 0;
            for (var n = 0; n < count; n++)
            {
                var angle = 2 * Math.PI * band * 4 * n / count;
                real += samples[n] * Math.Cos(angle);
                imag -= samples[n] * Math.Sin(angle);
            }

            levels[band - 1] = (float)Math.Min(1, Math.Sqrt(real * real + imag * imag) / count * 8);
        }

        return levels;
    }

    private static void Release(object? instance)
    {
        if (instance != null && Marshal.IsComObject(instance))
        {
            Marshal.ReleaseComObject(instance);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public ushort Tag;
        public ushort Channels;
        public uint Rate;
        public uint Average;
        public ushort BlockAlign;
        public ushort Bits;
        public ushort Extra;
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class DeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IAudioDevice device);
    }

    [ComImport]
    [Guid("DDE8F0C9-3F1A-4C3A-8B2E-6E7A1C0B9F11")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioDevice
    {
        [PreserveSig]
        int Activate(ref Guid interfaceId, int classContext, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }
}

[ComImport]
[Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig]
    int Initialize(int shareMode, uint flags, long bufferDuration, long periodicity, IntPtr format, IntPtr session);

    [PreserveSig]
    int GetBufferSize(out int frames);

    [PreserveSig]
    int GetStreamLatency(out long latency);

    [PreserveSig]
    int GetCurrentPadding(out int padding);

    [PreserveSig]
    int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);

    [PreserveSig]
    int GetMixFormat(out IntPtr format);

    [PreserveSig]
    int GetDevicePeriod(out long standard, out long minimum);

    [PreserveSig]
    int Start();

    [PreserveSig]
    int Stop();

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int SetEventHandle(IntPtr handle);

    [PreserveSig]
    int GetService(ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
}

[ComImport]
[Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    [PreserveSig]
    int GetBuffer(out IntPtr data, out int frames, out int flags, out long devicePosition, out long counter);

    [PreserveSig]
    int ReleaseBuffer(int frames);

    [PreserveSig]
    int GetNextPacketSize(out int frames);
}
