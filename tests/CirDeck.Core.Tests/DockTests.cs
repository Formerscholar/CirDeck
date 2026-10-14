using CirDeck.Core;

namespace CirDeck.Core.Tests;

internal static class DockTests
{
    public static void Run()
    {
        TestHost.Near(DockGeometry.RestWidth(3, 48, 8, 14), 188, "rest width");
        TestHost.Check(DockGeometry.Capacity(188, 48, 8, 14) == 3, "capacity exact");
        TestHost.Check(DockGeometry.Capacity(187, 48, 8, 14) == 2, "capacity short");
        TestHost.Check(DockGeometry.Capacity(0, 48, 8, 14) == 0, "capacity zero width");
        TestHost.Check(DockGeometry.Capacity(100, 0, 8, 14) == 0, "capacity zero icon");
        TestHost.Near(DockGeometry.ReserveWidth(3, 48, 8, 14, 1.6), 274.4, "reserve");

        var idle = new DockRequest(3, 48, 8, 14, 10, 1.6, double.NaN, true);
        var idleScales = DockGeometry.TargetScales(idle);
        TestHost.Check(idleScales.All(scale => Math.Abs(scale - 1) < 0.001), "no pointer keeps scale 1");
        var idleFrame = DockGeometry.Place(idle, idleScales);
        TestHost.Near(idleFrame.Width, 274.4, "idle frame uses reserve width");
        AssertNoOverlap(idleFrame);

        var centers = RestCenters(idle);
        var hot = idle with { PointerX = centers[0] };
        var hotScales = DockGeometry.TargetScales(hot);
        TestHost.Near(hotScales[0], 1.6, "icon under pointer uses full magnification");
        TestHost.Check(hotScales[0] > hotScales[1] && hotScales[1] > hotScales[2], "magnification falls off");
        var hotFrame = DockGeometry.Place(hot, hotScales);
        TestHost.Near(hotFrame.Width, 274.4, "pointer does not resize the reserved bar");
        AssertNoOverlap(hotFrame);
        TestHost.Check(hotFrame.Poses[0].Left >= -0.001 && hotFrame.Poses[^1].Left + hotFrame.Poses[^1].Size <= hotFrame.Width + 0.001, "poses stay inside the bar");

        var off = idle with { Magnify = false, PointerX = centers[0] };
        var offScales = DockGeometry.TargetScales(off);
        TestHost.Check(offScales.All(scale => Math.Abs(scale - 1) < 0.001), "magnify switch disables growth");
        var offFrame = DockGeometry.Place(off, offScales);
        TestHost.Near(offFrame.Width, 188, "disabled magnify uses rest width");
        TestHost.Near(offFrame.Poses[0].Left, 14, "rest layout starts at padding");

        var empty = DockGeometry.Place(new DockRequest(0, 48, 8, 14, 10, 1.6, 0, true), Array.Empty<double>());
        TestHost.Check(empty.Poses.Count == 0, "empty dock");

        var velocity = 0d;
        var value = 0d;
        for (var step = 0; step < 240; step++)
        {
            value = DockMotion.Follow(value, 1, ref velocity, 1d / 120d);
        }

        TestHost.Check(value > 0.98 && Math.Abs(velocity) < 0.05, "spring settles on the target");
        TestHost.Check(PageLayout.PageCount(0, 28) == 1, "empty catalog is one page");
        TestHost.Check(PageLayout.PageCount(29, 28) == 2, "page count rounds up");
        TestHost.Check(PageLayout.Clamp(5, 2) == 1, "page clamp");
    }

    private static void AssertNoOverlap(DockFrame frame)
    {
        for (var i = 1; i < frame.Poses.Count; i++)
        {
            var previous = frame.Poses[i - 1];
            var gap = frame.Poses[i].Left - (previous.Left + previous.Size);
            TestHost.Check(gap >= 8 - 0.001, "gap " + i);
        }
    }

    private static double[] RestCenters(DockRequest request)
    {
        var reserve = DockGeometry.ReserveWidth(request.Count, request.IconSize, request.Spacing, request.PaddingX, request.MagnifyScale);
        var rest = DockGeometry.RestWidth(request.Count, request.IconSize, request.Spacing, request.PaddingX);
        var clusterLeft = (reserve - rest) / 2;
        var pitch = request.IconSize + request.Spacing;
        var centers = new double[request.Count];
        for (var i = 0; i < request.Count; i++)
        {
            centers[i] = clusterLeft + request.PaddingX + request.IconSize / 2 + i * pitch;
        }

        return centers;
    }
}
