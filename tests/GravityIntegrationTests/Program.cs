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
Run();

[MethodImpl(MethodImplOptions.NoInlining)]
static void Run()
{
    var harmony = new Harmony("Gravity.IntegrationTests");
    var assembly = Assembly.Load("Gravity");
    harmony.PatchAll(assembly);
    var patched = harmony.GetPatchedMethods().ToArray();
    Console.WriteLine($"Successfully applied {patched.Length} Gravity patches to the installed game assemblies.");
    if (patched.Length < 15) throw new Exception("Missing game patches");
    SettingsTests.InitializeModels();
    GameRulesTests.Run();
    SettingsTests.Run();
    LobbyLifecycleTests.Run();
    OptionalSettingsTests.Run();
    harmony.UnpatchAll(harmony.Id);
}
