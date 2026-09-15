using System.Reflection;
using System.Text.Json;

namespace Gravity;

internal static class Localization
{
    private const string ResourcePrefix = "Gravity.Localization.";
    private static readonly Dictionary<string, Dictionary<string, string>> Translations = LoadTranslations();

    public static string Get(string key, string language)
    {
        if (Translations.TryGetValue(language, out var translation)
            && translation.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text))
            return text;

        return Translations["eng"].GetValueOrDefault(key, key);
    }

    private static Dictionary<string, Dictionary<string, string>> LoadTranslations()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var translations = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                || !resource.EndsWith(".json", StringComparison.Ordinal))
                continue;

            var language = resource[ResourcePrefix.Length..^".json".Length];
            using var stream = assembly.GetManifestResourceStream(resource)!;
            translations.Add(language, JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!);
        }
        return translations;
    }
}
