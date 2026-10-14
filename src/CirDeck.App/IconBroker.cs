using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CirDeck.Core;
using CirDeck.Interop;

namespace CirDeck.App;

public sealed class IconBroker
{
    private readonly ConcurrentDictionary<string, byte> _queued = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _ready = new(StringComparer.OrdinalIgnoreCase);

    public IconBroker()
    {
        Directory.CreateDirectory(AppPaths.IconCache);
    }

    public event Action<string, string>? Ready;

    public bool TryCached(string path, out string png)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            png = string.Empty;
            return false;
        }

        if (_ready.TryGetValue(path, out png!))
        {
            return true;
        }

        var destination = Destination(path);
        if (!File.Exists(destination))
        {
            png = string.Empty;
            return false;
        }

        _ready[path] = destination;
        png = destination;
        return true;
    }

    public void Request(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !_queued.TryAdd(path, 0))
        {
            return;
        }

        if (TryCached(path, out var png))
        {
            Ready?.Invoke(path, png);
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            try
            {
                var destination = Destination(path);
                if (!File.Exists(destination))
                {
                    Render(path, destination);
                }

                if (!File.Exists(destination))
                {
                    return;
                }

                _ready[path] = destination;
                Ready?.Invoke(path, destination);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
            {
            }
        }));
    }

    public static ImageSource? LoadImage(string png)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(png);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    public void Dispose()
    {
    }

    private static void Render(string path, string destination)
    {
        var icon = IconExtractor.Extract(path, 48);
        if (icon == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = File.Create(destination);
            encoder.Save(stream);
        }
        finally
        {
            IconExtractor.Destroy(icon);
        }
    }

    private static string Destination(string path)
    {
        long stamp = 0;
        try
        {
            if (File.Exists(path))
            {
                stamp = File.GetLastWriteTimeUtc(path).Ticks;
            }
        }
        catch (IOException)
        {
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant() + ":" + stamp)));
        return Path.Combine(AppPaths.IconCache, hash + ".png");
    }
}
