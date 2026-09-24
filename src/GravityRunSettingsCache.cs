using System.Text.Json;
using Godot;

namespace Gravity;

// Clients do not write native multiplayer saves. Remember their most recent
// run separately from preferences, including across game restarts.
internal static class GravityRunSettingsCache
{
    private sealed record CachedRun(ulong Host, string Seed, GravitySettingsSnapshot Settings);
    private static string CachePath => ProjectSettings.GlobalizePath("user://gravity_run_settings.json");

    internal static GravitySettingsSnapshot? Read(ulong host, string seed)
    {
        try
        {
            if (!File.Exists(CachePath)) return null;
            var run = JsonSerializer.Deserialize<CachedRun>(File.ReadAllText(CachePath));
            return run is { Settings.IsValid: true } && run.Host == host && run.Seed == seed
                ? run.Settings : null;
        }
        catch (Exception error)
        {
            Console.WriteLine($"[Gravity] Could not read remembered run settings: {error.Message}");
            return null;
        }
    }

    internal static void Store(ulong host, string seed, GravitySettingsSnapshot snapshot)
    {
        try
        {
            File.WriteAllText(CachePath, JsonSerializer.Serialize(new CachedRun(host, seed, snapshot)));
        }
        catch (Exception error)
        {
            Console.WriteLine($"[Gravity] Could not remember run settings: {error.Message}");
        }
    }
}
