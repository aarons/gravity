using System.Reflection;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using static Gravity.MainFile;

namespace Gravity;

// No references to library assemblies: Gravity must load with either library,
// both, or neither. Register after all mod initializers, on the game thread.
internal static class GravityOptionalSettings
{
    private static readonly HashSet<string> Attempted = [];

    internal static void RegisterLoaded()
    {
        foreach (var mod in ModManager.GetLoadedMods())
        {
            var id = mod.manifest?.id;
            if (id is not ("BaseLib" or "STS2-RitsuLib") || !Attempted.Add(id)) continue;
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == id);
                if (assembly == null) throw new InvalidOperationException("Loaded library assembly was not found.");
                if (id == "BaseLib") RegisterBaseLib(assembly);
                else RegisterRitsuLib(assembly);
            }
            catch (Exception error)
            {
                // An absent or incompatible optional API must never prevent playing.
                GD.PushWarning($"[Gravity] Optional {id} settings integration unavailable: {error.GetBaseException().Message}");
            }
        }
    }

    private static void RegisterBaseLib(Assembly assembly)
    {
        var configType = assembly.GetType("BaseLib.Config.ModConfig", true)!;
        var register = assembly.GetType("BaseLib.Config.ModConfigRegistry", true)!
            .GetMethod("Register", [typeof(string), configType])
            ?? throw new MissingMethodException("BaseLib.Config.ModConfigRegistry.Register");
        var adapter = BuildBaseLibAdapter(configType);
        adapter.GetField("BuildUi")!.SetValue(null, (Action<Control>)(host =>
            host.AddChild(new GravitySettingsPanel().Content)));
        register.Invoke(null, [ModId, Activator.CreateInstance(adapter)]);
    }

    internal static Type BuildBaseLibAdapter(Type configType)
    {
        var constructor = configType.GetConstructor([typeof(string)])
            ?? throw new MissingMethodException(configType.FullName, ".ctor(string)");
        var setup = configType.GetMethod("SetupConfigUI", [typeof(Control)])
            ?? throw new MissingMethodException(configType.FullName, "SetupConfigUI");
        var visible = configType.GetMethod("VisibleInModList", Type.EmptyTypes)
            ?? throw new MissingMethodException(configType.FullName, "VisibleInModList");
        if (!setup.IsVirtual || !visible.IsVirtual || visible.ReturnType != typeof(bool))
            throw new NotSupportedException("Unsupported BaseLib custom settings API.");

        // BaseLib requires a ModConfig subclass. Emit only this tiny forwarding
        // adapter once the optional assembly is loaded, avoiding a hard dependency
        // during Harmony's assembly scan. Gravity remains the sole settings store.
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Gravity.BaseLibAdapter"), AssemblyBuilderAccess.Run);
        var type = assembly.DefineDynamicModule("Adapter").DefineType("Gravity.BaseLibSettings",
            TypeAttributes.Public | TypeAttributes.Sealed, configType);
        var buildUi = type.DefineField("BuildUi", typeof(Action<Control>), FieldAttributes.Public | FieldAttributes.Static);
        var ctor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes).GetILGenerator();
        ctor.Emit(OpCodes.Ldarg_0);
        ctor.Emit(OpCodes.Ldstr, "gravity_baselib_panel");
        ctor.Emit(OpCodes.Call, constructor);
        ctor.Emit(OpCodes.Ret);
        var setupMethod = type.DefineMethod(setup.Name, MethodAttributes.Public | MethodAttributes.Virtual,
            typeof(void), [typeof(Control)]);
        var il = setupMethod.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, buildUi);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(Action<Control>).GetMethod("Invoke")!);
        il.Emit(OpCodes.Ret);
        type.DefineMethodOverride(setupMethod, setup);
        var visibleMethod = type.DefineMethod(visible.Name, MethodAttributes.Public | MethodAttributes.Virtual,
            typeof(bool), Type.EmptyTypes);
        il = visibleMethod.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        type.DefineMethodOverride(visibleMethod, visible);
        return type.CreateType()!;
    }

    private static void RegisterRitsuLib(Assembly assembly)
    {
        var framework = assembly.GetType("STS2RitsuLib.RitsuLibFramework", true)!;
        var register = framework.GetMethods().Single(m => m.Name == "RegisterModSettings"
            && m.GetParameters().Length == 3);
        register.Invoke(null, [ModId,
            AdaptAction(register.GetParameters()[1].ParameterType, ConfigureRitsuPage), null]);
    }

    internal static void ConfigureRitsuPage(object page)
    {
        // Newer RitsuLib releases split the API across companion assemblies.
        var withTitle = page.GetType().GetMethod("WithTitle")!;
        var text = withTitle.GetParameters()[0].ParameterType;
        var dynamicText = text.GetMethod("Dynamic", [typeof(Func<string>)])!;
        object Title() => dynamicText.Invoke(null, [(Func<string>)(() => Localize("settings.title"))])!;
        withTitle.Invoke(page, [Title()]);
        page.GetType().GetMethod("WithModDisplayName")!.Invoke(page,
            [text.GetMethod("Literal")!.Invoke(null, [ModId])]);
        var section = page.GetType().GetMethod("AddSection")!;
        section.Invoke(page, ["settings", AdaptAction(section.GetParameters()[1].ParameterType, builder =>
        {
            var custom = builder.GetType().GetMethod("AddCustom")!;
            // Func<object, Control> is contravariant and accepts the library's UI host.
            Func<object, Control> create = _ => new GravitySettingsPanel().Content;
            var factory = Delegate.CreateDelegate(custom.GetParameters()[2].ParameterType, create.Target, create.Method);
            InvokeWithDefaults(custom, builder, "gravity_settings", Title(), factory);
        })]);
    }

    private static Delegate AdaptAction(Type delegateType, Action<object> action) =>
        Delegate.CreateDelegate(delegateType, action.Target, action.Method);

    private static object? InvokeWithDefaults(MethodInfo method, object target, params object?[] arguments)
    {
        var parameters = method.GetParameters();
        return method.Invoke(target, parameters.Select((p, i) => i < arguments.Length ? arguments[i]
            : p.HasDefaultValue ? p.DefaultValue : throw new MissingMethodException(method.Name)).ToArray());
    }
}

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class GravityOptionalSettingsPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix() => GravityOptionalSettings.RegisterLoaded();
}
