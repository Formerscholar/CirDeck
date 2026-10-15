using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace CirDeck.App;

internal static class DesktopBackdrop
{
    public static ImageSource? Current { get; private set; }

    public static ImageSource? Load(double blurRadius = 48)
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
            frame.Freeze();
            const int width = 1400;
            var height = Math.Max(1, (int)Math.Round(frame.PixelHeight * (width / (double)Math.Max(1, frame.PixelWidth))));
            const int bleed = 120;
            var visual = new System.Windows.Controls.Image
            {
                Source = frame,
                Width = width + bleed * 2,
                Height = height + bleed * 2,
                Stretch = Stretch.UniformToFill,
                Effect = new System.Windows.Media.Effects.BlurEffect
                {
                    Radius = Math.Clamp(blurRadius, 0, 80),
                    KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
                    RenderingBias = System.Windows.Media.Effects.RenderingBias.Quality
                }
            };
            var big = new Size(width + bleed * 2, height + bleed * 2);
            visual.Measure(big);
            visual.Arrange(new Rect(big));
            var full = new RenderTargetBitmap((int)big.Width, (int)big.Height, 96, 96, PixelFormats.Pbgra32);
            full.Render(visual);
            var result = new CroppedBitmap(full, new Int32Rect(bleed, bleed, width, height));
            result.Freeze();
            Current = result;
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
