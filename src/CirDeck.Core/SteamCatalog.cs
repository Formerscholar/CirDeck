namespace CirDeck.Core;

public static class SteamCatalog
{
    public static IReadOnlyList<string> LibraryPaths(string vdf)
    {
        var paths = new List<string>();
        foreach (var pair in ReadPairs(vdf))
        {
            if (pair.Key.Equals("path", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pair.Value))
            {
                paths.Add(pair.Value);
            }
        }

        return paths;
    }

    public static AppEntry? FromManifest(string acf)
    {
        string? appId = null;
        string? name = null;
        foreach (var pair in ReadPairs(acf))
        {
            if (pair.Key.Equals("appid", StringComparison.OrdinalIgnoreCase))
            {
                appId = pair.Value;
            }
            else if (pair.Key.Equals("name", StringComparison.OrdinalIgnoreCase))
            {
                name = pair.Value;
            }
        }

        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new AppEntry(name, "steam://rungameid/" + appId, string.Empty, string.Empty, "Steam");
    }

    private static IEnumerable<(string Key, string Value)> ReadPairs(string text)
    {
        var quotes = new List<string>();
        var index = 0;
        while (index < text.Length)
        {
            var value = ReadQuoted(text, ref index);
            if (value == null)
            {
                break;
            }

            quotes.Add(value);
        }

        for (var i = 0; i + 1 < quotes.Count; i++)
        {
            if (quotes[i].Equals("path", StringComparison.OrdinalIgnoreCase)
                || quotes[i].Equals("appid", StringComparison.OrdinalIgnoreCase)
                || quotes[i].Equals("name", StringComparison.OrdinalIgnoreCase))
            {
                yield return (quotes[i], quotes[i + 1]);
            }
        }
    }

    private static string? ReadQuoted(string text, ref int index)
    {
        while (index < text.Length && text[index] != '"')
        {
            index++;
        }

        if (index >= text.Length)
        {
            return null;
        }

        index++;
        var start = index;
        while (index < text.Length && text[index] != '"')
        {
            if (text[index] == '\\' && index + 1 < text.Length)
            {
                index += 2;
                continue;
            }

            index++;
        }

        if (index >= text.Length)
        {
            return null;
        }

        var value = text.Substring(start, index - start).Replace("\\\\", "\\", StringComparison.Ordinal);
        index++;
        return value;
    }
}
