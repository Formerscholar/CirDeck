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

    public static string LogPath => Path.Combine(LocalRoot, "cirdeck.log");
}
