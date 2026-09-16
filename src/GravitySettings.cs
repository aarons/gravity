using Godot;

namespace Gravity;

internal static class GravitySettings
{
    private const string Path = "user://gravity.cfg";
    public const int DefaultHue = 43;
    public const int DefaultPulsePercent = 25;
    public static int Hue { get; set; } = DefaultHue;
    public static int PulsePercent { get; set; } = DefaultPulsePercent;
    public enum EncounterMode { None, Default, All, Custom }
    public const int DefaultEncounterCount = 15;
    public const int MaxEncounterCount = 999;
    public static EncounterMode Mode { get; set; } = EncounterMode.Default;
    public static bool LockEncountersAfterBossUnlock { get; set; }
    private static int _customEncounterCount = DefaultEncounterCount;
    public static int CustomEncounterCount
    {
        get => _customEncounterCount;
        set => _customEncounterCount = Math.Clamp(value, 0, MaxEncounterCount);
    }
    public static int NextRunRequirement => Mode switch
    {
        EncounterMode.None => 0,
        EncounterMode.All => -1,
        EncounterMode.Custom => CustomEncounterCount,
        _ => DefaultEncounterCount,
    };

    // Also reads preferences from before the four-choice UI existed.
    internal static void RestoreEncounterPreferences(int encounters, bool enabled, int mode = -1,
        int custom = DefaultEncounterCount)
    {
        CustomEncounterCount = custom;
        if (Enum.IsDefined(typeof(EncounterMode), mode))
        {
            Mode = (EncounterMode)mode;
            return;
        }
        if (encounters > 0 && encounters != DefaultEncounterCount) CustomEncounterCount = encounters;
        Mode = !enabled || encounters == 0 ? EncounterMode.None
            : encounters == -1 ? EncounterMode.All
            : encounters == DefaultEncounterCount ? EncounterMode.Default : EncounterMode.Custom;
    }

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
        var lockEncounters = config.GetValue("settings", "lock_encounters_after_boss_unlock", false);
        LockEncountersAfterBossUnlock = lockEncounters.VariantType == Variant.Type.Bool && lockEncounters.AsBool();
        var encounters = ReadInt("encounters", DefaultEncounterCount, -1, 1000);
        var enabled = config.GetValue("settings", "requirement_enabled", true);
        RestoreEncounterPreferences(encounters, enabled.VariantType != Variant.Type.Bool || enabled.AsBool(),
            ReadInt("encounter_mode", -1, 0, 3),
            ReadInt("custom_encounters", DefaultEncounterCount, 0, MaxEncounterCount));
    }

    public static Error Save()
    {
        using var config = new ConfigFile();
        config.SetValue("settings", "hue", Hue);
        config.SetValue("settings", "pulse_percent", PulsePercent);
        config.SetValue("settings", "encounters", NextRunRequirement);
        config.SetValue("settings", "encounter_mode", (int)Mode);
        config.SetValue("settings", "custom_encounters", CustomEncounterCount);
        config.SetValue("settings", "lock_encounters_after_boss_unlock", LockEncountersAfterBossUnlock);
        var error = config.Save(Path);
        if (error != Error.Ok) GD.PushWarning($"[Gravity] Could not save settings: {error}");
        return error;
    }
}
