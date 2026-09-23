using Gravity;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

internal static class NeowTests
{
    public static void Run()
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
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
        var run = RunState.CreateForNewRun([player], [], modifiers, GameMode.Standard,
            0, "GRAVITYTEST");
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
