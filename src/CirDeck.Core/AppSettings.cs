namespace CirDeck.Core;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    public int Columns { get; set; } = 7;

    public int Rows { get; set; } = 4;

    public bool CloseAfterLaunch { get; set; } = true;

    public bool OpenLaunchpadAtStartup { get; set; } = true;

    public bool ShowDock { get; set; } = true;

    public double DockIconSize { get; set; } = 48;

    public double DockSpacing { get; set; } = 8;

    public double DockPadding { get; set; } = 14;

    public bool DockMagnify { get; set; } = true;

    public double DockMagnifyScale { get; set; } = 1.6;

    public int MaxDockItems { get; set; } = 12;

    public bool ShowRunningIndicators { get; set; } = true;

    public void Clamp()
    {
        Columns = Math.Clamp(Columns, 4, 12);
        Rows = Math.Clamp(Rows, 2, 8);
        DockIconSize = Math.Clamp(DockIconSize, 32, 96);
        DockSpacing = Math.Clamp(DockSpacing, 0, 32);
        DockPadding = Math.Clamp(DockPadding, 0, 48);
        DockMagnifyScale = Math.Clamp(DockMagnifyScale, 1, 2.2);
        MaxDockItems = Math.Clamp(MaxDockItems, 1, 16);
    }
}
