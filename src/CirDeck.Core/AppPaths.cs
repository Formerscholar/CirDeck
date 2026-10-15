namespace CirDeck.Core;

public static class AppPaths
{
    public static string RoamingRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CirDeck");

    public static string LocalRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CirDeck");

    public static string SettingsPath => Path.Combine(RoamingRoot, "settings.json");

    public static string DockPath => Path.Combine(RoamingRoot, "dock.json");

    public static string CatalogPath => Path.Combine(LocalRoot, "catalog.json");

    public static string IconCache => Path.Combine(LocalRoot, "icons");

    public static string CustomIcons => Path.Combine(LocalRoot, "custom-icons");

    public static string LogPath => Path.Combine(LocalRoot, "cirdeck.log");

    public static string LibraryPath => Path.Combine(RoamingRoot, "library.json");

    public static string LayoutsDirectory => Path.Combine(RoamingRoot, "layouts");

    public static string VaultDirectory => Path.Combine(RoamingRoot, "vault");

    public static string VaultManifest => Path.Combine(VaultDirectory, "manifest.json");

    public static string SnapshotsDirectory => Path.Combine(RoamingRoot, "snapshots");
}
