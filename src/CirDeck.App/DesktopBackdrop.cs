using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace CirDeck.App;

internal static class DesktopBackdrop
{
    public static ImageSource? Load()
    {
        try
        {
            var path = WallpaperPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
            const int width = 220;
            var height = Math.Max(1, (int)Math.Round(frame.PixelHeight * (width / (double)Math.Max(1, frame.PixelWidth))));
            var scaled = new TransformedBitmap(frame, new ScaleTransform(width / (double)frame.PixelWidth, height / (double)frame.PixelHeight));
            var formatted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            var stride = width * 4;
            var pixels = new byte[stride * height];
            formatted.CopyPixels(pixels, stride, 0);
            BoxBlur(pixels, width, height, 10);
            var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? WallpaperPath()
    {
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
        if (File.Exists(local))
        {
            return local;
        }

        var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
        if (File.Exists(roaming))
        {
            return roaming;
        }

        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        return key?.GetValue("WallPaper") as string;
    }

    private static void BoxBlur(byte[] pixels, int width, int height, int radius)
    {
        var copy = new byte[pixels.Length];
        BlurAxis(pixels, copy, width, height, radius, true);
        BlurAxis(copy, pixels, width, height, radius, false);
    }

    private static void BlurAxis(byte[] source, byte[] destination, int width, int height, int radius, bool horizontal)
    {
        var length = horizontal ? width : height;
        var lines = horizontal ? height : width;
        for (var line = 0; line < lines; line++)
        {
            int blue = 0, green = 0, red = 0, alpha = 0;
            var count = 0;
            for (var i = -radius; i < length; i++)
            {
                if (i + radius < length)
                {
                    Add(source, Index(i + radius, line, width, horizontal), ref blue, ref green, ref red, ref alpha);
                    count++;
                }

                if (i - radius - 1 >= 0)
                {
                    Add(source, Index(i - radius - 1, line, width, horizontal), ref blue, ref green, ref red, ref alpha, -1);
                    count--;
                }

                if (i >= 0 && count > 0)
                {
                    var at = Index(i, line, width, horizontal);
                    destination[at] = (byte)(blue / count);
                    destination[at + 1] = (byte)(green / count);
                    destination[at + 2] = (byte)(red / count);
                    destination[at + 3] = (byte)(alpha / count);
                }
            }
        }
    }

    private static int Index(int primary, int line, int width, bool horizontal)
    {
        return horizontal ? (line * width + primary) * 4 : (primary * width + line) * 4;
    }

    private static void Add(byte[] pixels, int index, ref int blue, ref int green, ref int red, ref int alpha, int sign = 1)
    {
        blue += sign * pixels[index];
        green += sign * pixels[index + 1];
        red += sign * pixels[index + 2];
        alpha += sign * pixels[index + 3];
    }
}
