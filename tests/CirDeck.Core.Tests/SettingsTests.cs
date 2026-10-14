using CirDeck.Core;

namespace CirDeck.Core.Tests;

internal static class SettingsTests
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "cirdeck-tests-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "settings.json");
            var settings = new AppSettings { Columns = 0, DockMagnifyScale = 9 };
            settings.Clamp();
            TestHost.Check(settings.Columns == 4 && Math.Abs(settings.DockMagnifyScale - 2.2) < 0.001, "clamp");

            settings.Columns = 8;
            settings.DockMagnifyScale = 1.4;
            JsonStore.Save(path, settings);
            var loaded = JsonStore.Load(path, () => new AppSettings());
            TestHost.Check(loaded.Columns == 8 && Math.Abs(loaded.DockMagnifyScale - 1.4) < 0.001, "settings roundtrip");

            File.WriteAllText(path, "{ not json");
            var fallback = JsonStore.Load(path, () => new AppSettings());
            TestHost.Check(fallback.Columns == 7, "corrupt settings fall back");

            var catalogPath = Path.Combine(root, "catalog.json");
            var apps = new List<AppEntry>
            {
                new("记事本", @"C:\Windows\notepad.exe", "", @"C:\Windows", "StartMenu")
            };
            JsonStore.Save(catalogPath, apps);
            var catalog = JsonStore.Load(catalogPath, () => new List<AppEntry>());
            TestHost.Check(catalog.Count == 1 && catalog[0].Name == "记事本" && catalog[0].TargetPath.EndsWith("notepad.exe"), "catalog roundtrip");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
