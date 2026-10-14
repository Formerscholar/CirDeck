using System.Windows.Threading;
using CirDeck.Core;
using CirDeck.Interop;

namespace CirDeck.App;

public sealed class LibrarySession
{
    private readonly Dispatcher _dispatcher;
    private bool _pinsMissing;
    private SearchIndex _index;

    private LibrarySession(Dispatcher dispatcher, AppSettings settings, IReadOnlyList<AppEntry> apps, IReadOnlyList<string> pins, bool pinsMissing)
    {
        _dispatcher = dispatcher;
        Settings = settings;
        Apps = apps;
        DockPins = pins;
        _pinsMissing = pinsMissing;
        _index = new SearchIndex(apps);
        if (_pinsMissing && apps.Count > 0)
        {
            SeedPins(apps);
        }
    }

    public AppSettings Settings { get; }

    public IReadOnlyList<AppEntry> Apps { get; private set; }

    public IReadOnlyList<string> DockPins { get; private set; }

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
        return new LibrarySession(dispatcher, settings, apps, pins, pinsMissing);
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

    public void Rescan()
    {
        var thread = new Thread(() =>
        {
            try
            {
                var apps = StartMenuScanner.Scan();
                JsonStore.Save(AppPaths.CatalogPath, apps.ToList());
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
        _index = new SearchIndex(apps);
        if (_pinsMissing)
        {
            SeedPins(apps);
        }

        Changed?.Invoke();
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
