using CirDeck.Core;

namespace CirDeck.Core.Tests;

internal static class LibraryTests
{
    public static void Run()
    {
        var chrome = new AppEntry("Google Chrome", @"C:\chrome.exe", "", "", "StartMenu");
        var wechat = new AppEntry("微信", @"C:\Weixin.exe", "", "", "StartMenu");
        var code = new AppEntry("Visual Studio Code", @"C:\code.exe", "", "", "StartMenu");
        var library = new UserLibrary();
        library.Edit(wechat.TargetPath).Hidden = true;
        library.Edit(chrome.TargetPath).DisplayName = "浏览器";
        var folder = new FolderRecord { Id = "a1", Name = "办公", Targets = new List<string> { code.TargetPath } };
        library.Folders.Add(folder);
        library.Order.Add(LibraryBoard.FolderPrefix + "a1");
        library.Order.Add(chrome.TargetPath);

        var board = LibraryBoard.Compose(new[] { chrome, wechat, code }, library, null);
        TestHost.Check(board.Count == 2 && board[0].IsFolder && board[0].Name == "办公", "folder leads the board");
        TestHost.Check(board[0].Children.Count == 1 && board[0].Children[0].App!.TargetPath == code.TargetPath, "folder contains code");
        TestHost.Check(board[1].Name == "浏览器", "renamed chrome");
        TestHost.Check(board.All(tile => tile.App?.TargetPath != wechat.TargetPath && tile.Children.All(child => child.App?.TargetPath != wechat.TargetPath)), "hidden wechat");

        var found = LibraryBoard.Compose(new[] { chrome, wechat, code }, library, "浏览器");
        TestHost.Check(found.Count == 1 && found[0].Name == "浏览器", "search uses the display name");

        LibraryBoard.Move(library, chrome.TargetPath, LibraryBoard.FolderPrefix + "a1", null);
        TestHost.Check(library.Order[0] == chrome.TargetPath, "drag moves a tile before the target");

        TestHost.Check(HotCorner.Contains("TopLeft", 2, 3, 0, 0, 1000, 800), "top left corner");
        TestHost.Check(!HotCorner.Contains("TopLeft", 40, 3, 0, 0, 1000, 800), "outside the corner");
        TestHost.Check(HotCorner.Contains("BottomRight", 996, 796, 0, 0, 1000, 800), "bottom right corner");

        var vdf = "\"libraryfolders\" { \"0\" { \"path\" \"C:\\\\Program Files (x86)\\\\Steam\" } }";
        var paths = SteamCatalog.LibraryPaths(vdf);
        TestHost.Check(paths.Count == 1 && paths[0].Contains("Steam"), "steam library path");
        var game = SteamCatalog.FromManifest("\"AppState\" { \"appid\" \"730\" \"name\" \"Counter-Strike 2\" }");
        TestHost.Check(game != null && game!.TargetPath == "steam://rungameid/730" && game.Name == "Counter-Strike 2", "steam manifest");

        var root = Path.Combine(Path.GetTempPath(), "cirdeck-layout-" + Guid.NewGuid().ToString("n"));
        try
        {
            var bundle = new WorkspaceBundle
            {
                Name = "家里",
                DockPins = new List<string> { chrome.TargetPath },
                Library = library,
                Settings = new AppSettings { Columns = 6 }
            };
            var file = Path.Combine(root, "layout.json");
            JsonStore.Save(file, bundle);
            var loaded = JsonStore.Load(file, () => new WorkspaceBundle());
            TestHost.Check(loaded.Name == "家里" && loaded.DockPins.Count == 1 && loaded.Library.Folders.Count == 1 && loaded.Settings.Columns == 6, "workspace roundtrip");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
