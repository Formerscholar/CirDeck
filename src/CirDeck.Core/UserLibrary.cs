namespace CirDeck.Core;

public sealed class UserLibrary
{
    public List<string> Order { get; set; } = new();

    public List<FolderRecord> Folders { get; set; } = new();

    public List<AppCustomization> Items { get; set; } = new();

    public AppCustomization Edit(string targetPath)
    {
        var found = Items.FirstOrDefault(item => string.Equals(item.TargetPath, targetPath, StringComparison.OrdinalIgnoreCase));
        if (found != null)
        {
            return found;
        }

        found = new AppCustomization { TargetPath = targetPath };
        Items.Add(found);
        return found;
    }
}

public sealed class FolderRecord
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<string> Targets { get; set; } = new();
}

public sealed class AppCustomization
{
    public string TargetPath { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? IconFile { get; set; }

    public bool Hidden { get; set; }
}

public sealed class LaunchTile
{
    public bool IsFolder { get; init; }

    public string Key { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? IconFile { get; init; }

    public AppEntry? App { get; init; }

    public IReadOnlyList<LaunchTile> Children { get; init; } = Array.Empty<LaunchTile>();
}

public sealed class WorkspaceBundle
{
    public int Version { get; set; } = 1;

    public string Name { get; set; } = "默认";

    public AppSettings Settings { get; set; } = new();

    public List<string> DockPins { get; set; } = new();

    public UserLibrary Library { get; set; } = new();
}
