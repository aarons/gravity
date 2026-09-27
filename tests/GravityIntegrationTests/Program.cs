using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;

var data = Environment.GetEnvironmentVariable("GRAVITY_TEST_GAME_DATA") ?? Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(attribute => attribute.Key == "Sts2DataDir").Value!;
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = Path.Combine(data, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
Run(args.Contains("--map-only"));

[MethodImpl(MethodImplOptions.NoInlining)]
static void Run(bool mapOnly)
{
    var harmony = new Harmony("Gravity.IntegrationTests");
    var assembly = Assembly.Load("Gravity");
    if (mapOnly)
    {
        foreach (var type in new[] { typeof(Gravity.TravelChoicesPatch), typeof(Gravity.VisitHistoryPatch),
            typeof(Gravity.ResumedMapRevealPatch) })
            harmony.CreateClassProcessor(type).Patch();
        SettingsTests.InitializeModels();
        GameRulesTests.Run();
        harmony.UnpatchAll(harmony.Id);
        return;
    }
    harmony.PatchAll(assembly);
    var patched = harmony.GetPatchedMethods().ToArray();
    Console.WriteLine($"Successfully applied {patched.Length} Gravity patches to the installed game assemblies.");
    if (patched.Length < 15) throw new Exception("Missing game patches");
    SettingsTests.InitializeModels();
    GameRulesTests.Run();
    SettingsTests.Run();
    NeowTests.Run();
    LobbyLifecycleTests.Run();
    OptionalSettingsTests.Run();
    harmony.UnpatchAll(harmony.Id);
}
