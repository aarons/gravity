using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Godot;
using Gravity;

internal static class OptionalSettingsTests
{
    public static void Run()
    {
        var references = typeof(MainFile).Assembly.GetReferencedAssemblies();
        Check(!references.Any(a => a.Name is "BaseLib" or "STS2-RitsuLib"),
            "Optional libraries must not become assembly dependencies");
        CheckAdapter(typeof(TestModConfig));
        foreach (var (variable, name) in new[] { ("GRAVITY_BASELIB_DLL", "BaseLib"), ("GRAVITY_RITSULIB_DLL", "STS2-RitsuLib") })
        {
            var path = System.Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrEmpty(path)) continue;
            var directories = new List<string>();
            var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!);
            for (var depth = 0; directory != null && depth < 3; depth++, directory = directory.Parent)
            {
                directories.Add(directory.FullName);
                directories.Add(Path.Combine(directory.FullName, "shared"));
            }
            AssemblyLoadContext.Default.Resolving += (context, dependency) =>
            {
                var candidate = directories.Select(d => Path.Combine(d, dependency.Name + ".dll")).FirstOrDefault(File.Exists);
                return candidate == null ? null : context.LoadFromAssemblyPath(candidate);
            };
            var assembly = Assembly.LoadFrom(Path.GetFullPath(path));
            Check(assembly.GetName().Name == name, "Unexpected optional library assembly name");
            if (name == "BaseLib")
            {
                var type = assembly.GetType("BaseLib.Config.ModConfig", true)!;
                CheckAdapter(type);
                Check(assembly.GetType("BaseLib.Config.ModConfigRegistry", true)!
                    .GetMethod("Register", [typeof(string), type]) != null, "Missing BaseLib registration API");
            }
            else
            {
                Check(assembly.GetType("STS2RitsuLib.RitsuLibFramework", true)!.GetMethods()
                    .Any(m => m.Name == "RegisterModSettings" && m.GetParameters().Length == 3),
                    "Missing RitsuLib registration API");
                var builderType = assembly.GetType("STS2RitsuLib.Settings.ModSettingsPageBuilder", true)!;
                var builder = Activator.CreateInstance(builderType, "Gravity", null)!;
                GravityOptionalSettings.ConfigureRitsuPage(builder);
                var sections = ((IEnumerable)builderType.GetField("_sections", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(builder)!).Cast<object>().ToArray();
                Check(sections.Length == 1, "Expected one Gravity section");
                var entries = ((IEnumerable)sections[0].GetType().GetProperty("Entries")!.GetValue(sections[0])!)
                    .Cast<object>().ToArray();
                Check(entries.Length == 1 && entries[0].GetType().Name == "CustomModSettingsEntryDefinition",
                    "RitsuLib must host the shared custom panel");
                Check(entries[0].GetType().GetProperty("ControlFactory")!.GetValue(entries[0]) is Delegate,
                    "Custom panel factory was not bound");
            }
            Console.WriteLine($"Passed optional settings API checks against {name}: {path}");
        }
        Console.WriteLine("Passed settings adapter dispatch and optional assembly dependency checks.");
    }

    private static void CheckAdapter(Type baseType)
    {
        var adapter = GravityOptionalSettings.BuildBaseLibAdapter(baseType);
        var called = false;
        adapter.GetField("BuildUi")!.SetValue(null, (Action<Control>)(_ => called = true));
        // Skip BaseLib's disk-writing constructor and native Godot UI creation.
        var instance = RuntimeHelpers.GetUninitializedObject(adapter);
        Check((bool)baseType.GetMethod("VisibleInModList")!.Invoke(instance, null)!,
            "Custom settings must be visible without BaseLib-managed properties");
        baseType.GetMethod("SetupConfigUI")!.Invoke(instance, [null]);
        Check(called, "BaseLib virtual UI callback must dispatch to Gravity");
        Check(!adapter.GetProperties().Any(p => p.GetMethod?.IsStatic == true),
            "BaseLib must not maintain a second set of Gravity preferences");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}

public abstract class TestModConfig
{
    protected TestModConfig() { }
    public TestModConfig(string filename) { }
    public abstract void SetupConfigUI(Control host);
    public virtual bool VisibleInModList() => false;
}
