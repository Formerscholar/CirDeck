namespace CirDeck.Core;

public readonly record struct DockRequest(
    int Count,
    double IconSize,
    double Spacing,
    double PaddingX,
    double PaddingY,
    double MagnifyScale,
    double PointerX,
    bool Magnify);

public readonly record struct DockPose(int Index, double Left, double Top, double Size, double Scale);

public sealed record DockFrame(double Width, double Height, IReadOnlyList<DockPose> Poses);

/// <summary>
/// Dock sizes are plain numbers. The window applies them with a render transform and does not
/// remeasure children on each frame.
/// </summary>
public static class DockGeometry
{
    public static double RestWidth(int count, double iconSize, double spacing, double paddingX)
    {
        if (count <= 0 || iconSize <= 0)
        {
            return Math.Max(0, paddingX) * 2;
        }

        return paddingX * 2 + count * iconSize + Math.Max(0, count - 1) * Math.Max(0, spacing);
    }

    public static double ReserveWidth(int count, double iconSize, double spacing, double paddingX, double magnifyScale)
    {
        var rest = RestWidth(count, iconSize, spacing, paddingX);
        if (count <= 0 || iconSize <= 0 || magnifyScale <= 1)
        {
            return rest;
        }

        var extraIcons = Math.Min(count, 5);
        return rest + iconSize * (magnifyScale - 1) * extraIcons;
    }

    public static int Capacity(double barWidth, double iconSize, double spacing, double paddingX)
    {
        if (barWidth <= 0 || iconSize <= 0)
        {
            return 0;
        }

        var inner = barWidth - paddingX * 2;
        if (inner < iconSize)
        {
            return 0;
        }

        var gap = Math.Max(0, spacing);
        var pitch = iconSize + gap;
        return Math.Max(0, (int)Math.Floor((inner + gap) / pitch));
    }

    public static double[] TargetScales(in DockRequest request)
    {
        var scales = new double[Math.Max(0, request.Count)];
        if (scales.Length == 0 || request.IconSize <= 0)
        {
            return scales;
        }

        var magnify = request.Magnify && request.MagnifyScale > 1 && IsFinite(request.PointerX);
        var radius = request.IconSize * 2.5;
        var centers = RestCenters(request);
        for (var i = 0; i < scales.Length; i++)
        {
            var scale = 1d;
            if (magnify && radius > 0)
            {
                var distance = Math.Abs(request.PointerX - centers[i]);
                if (distance < radius)
                {
                    var t = distance / radius;
                    var falloff = 0.5 * (1 + Math.Cos(Math.PI * t));
                    scale = 1 + (request.MagnifyScale - 1) * falloff;
                }
            }

            scales[i] = scale;
        }

        return scales;
    }

    public static DockFrame Place(in DockRequest request, IReadOnlyList<double> scales)
    {
        var count = request.Count;
        if (count <= 0 || request.IconSize <= 0 || scales.Count < count)
        {
            return new DockFrame(Math.Max(0, request.PaddingX * 2), Math.Max(0, request.PaddingY * 2), Array.Empty<DockPose>());
        }

        var magnify = request.Magnify && request.MagnifyScale > 1;
        var reserve = ReserveWidth(count, request.IconSize, request.Spacing, request.PaddingX, magnify ? request.MagnifyScale : 1);
        var content = 0d;
        var maxScale = 1d;
        for (var i = 0; i < count; i++)
        {
            var scale = scales[i] < 1 ? 1 : scales[i];
            if (scale > maxScale)
            {
                maxScale = scale;
            }

            content += request.IconSize * scale;
        }

        if (count > 1)
        {
            content += request.Spacing * (count - 1);
        }

        var cursor = (reserve - content) / 2;
        var rowHeight = request.IconSize * maxScale;
        var poses = new DockPose[count];
        for (var i = 0; i < count; i++)
        {
            var scale = scales[i] < 1 ? 1 : scales[i];
            var size = request.IconSize * scale;
            var top = request.PaddingY + (rowHeight - size) / 2;
            poses[i] = new DockPose(i, cursor, top, size, scale);
            cursor += size + request.Spacing;
        }

        return new DockFrame(reserve, request.PaddingY * 2 + rowHeight, poses);
    }

    private static double[] RestCenters(in DockRequest request)
    {
        var magnify = request.Magnify && request.MagnifyScale > 1;
        var reserve = ReserveWidth(request.Count, request.IconSize, request.Spacing, request.PaddingX, magnify ? request.MagnifyScale : 1);
        var rest = RestWidth(request.Count, request.IconSize, request.Spacing, request.PaddingX);
        var clusterLeft = (reserve - rest) / 2;
        var pitch = request.IconSize + request.Spacing;
        var centers = new double[request.Count];
        for (var i = 0; i < request.Count; i++)
        {
            centers[i] = clusterLeft + request.PaddingX + request.IconSize / 2 + i * pitch;
        }

        return centers;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
