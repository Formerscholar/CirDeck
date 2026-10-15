using System.Windows.Threading;
using CirDeck.Core;
using CirDeck.Interop;

namespace CirDeck.App;

public sealed class LibrarySession
{
    private readonly Dispatcher _dispatcher;
    private bool _pinsMissing;
    private SearchIndex _index = new(Array.Empty<AppEntry>());

    private LibrarySession(Dispatcher dispatcher, AppSettings settings, IReadOnlyList<AppEntry> apps, IReadOnlyList<string> pins, bool pinsMissing, UserLibrary library)
    {
        _dispatcher = dispatcher;
        Settings = settings;
        Apps = apps;
        DockPins = pins;
        _pinsMissing = pinsMissing;
        Library = library;
        RebuildIndex();
        if (_pinsMissing && apps.Count > 0)
        {
            SeedPins(apps);
        }
    }

    public AppSettings Settings { get; }

    public IReadOnlyList<AppEntry> Apps { get; private set; }

    public IReadOnlyList<string> DockPins { get; private set; }

    public UserLibrary Library { get; private set; }

    public string? OpenFolderId { get; private set; }

    public event Action? Changed;

    public static LibrarySession Load(Dispatcher dispatcher)
    {
        var settings = JsonStore.Load(AppPaths.SettingsPath, () => new AppSettings());
        settings.Clamp();
        JsonStore.Save(AppPaths.SettingsPath, settings);
        var apps = JsonStore.Load(AppPaths.CatalogPath, () => new List<AppEntry>());
        var pinsMissing = !File.Exists(AppPaths.DockPath);
        var pins = pinsMissing
            ? (IReadOnlyList<string>)Array.Empty<string>()
            : JsonStore.Load(AppPaths.DockPath, () => new List<string>());
        var library = JsonStore.Load(AppPaths.LibraryPath, () => new UserLibrary());
        var session = new LibrarySession(dispatcher, settings, apps, pins, pinsMissing, library);
        session.EnsureLayout();
        StartupRegistration.Apply(settings.LaunchAtStartup, Environment.ProcessPath ?? string.Empty);
        return session;
    }

    public IReadOnlyList<LaunchTile> Board()
    {
        var tiles = LibraryBoard.Compose(Apps, Library, null);
        if (string.IsNullOrEmpty(OpenFolderId))
        {
            return tiles;
        }

        var folder = tiles.FirstOrDefault(tile => tile.IsFolder && tile.Key == LibraryBoard.FolderPrefix + OpenFolderId);
        return folder?.Children ?? tiles;
    }

    public IReadOnlyList<LaunchTile> Search(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Board();
        }

        OpenFolderId = null;
        return LibraryBoard.Compose(Apps, Library, text);
    }

    public void OpenFolder(string? folderId)
    {
        OpenFolderId = folderId;
        Changed?.Invoke();
    }

    public void Rename(AppEntry app, string name)
    {
        Library.Edit(app.TargetPath).DisplayName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        SaveLibrary();
    }

    public void SetIcon(AppEntry app, string? iconFile)
    {
        Library.Edit(app.TargetPath).IconFile = iconFile;
        SaveLibrary();
    }

    public void Hide(AppEntry app)
    {
        Library.Edit(app.TargetPath).Hidden = true;
        SaveLibrary();
    }

    public void Unhide(string targetPath)
    {
        Library.Edit(targetPath).Hidden = false;
        SaveLibrary();
    }

    public IReadOnlyList<AppEntry> HiddenApps()
    {
        var hidden = new HashSet<string>(Library.Items.Where(item => item.Hidden).Select(item => item.TargetPath), StringComparer.OrdinalIgnoreCase);
        return Apps.Where(app => hidden.Contains(app.TargetPath)).ToList();
    }

    public void CreateFolder(AppEntry app, string name)
    {
        var folder = new FolderRecord
        {
            Id = Guid.NewGuid().ToString("n")[..8],
            Name = string.IsNullOrWhiteSpace(name) ? "文件夹" : name.Trim(),
            Targets = new List<string> { app.TargetPath }
        };
        Library.Folders.Add(folder);
        var key = LibraryBoard.FolderPrefix + folder.Id;
        var index = Library.Order.FindIndex(item => string.Equals(item, app.TargetPath, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            Library.Order[index] = key;
        }
        else
        {
            Library.Order.Insert(0, key);
        }

        SaveLibrary();
    }

    public void RemoveFromFolder(AppEntry app)
    {
        foreach (var folder in Library.Folders)
        {
            folder.Targets.RemoveAll(target => string.Equals(target, app.TargetPath, StringComparison.OrdinalIgnoreCase));
        }

        if (!Library.Order.Any(item => string.Equals(item, app.TargetPath, StringComparison.OrdinalIgnoreCase)))
        {
            Library.Order.Add(app.TargetPath);
        }

        SaveLibrary();
    }

    public void MoveTile(string fromKey, string toKey)
    {
        var shown = Board();
        if (string.IsNullOrEmpty(OpenFolderId))
        {
            Library.Order = shown.Select(tile => tile.Key).ToList();
        }
        else
        {
            var folder = Library.Folders.FirstOrDefault(item => item.Id == OpenFolderId);
            if (folder != null)
            {
                folder.Targets = shown.Select(tile => tile.Key).ToList();
            }
        }

        LibraryBoard.Move(Library, fromKey, toKey, OpenFolderId);
        SaveLibrary();
    }

    public void SaveLayout(string name)
    {
        var id = Guid.NewGuid().ToString("n")[..8];
        Settings.ActiveLayoutId = id;
        WriteLayout(id, string.IsNullOrWhiteSpace(name) ? "布局" : name.Trim());
        SaveSettings();
    }

    public IReadOnlyList<(string Id, string Name)> LayoutChoices()
    {
        var list = new List<(string, string)>();
        if (!Directory.Exists(AppPaths.LayoutsDirectory))
        {
            return list;
        }

        foreach (var file in Directory.EnumerateFiles(AppPaths.LayoutsDirectory, "*.json"))
        {
            var bundle = JsonStore.Load(file, () => new WorkspaceBundle());
            list.Add((Path.GetFileNameWithoutExtension(file), bundle.Name));
        }

        return list;
    }

    public void SwitchLayout(string id)
    {
        var bundle = JsonStore.Load(LayoutFile(id), () => new WorkspaceBundle());
        ApplyBundle(bundle);
        Settings.ActiveLayoutId = id;
        SaveSettings();
    }

    public void ExportLayout(string path)
    {
        JsonStore.Save(path, CaptureBundle(CurrentLayoutName()));
    }

    public void ImportLayout(string path)
    {
        var bundle = JsonStore.Load(path, () => new WorkspaceBundle());
        var id = Guid.NewGuid().ToString("n")[..8];
        Directory.CreateDirectory(AppPaths.LayoutsDirectory);
        JsonStore.Save(LayoutFile(id), bundle);
        Settings.ActiveLayoutId = id;
        ApplyBundle(bundle);
        SaveSettings();
    }

    public IReadOnlyList<AppEntry> Query(string? text)
    {
        return _index.Search(text);
    }

    public IReadOnlyList<AppEntry> DockEntries()
    {
        var map = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in Apps)
        {
            map[app.TargetPath] = app;
        }

        var list = new List<AppEntry>();
        foreach (var pin in DockPins)
        {
            if (map.TryGetValue(pin, out var app))
            {
                list.Add(app);
            }

            if (list.Count >= Settings.MaxDockItems)
            {
                break;
            }
        }

        return list;
    }

    public bool IsPinned(AppEntry app)
    {
        return DockPins.Any(pin => string.Equals(pin, app.TargetPath, StringComparison.OrdinalIgnoreCase));
    }

    public const string PinDragFormat = "CirDeck.Pin";

    public void Pin(AppEntry app)
    {
        if (IsPinned(app))
        {
            return;
        }

        TogglePin(app);
    }

    public void Unpin(AppEntry app)
    {
        if (!IsPinned(app))
        {
            return;
        }

        TogglePin(app);
    }

    public void TogglePin(AppEntry app)
    {
        var pins = DockPins.ToList();
        var index = pins.FindIndex(pin => string.Equals(pin, app.TargetPath, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            pins.RemoveAt(index);
        }
        else if (pins.Count < Settings.MaxDockItems)
        {
            pins.Add(app.TargetPath);
        }

        DockPins = pins;
        _pinsMissing = false;
        JsonStore.Save(AppPaths.DockPath, pins);
        Changed?.Invoke();
    }

    public void AddApp(AppEntry app)
    {
        var apps = Apps.ToList();
        if (apps.Any(item => string.Equals(item.TargetPath, app.TargetPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        apps.Add(app);
        Apps = apps;
        JsonStore.Save(AppPaths.CatalogPath, apps.ToList());
        if (!Library.Order.Any(item => string.Equals(item, app.TargetPath, StringComparison.OrdinalIgnoreCase)))
        {
            Library.Order.Add(app.TargetPath);
            SaveLibrary();
            return;
        }

        RebuildIndex();
        Changed?.Invoke();
    }

    public void RemoveApp(string path)
    {
        Apps = Apps.Where(item => !string.Equals(item.TargetPath, path, StringComparison.OrdinalIgnoreCase)).ToList();
        JsonStore.Save(AppPaths.CatalogPath, Apps.ToList());
        Library.Order.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        Unpin(Apps.FirstOrDefault(item => string.Equals(item.TargetPath, path, StringComparison.OrdinalIgnoreCase)) ?? new AppEntry("", path, "", "", ""));
        SaveLibrary();
    }

    public void ResetLibrary()
    {
        Library = new UserLibrary();
        JsonStore.Save(AppPaths.LibraryPath, Library);
        Rescan();
    }

    public int CollectDesktop(bool apps)
    {
        var count = DesktopVault.Collect(apps);
        foreach (var app in DesktopVault.TakenApps())
        {
            AddApp(app);
        }

        return count;
    }

    public int RestoreDesktop()
    {
        return DesktopVault.Restore();
    }

    public void SaveSnapshot(string name)
    {
        Directory.CreateDirectory(AppPaths.SnapshotsDirectory);
        var id = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        JsonStore.Save(Path.Combine(AppPaths.SnapshotsDirectory, id + ".json"), CaptureBundle(string.IsNullOrWhiteSpace(name) ? "桌面" : name.Trim()));
    }

    public IReadOnlyList<(string Id, string Name)> Snapshots()
    {
        if (!Directory.Exists(AppPaths.SnapshotsDirectory))
        {
            return Array.Empty<(string, string)>();
        }

        return Directory.EnumerateFiles(AppPaths.SnapshotsDirectory, "*.json")
            .OrderByDescending(file => file)
            .Select(file =>
            {
                var bundle = JsonStore.Load(file, () => new WorkspaceBundle());
                return (Path.GetFileNameWithoutExtension(file), bundle.Name + "  " + Path.GetFileNameWithoutExtension(file));
            })
            .ToList();
    }

    public void ExportWorkspace(string path)
    {
        JsonStore.Save(path, CaptureBundle("导出"));
    }

    public void ImportWorkspace(string path)
    {
        ApplyBundle(JsonStore.Load(path, () => new WorkspaceBundle()));
        SaveSettings();
    }

    public void RestoreSnapshot(string id)
    {
        ApplyBundle(JsonStore.Load(Path.Combine(AppPaths.SnapshotsDirectory, id + ".json"), () => new WorkspaceBundle()));
        SaveSettings();
    }

    public void Rescan()
    {
        var thread = new Thread(() =>
        {
            try
            {
                var apps = StartMenuScanner.Scan().ToList();
                if (Settings.ScanSteam)
                {
                    apps.AddRange(SteamScanner.Scan());
                }

                JsonStore.Save(AppPaths.CatalogPath, apps);
                _dispatcher.BeginInvoke(new Action(() => ApplyScan(apps)));
            }
            catch (Exception ex)
            {
                Log.Write(ex);
            }
        })
        {
            IsBackground = true,
            Name = "CirDeck.Scan"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private void ApplyScan(IReadOnlyList<AppEntry> apps)
    {
        Apps = apps;
        RebuildIndex();
        if (_pinsMissing)
        {
            SeedPins(apps);
        }

        Changed?.Invoke();
    }

    private void RebuildIndex()
    {
        _index = new SearchIndex(Apps);
    }

    private void SaveLibrary()
    {
        JsonStore.Save(AppPaths.LibraryPath, Library);
        WriteLayout(Settings.ActiveLayoutId, CurrentLayoutName());
        RebuildIndex();
        Changed?.Invoke();
    }

    private void SaveSettings()
    {
        Settings.Clamp();
        JsonStore.Save(AppPaths.SettingsPath, Settings);
        StartupRegistration.Apply(Settings.LaunchAtStartup, Environment.ProcessPath ?? string.Empty);
        Changed?.Invoke();
    }

    public void CommitSettings()
    {
        SaveSettings();
        WriteLayout(Settings.ActiveLayoutId, CurrentLayoutName());
    }

    private void EnsureLayout()
    {
        Directory.CreateDirectory(AppPaths.LayoutsDirectory);
        if (!File.Exists(LayoutFile(Settings.ActiveLayoutId)))
        {
            WriteLayout(Settings.ActiveLayoutId, "默认");
        }
    }

    private void WriteLayout(string id, string name)
    {
        Directory.CreateDirectory(AppPaths.LayoutsDirectory);
        JsonStore.Save(LayoutFile(id), CaptureBundle(name));
    }

    private WorkspaceBundle CaptureBundle(string name)
    {
        return new WorkspaceBundle
        {
            Name = name,
            Settings = JsonStore.Load(AppPaths.SettingsPath, () => Settings),
            DockPins = DockPins.ToList(),
            Library = Library
        };
    }

    private void ApplyBundle(WorkspaceBundle bundle)
    {
        Settings.CopyFrom(bundle.Settings ?? new AppSettings());
        DockPins = bundle.DockPins ?? new List<string>();
        Library = bundle.Library ?? new UserLibrary();
        JsonStore.Save(AppPaths.DockPath, DockPins.ToList());
        JsonStore.Save(AppPaths.LibraryPath, Library);
        RebuildIndex();
        Changed?.Invoke();
    }

    private string CurrentLayoutName()
    {
        if (!File.Exists(LayoutFile(Settings.ActiveLayoutId)))
        {
            return "默认";
        }

        return JsonStore.Load(LayoutFile(Settings.ActiveLayoutId), () => new WorkspaceBundle()).Name;
    }

    private static string LayoutFile(string id)
    {
        return Path.Combine(AppPaths.LayoutsDirectory, id + ".json");
    }

    private void SeedPins(IReadOnlyList<AppEntry> apps)
    {
        DockPins = apps.Take(Math.Min(8, apps.Count)).Select(app => app.TargetPath).ToList();
        _pinsMissing = false;
        JsonStore.Save(AppPaths.DockPath, DockPins);
    }
}

internal static class Log
{
    public static void Write(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LocalRoot);
            File.AppendAllText(AppPaths.LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + exception + Environment.NewLine);
        }
        catch (IOException)
        {
        }
    }
}
