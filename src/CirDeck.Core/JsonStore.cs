using System.Text.Json;
using System.Text.Json.Serialization;

namespace CirDeck.Core;

public static class JsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static T Load<T>(string path, Func<T> fallback)
    {
        try
        {
            if (!File.Exists(path))
            {
                return fallback();
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, Options) ?? fallback();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return fallback();
        }
    }

    public static void Save<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, overwrite: true);
    }
}
