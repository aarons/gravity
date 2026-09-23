using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;

namespace Gravity;

[ModInitializer(nameof(Initialize))]
public static class MainFile
{
    public const string ModId = "Gravity";

    public static void Initialize()
    {
        GravitySettings.Load();
        new Harmony(ModId).PatchAll(typeof(MainFile).Assembly);
        GD.Print($"[{ModId}] {Localize("mod.loaded")}");
    }

    // Read the language at lookup time so newly created UI follows game settings.
    internal static string Localize(string key) =>
        Localization.Get(key, LocManager.Instance?.Language ?? "eng");
}
