using CirDeck.Core;
using Microsoft.Win32;

namespace CirDeck.Interop;

public static class SteamScanner
{
    public static IReadOnlyList<AppEntry> Scan()
    {
        var found = new List<AppEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in Roots())
        {
            var apps = Path.Combine(root, "steamapps");
            if (!Directory.Exists(apps))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(apps, "appmanifest_*.acf");
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                try
                {
                    var game = SteamCatalog.FromManifest(File.ReadAllText(file));
                    if (game != null && seen.Add(game.TargetPath))
                    {
                        found.Add(game);
                    }
                }
                catch (IOException)
                {
                }
            }
        }

        found.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase));
        return found;
    }

    private static IEnumerable<string> Roots()
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var install = key?.GetValue("SteamPath") as string;
        if (!string.IsNullOrWhiteSpace(install))
        {
            install = install.Replace('/', '\\');
            if (yielded.Add(install))
            {
                yield return install;
            }

            var vdf = Path.Combine(install, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (var path in SteamCatalog.LibraryPaths(File.ReadAllText(vdf)))
                {
                    var normalized = path.Replace('/', '\\');
                    if (yielded.Add(normalized))
                    {
                        yield return normalized;
                    }
                }
            }
        }
    }
}
