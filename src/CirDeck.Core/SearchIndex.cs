namespace CirDeck.Core;

public sealed class SearchIndex
{
    private readonly Row[] _rows;

    public SearchIndex(IReadOnlyList<AppEntry> apps, Func<string, string>? initials = null)
    {
        initials ??= PinyinInitials.Of;
        _rows = new Row[apps.Count];
        for (var i = 0; i < apps.Count; i++)
        {
            var name = apps[i].Name ?? string.Empty;
            var fold = name.Trim().ToLowerInvariant();
            _rows[i] = new Row(apps[i], fold, (initials(name) ?? string.Empty).ToLowerInvariant(), WordInitials(fold));
        }
    }

    public IReadOnlyList<AppEntry> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            var all = new AppEntry[_rows.Length];
            for (var i = 0; i < _rows.Length; i++)
            {
                all[i] = _rows[i].App;
            }

            return all;
        }

        var needle = query.Trim().ToLowerInvariant();
        var hits = new List<Hit>(_rows.Length);
        for (var i = 0; i < _rows.Length; i++)
        {
            var score = Score(_rows[i], needle);
            if (score > 0)
            {
                hits.Add(new Hit(score, i));
            }
        }

        hits.Sort(static (left, right) =>
        {
            var byScore = right.Score.CompareTo(left.Score);
            return byScore != 0 ? byScore : left.Index.CompareTo(right.Index);
        });

        var result = new AppEntry[hits.Count];
        for (var i = 0; i < hits.Count; i++)
        {
            result[i] = _rows[hits[i].Index].App;
        }

        return result;
    }

    private static int Score(in Row row, string query)
    {
        if (row.NameFold == query)
        {
            return 1000;
        }

        if (row.NameFold.StartsWith(query, StringComparison.Ordinal))
        {
            return 800;
        }

        if (row.Initials.Length > 0 && row.Initials.StartsWith(query, StringComparison.Ordinal))
        {
            return 760;
        }

        if (row.WordInitials.Length > 0 && row.WordInitials.StartsWith(query, StringComparison.Ordinal))
        {
            return 720;
        }

        if (row.NameFold.Contains(query, StringComparison.Ordinal))
        {
            return 500;
        }

        if (row.Initials.Contains(query, StringComparison.Ordinal))
        {
            return 420;
        }

        if (row.WordInitials.Contains(query, StringComparison.Ordinal))
        {
            return 400;
        }

        return 0;
    }

    private static string WordInitials(string fold)
    {
        var buffer = new char[fold.Length];
        var count = 0;
        var atBoundary = true;
        foreach (var character in fold)
        {
            var letter = character is >= 'a' and <= 'z';
            if (letter && atBoundary)
            {
                buffer[count++] = character;
            }

            atBoundary = !letter;
        }

        return new string(buffer, 0, count);
    }

    private readonly record struct Row(AppEntry App, string NameFold, string Initials, string WordInitials);

    private readonly record struct Hit(int Score, int Index);
}
