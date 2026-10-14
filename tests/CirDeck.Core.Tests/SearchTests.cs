using CirDeck.Core;

namespace CirDeck.Core.Tests;

internal static class SearchTests
{
    public static void Run()
    {
        var chrome = new AppEntry("Google Chrome", @"C:\chrome.exe", "", "", "StartMenu");
        var code = new AppEntry("Visual Studio Code", @"C:\code.exe", "", "", "StartMenu");
        var wechat = new AppEntry("微信", @"C:\Weixin.exe", "", "", "StartMenu");
        var apps = new[] { chrome, code, wechat };
        var index = new SearchIndex(apps, name => name == "微信" ? "wx" : string.Empty);

        var all = index.Search("  ");
        TestHost.Check(all.Count == 3 && all[0] == chrome && all[2] == wechat, "blank query keeps order");

        var byText = index.Search("chr");
        TestHost.Check(byText.Count == 1 && byText[0] == chrome, "substring match");

        var byWord = index.Search("vsc");
        TestHost.Check(byWord.Count == 1 && byWord[0] == code, "word initials");

        var byPinyin = index.Search("WX");
        TestHost.Check(byPinyin.Count == 1 && byPinyin[0] == wechat, "pinyin initials");

        var prefixBeats = index.Search("code");
        TestHost.Check(prefixBeats.Count == 1 && prefixBeats[0] == code, "contains code");
        TestHost.Check(index.Search("zzz").Count == 0, "miss");

        TestHost.Check(PinyinInitials.Of("微信") == "wx", "embedded pinyin for 微信");
        TestHost.Check(PinyinInitials.Of("Chrome") == string.Empty, "latin is not pinyin");
        TestHost.Check(PinyinInitials.Lookup('中') == 'z', "zhong initial");
    }
}
