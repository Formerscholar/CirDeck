namespace CirDeck.Core;

/// <summary>
/// First pinyin letter for CJK unified ideographs. The table is derived from
/// mozillazg/pinyin-data (MIT), keeping only the first reading of each character.
/// </summary>
public static class PinyinInitials
{
    private const int Start = 0x4E00;
    private static readonly byte[] Table = Load();

    public static char? Lookup(char character)
    {
        var index = character - Start;
        if ((uint)index >= (uint)Table.Length)
        {
            return null;
        }

        var value = Table[index];
        if (value == 0)
        {
            return null;
        }

        return (char)('a' + value - 1);
    }

    public static string Of(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var buffer = new char[text.Length];
        var count = 0;
        foreach (var character in text)
        {
            var initial = Lookup(character);
            if (initial == null)
            {
                continue;
            }

            buffer[count++] = initial.Value;
        }

        return new string(buffer, 0, count);
    }

    private static byte[] Load()
    {
        using var stream = typeof(PinyinInitials).Assembly.GetManifestResourceStream("CirDeck.Core.pinyin-initials.bin");
        if (stream == null)
        {
            return Array.Empty<byte>();
        }

        var buffer = new byte[stream.Length];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer, read, buffer.Length - read);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        return buffer;
    }
}
