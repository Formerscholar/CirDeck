namespace CirDeck.Core;

public static class LibraryBoard
{
    public const string FolderPrefix = "folder:";

    public static IReadOnlyList<LaunchTile> Compose(IReadOnlyList<AppEntry> apps, UserLibrary library, string? query)
    {
        var map = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
        {
            if (!map.ContainsKey(app.TargetPath))
            {
                map[app.TargetPath] = app;
            }
        }

        var custom = new Dictionary<string, AppCustomization>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in library.Items)
        {
            if (!string.IsNullOrWhiteSpace(item.TargetPath) && !custom.ContainsKey(item.TargetPath))
            {
                custom[item.TargetPath] = item;
            }
        }

        bool IsHidden(string path)
        {
            return custom.TryGetValue(path, out var edit) && edit.Hidden;
        }

        LaunchTile MakeApp(AppEntry app)
        {
            custom.TryGetValue(app.TargetPath, out var edit);
            var name = string.IsNullOrWhiteSpace(edit?.DisplayName) ? app.Name : edit!.DisplayName!;
            return new LaunchTile
            {
                Key = app.TargetPath,
                Name = name,
                IconFile = string.IsNullOrWhiteSpace(edit?.IconFile) ? null : edit!.IconFile,
                App = app with { Name = name }
            };
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var renamed = new List<AppEntry>();
            foreach (var app in map.Values)
            {
                if (!IsHidden(app.TargetPath))
                {
                    renamed.Add(MakeApp(app).App!);
                }
            }

            return new SearchIndex(renamed).Search(query).Select(hit => MakeApp(map[hit.TargetPath])).ToList();
        }

        var inside = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new Dictionary<string, LaunchTile>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in library.Folders)
        {
            var children = new List<LaunchTile>();
            foreach (var target in folder.Targets)
            {
                if (!map.TryGetValue(target, out var app) || IsHidden(target))
                {
                    continue;
                }

                children.Add(MakeApp(app));
                inside.Add(target);
            }

            if (children.Count == 0)
            {
                continue;
            }

            folders[folder.Id] = new LaunchTile
            {
                IsFolder = true,
                Key = FolderPrefix + folder.Id,
                Name = string.IsNullOrWhiteSpace(folder.Name) ? "文件夹" : folder.Name,
                Children = children,
                IconFile = children[0].IconFile,
                App = children[0].App
            };
        }

        var tiles = new List<LaunchTile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in library.Order)
        {
            if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
            {
                continue;
            }

            if (key.StartsWith(FolderPrefix, StringComparison.Ordinal))
            {
                var id = key.Substring(FolderPrefix.Length);
                if (folders.TryGetValue(id, out var folderTile))
                {
                    tiles.Add(folderTile);
                }

                continue;
            }

            if (inside.Contains(key) || IsHidden(key) || !map.TryGetValue(key, out var app))
            {
                continue;
            }

            tiles.Add(MakeApp(app));
        }

        foreach (var folderTile in folders.Values)
        {
            if (seen.Add(folderTile.Key))
            {
                tiles.Add(folderTile);
            }
        }

        foreach (var app in map.Values.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (inside.Contains(app.TargetPath) || IsHidden(app.TargetPath))
            {
                continue;
            }

            if (seen.Add(app.TargetPath))
            {
                tiles.Add(MakeApp(app));
            }
        }

        return tiles;
    }

    public static void Move(UserLibrary library, string fromKey, string toKey, string? folderId)
    {
        if (string.IsNullOrWhiteSpace(fromKey) || string.Equals(fromKey, toKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrEmpty(folderId))
        {
            library.Order.RemoveAll(key => string.Equals(key, fromKey, StringComparison.OrdinalIgnoreCase));
            var index = library.Order.FindIndex(key => string.Equals(key, toKey, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                library.Order.Add(fromKey);
            }
            else
            {
                library.Order.Insert(index, fromKey);
            }

            return;
        }

        var folder = library.Folders.FirstOrDefault(item => item.Id == folderId);
        if (folder == null)
        {
            return;
        }

        folder.Targets.RemoveAll(key => string.Equals(key, fromKey, StringComparison.OrdinalIgnoreCase));
        var at = folder.Targets.FindIndex(key => string.Equals(key, toKey, StringComparison.OrdinalIgnoreCase));
        if (at < 0)
        {
            folder.Targets.Add(fromKey);
        }
        else
        {
            folder.Targets.Insert(at, fromKey);
        }
    }
}

public static class HotCorner
{
    public static bool Contains(string corner, int x, int y, int left, int top, int right, int bottom, int margin = 6)
    {
        var nearLeft = x <= left + margin;
        var nearRight = x >= right - 1 - margin;
        var nearTop = y <= top + margin;
        var nearBottom = y >= bottom - 1 - margin;
        return corner switch
        {
            "TopRight" => nearRight && nearTop,
            "BottomLeft" => nearLeft && nearBottom,
            "BottomRight" => nearRight && nearBottom,
            _ => nearLeft && nearTop
        };
    }
}
