using CirDeck.Core;
using CirDeck.Interop;

namespace CirDeck.App;

public sealed class VaultRecord
{
    public string Name { get; set; } = "";

    public string Kind { get; set; } = "File";
}

public static class DesktopVault
{
    public static int Collect(bool apps)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        Directory.CreateDirectory(AppPaths.VaultDirectory);
        var records = Load();
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(desktop))
        {
            var extension = Path.GetExtension(file);
            var isApp = extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".url", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase);
            if (apps != isApp)
            {
                continue;
            }

            var destination = Unique(AppPaths.VaultDirectory, Path.GetFileName(file));
            File.Move(file, destination);
            records.Add(new VaultRecord { Name = Path.GetFileName(destination), Kind = apps ? "App" : "File" });
            count++;
        }

        JsonStore.Save(AppPaths.VaultManifest, records);
        return count;
    }

    public static int Restore()
    {
        if (!Directory.Exists(AppPaths.VaultDirectory))
        {
            return 0;
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(AppPaths.VaultDirectory))
        {
            if (string.Equals(file, AppPaths.VaultManifest, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Move(file, Unique(desktop, Path.GetFileName(file)));
            count++;
        }

        JsonStore.Save(AppPaths.VaultManifest, new List<VaultRecord>());
        return count;
    }

    public static IReadOnlyList<AppEntry> TakenApps()
    {
        if (!Directory.Exists(AppPaths.VaultDirectory))
        {
            return Array.Empty<AppEntry>();
        }

        return StartMenuScanner.Scan(new[] { AppPaths.VaultDirectory });
    }

    private static List<VaultRecord> Load()
    {
        return JsonStore.Load(AppPaths.VaultManifest, () => new List<VaultRecord>());
    }

    private static string Unique(string directory, string name)
    {
        var destination = Path.Combine(directory, name);
        if (!File.Exists(destination))
        {
            return destination;
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var i = 2; i < 100; i++)
        {
            destination = Path.Combine(directory, stem + " (" + i + ")" + extension);
            if (!File.Exists(destination))
            {
                return destination;
            }
        }

        return Path.Combine(directory, Guid.NewGuid().ToString("n") + extension);
    }
}
