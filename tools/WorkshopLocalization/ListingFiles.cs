using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WorkshopLocalization;

internal record Listing(string Language, string GameLanguage, string Title, string Description);

internal static class ListingFiles
{
    // Steam buffer sizes include the terminating NUL. Keep validation byte-based.
    // https://partner.steamgames.com/doc/api/ISteamRemoteStorage#Constants
    private const int TitleBufferSize = 129;
    private const int DescriptionBufferSize = 8000;

    public static Dictionary<string, string> ReadLanguages()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SupportedLanguages.json")!;
        using var document = JsonDocument.Parse(stream);
        var catalog = ReadObject(document.RootElement, "supported-languages.json");
        if (catalog.Count != 14 || catalog.GetValueOrDefault("eng") != "english"
            || catalog.Values.Distinct(StringComparer.Ordinal).Count() != catalog.Count
            || catalog.Any(pair => !Regex.IsMatch(pair.Key, "^[a-z]{3}$")
                || !Regex.IsMatch(pair.Value, "^[a-z]+$")))
            throw new InvalidDataException("Expected the 14 game locales mapped to unique Steam language codes.");
        return catalog;
    }

    public static List<Listing> ReadListings(string workspace, Dictionary<string, string> catalog)
    {
        var gameBySteam = catalog.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
        var listings = new List<Listing>();
        var directory = Path.Combine(workspace, "localizations");
        foreach (var path in Directory.GetFiles(directory).Order(StringComparer.Ordinal))
        {
            if (!Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
                continue;
            var language = Path.GetFileNameWithoutExtension(path);
            if (!gameBySteam.TryGetValue(language, out var gameLanguage)
                || Path.GetExtension(path) != ".json")
                throw new InvalidDataException($"{path}: unsupported locale filename; use a supported Steam API code with .json.");

            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var fields = ReadObject(document.RootElement, path);
            if (fields.Count != 2 || !fields.ContainsKey("title") || !fields.ContainsKey("description"))
                throw new InvalidDataException($"{path}: expected exactly title and description.");
            ValidateText(fields["title"], TitleBufferSize, $"{path}: title");
            ValidateText(fields["description"], DescriptionBufferSize, $"{path}: description");
            listings.Add(new Listing(language, gameLanguage, fields["title"], fields["description"]));
        }
        if (!listings.Any(listing => listing.Language == "english"))
            throw new InvalidDataException("localizations/english.json is required as the translation source.");
        return listings;
    }

    public static ulong? ReadItemId(string workspace, bool required)
    {
        var path = Path.Combine(workspace, "mod_id.txt");
        if (!File.Exists(path) && !required)
            return null; // Preflight must also work before the first content upload.
        var text = File.ReadAllText(path).Trim();
        if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0)
            throw new InvalidDataException($"{path}: expected a nonzero unsigned 64-bit Workshop item ID.");
        return id;
    }

    private static Dictionary<string, string> ReadObject(JsonElement root, string source)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{source}: expected a JSON object.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"{source}: {property.Name} must be a string.");
            if (!result.TryAdd(property.Name, property.Value.GetString()!))
                throw new InvalidDataException($"{source}: duplicate key {property.Name}.");
        }
        return result;
    }

    private static void ValidateText(string text, int bufferSize, string source)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains('\0'))
            throw new InvalidDataException($"{source} must be nonempty text without NUL characters.");
        if (Encoding.UTF8.GetByteCount(text) >= bufferSize)
            throw new InvalidDataException($"{source} exceeds {bufferSize - 1} UTF-8 bytes.");
    }
}
