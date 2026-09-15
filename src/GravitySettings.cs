using Godot;

namespace Gravity;

internal static class GravitySettings
{
    private const string Path = "user://gravity.cfg";
    public const int DefaultHue = 43;
    public const int DefaultPulsePercent = 25;
    public static int Hue { get; set; } = DefaultHue;
    public static int PulsePercent { get; set; } = DefaultPulsePercent;
    public const int DefaultEncounterCount = 15;
    public const int MaxEncounterCount = 1000;
    private static int _encounterCount = DefaultEncounterCount;
    public static int EncounterCount
    {
        get => _encounterCount;
        set => _encounterCount = Math.Clamp(value, 0, MaxEncounterCount);
    }
    public static int NextRunRequirement => EncounterCount;

    // Convert the former All/disabled preferences to the numeric control.
    internal static int MigrateEncounterCount(int value, bool enabled) => !enabled ? 0
        : value == -1 ? MaxEncounterCount : Math.Clamp(value, 0, MaxEncounterCount);
    public static Color HighlightColor => ColorAt(Hue);
    public static event Action? AppearanceChanged;

    public static Color ColorAt(int hue)
    {
        var gold = new Color("eac477");
        return hue == DefaultHue ? gold : Color.FromHsv(
            Mathf.PosMod(gold.H + (hue - DefaultHue) / 360f, 1f), gold.S, gold.V);
    }

    public static void RefreshAppearance() => AppearanceChanged?.Invoke();

    public static void Load()
    {
        using var config = new ConfigFile();
        var error = config.Load(Path);
        if (error == Error.FileNotFound) return;
        if (error != Error.Ok) { GD.PushWarning($"[Gravity] Could not load settings: {error}"); return; }
        int ReadInt(string key, int fallback, int min, int max)
        {
            var value = config.GetValue("settings", key, fallback);
            return value.VariantType == Variant.Type.Int && value.AsInt64() >= min && value.AsInt64() <= max
                ? value.AsInt32() : fallback;
        }
        Hue = ReadInt("hue", DefaultHue, 0, 360);
        PulsePercent = ReadInt("pulse_percent", DefaultPulsePercent, 0, 100);
        var encounters = ReadInt("encounters", DefaultEncounterCount, -1, MaxEncounterCount);
        var enabled = config.GetValue("settings", "requirement_enabled", true);
        EncounterCount = MigrateEncounterCount(encounters,
            enabled.VariantType != Variant.Type.Bool || enabled.AsBool());
    }

    public static Error Save()
    {
        using var config = new ConfigFile();
        config.SetValue("settings", "hue", Hue);
        config.SetValue("settings", "pulse_percent", PulsePercent);
        config.SetValue("settings", "encounters", EncounterCount);
        var error = config.Save(Path);
        if (error != Error.Ok) GD.PushWarning($"[Gravity] Could not save settings: {error}");
        return error;
    }
}
