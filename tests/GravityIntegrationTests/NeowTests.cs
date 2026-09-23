using Gravity;
using HarmonyLib;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

internal static class NeowTests
{
    public static void Run()
    {
        // Keep the actual Neow selection method and RNG. Stand in for scene-backed
        // player inventories, localized option UI, and the base Ancient description.
        var fixture = new Harmony("Gravity.NeowFixture");
        fixture.Patch(AccessTools.Method(typeof(EventOption), nameof(EventOption.FromRelic)),
            prefix: new HarmonyMethod(typeof(NeowTests), nameof(RelicOption)));
        fixture.Patch(AccessTools.PropertyGetter(typeof(AncientEventModel), nameof(AncientEventModel.InitialDescription)),
            prefix: new HarmonyMethod(typeof(NeowTests), nameof(AncientDescription)));
        foreach (var method in typeof(RelicModel).Assembly.GetTypes()
            .Where(type => typeof(RelicModel).IsAssignableFrom(type))
            .Select(type => AccessTools.DeclaredMethod(type, nameof(RelicModel.IsAllowedAtNeow)))
            .Where(method => method != null))
            fixture.Patch(method, prefix: new HarmonyMethod(typeof(NeowTests), nameof(AllowRelic)));
        try { VerifyChoices(); }
        finally { fixture.UnpatchAll(fixture.Id); }
    }

    private static bool RelicOption(RelicModel relic, ref EventOption __result)
    {
        __result = ((EventOption)RuntimeHelpers.GetUninitializedObject(typeof(EventOption))).WithRelic(relic);
        return false;
    }

    private static bool AncientDescription(ref LocString __result)
    {
        __result = new LocString("ancients", "NEOW.pages.INITIAL.description");
        return false;
    }

    private static bool AllowRelic(ref bool __result)
    {
        __result = true;
        return false;
    }

    private static void VerifyChoices()
    {
        var baseline = Generate([]);
        var settings = GravitySettingsModifier.Create(15, false);
        var gravity = Generate([settings]);
        Check(baseline.Options.Count == 3 && gravity.Options.Count == 3,
            "Gravity must retain Neow's three normal choices");
        Check(baseline.Options.Select(o => o.Relic!.Id).SequenceEqual(gravity.Options.Select(o => o.Relic!.Id)),
            "Gravity must preserve Neow's seeded relic choices");
        Check(baseline.Description == gravity.Description, "Gravity must preserve Neow's normal description");
        var other = (OtherSettingsModifier)ModelDb.Modifier<OtherSettingsModifier>().ToMutable();
        var custom = Generate([other]);
        var mixed = Generate([other, settings]);
        Check(custom.Options.Count == 0 && mixed.Options.Count == 0,
            "Other modifiers must retain the game's modifier-specific Neow behavior");
        Check(custom.Description == mixed.Description && custom.Description != baseline.Description,
            "Other modifiers must retain Neow's modifier-specific description");
        Console.WriteLine("Passed Neow normal choices, seeded rewards, descriptions and modifier coexistence checks.");
    }

    private static (IReadOnlyList<EventOption> Options, string Description) Generate(IReadOnlyList<ModifierModel> modifiers)
    {
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        var run = SettingsTests.NewState(modifiers);
        AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
        var neow = (Neow)ModelDb.Event<Neow>().ToMutable();
        AccessTools.Property(typeof(EventModel), nameof(EventModel.Owner)).SetValue(neow, player);
        AccessTools.Property(typeof(EventModel), nameof(EventModel.Rng)).SetValue(neow, new Rng(123));
        var options = (IReadOnlyList<EventOption>)AccessTools.Method(typeof(Neow), "GenerateInitialOptions").Invoke(neow, null)!;
        Check(run.Modifiers.SequenceEqual(modifiers), "Neow must not remove settings from the saved run");
        return (options, neow.InitialDescription.LocEntryKey);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
