using Godot;
using Gravity;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

// Run only in a disposable game copy/profile; this starts and saves a custom run.
[ModInitializer(nameof(Init))]
public static class RunSetupTests
{
    private static bool started;

    public static void Init() => new Harmony("Gravity.RunSetupTests").Patch(
        AccessTools.Method(typeof(NMainMenu), "_Ready"),
        postfix: new HarmonyMethod(typeof(RunSetupTests), nameof(Ready)));

    private static void Ready(NMainMenu __instance)
    {
        if (started) return;
        started = true;
        _ = Run(__instance);
    }

    private static async Task Frames(SceneTree tree, int count = 15)
    {
        for (var i = 0; i < count; i++) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    private static async Task Run(NMainMenu menu)
    {
        var tree = menu.GetTree();
        try
        {
            await Frames(tree);
            if (System.Environment.GetEnvironmentVariable("GRAVITY_SETUP_RESUME") == "1")
            {
                GravitySettings.Enabled = true;
                var saved = SaveManager.Instance.LoadRunSave().SaveData ?? throw new Exception("No run to resume");
                var resumed = RunState.FromSerializable(saved);
                Require(GravityRunSettings.GetSnapshot(resumed).Disabled, "Process restart enabled Gravity");
                Require(!GravityRules.Applies(resumed), "Process restart changed map rules");
                GD.Print("RUN SETUP TEST PASS disabled disk restore after process restart");
                tree.Quit();
                return;
            }
            var screen = menu.SubmenuStack.GetSubmenuType<NCustomRunScreen>();
            var portraits = (Control)AccessTools.Field(typeof(NCustomRunScreen), "_charButtonContainer").GetValue(screen)!;
            foreach (var scrolling in new[] { false, true })
            {
                if (scrolling) ReparentPortraits(portraits);
                screen.InitializeSingleplayer();
                menu.SubmenuStack.Push(screen);
                await Frames(tree);
                var toggle = CheckScreen(screen, portraits);
                toggle.ButtonPressed = false;
                menu.SubmenuStack.Pop();
                await Frames(tree);
                screen.InitializeSingleplayer();
                menu.SubmenuStack.Push(screen);
                await Frames(tree);
                toggle = CheckScreen(screen, portraits);
                Require(!toggle.ButtonPressed && !GravitySettings.Enabled, "Reopening lost disabled choice");
                if (!scrolling) menu.SubmenuStack.Pop();
            }

            if (System.Environment.GetEnvironmentVariable("GRAVITY_SETUP_SCREENSHOT") is { } screenshot)
            {
                await menu.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                menu.GetViewport().GetTexture().GetImage().SavePng(screenshot);
            }

            // Exercise the real screen, run initialization and disk save path.
            AccessTools.Method(typeof(NCustomRunScreen), "OnEmbarkPressed").Invoke(screen, [null]);
            await Frames(tree, 240);
            var manager = RunManager.Instance;
            var run = manager.DebugOnlyGetState() ?? throw new Exception("Custom run did not start");
            Require(GravityRunSettings.GetSnapshot(run).Disabled, "New custom run enabled Gravity");
            Require(!GravityRules.Applies(run), "Disabled run changed map rules");
            await SaveManager.Instance.SaveRun(null);
            var save = SaveManager.Instance.LoadRunSave().SaveData ?? throw new Exception("No run saved");

            // Also cover save rewrites before RunState.FromSerializable is called.
            var rewritten = JsonSerializationUtility.FromJson<SerializableRun>(JsonSerializationUtility.ToJson(save)).SaveData!;
            GravitySettings.Enabled = true;
            var restored = RunState.FromSerializable(rewritten);
            Require(GravityRunSettings.GetSnapshot(restored).Disabled, "Disk resume enabled Gravity");
            Require(!GravityRules.Applies(restored), "Resumed run changed map rules");
            GD.Print("RUN SETUP TEST PASS custom screen, scrolling portraits, reopening, disabled startup and disk restore");
            tree.Quit();
        }
        catch (Exception error)
        {
            GD.PrintErr("RUN SETUP TEST FAIL " + error);
            tree.Quit(1);
        }
    }

    private static CheckButton CheckScreen(NCustomRunScreen screen, Control portraits)
    {
        var toggle = screen.GetNode<CheckButton>("GravityRunToggle");
        Require(screen.IsVisibleInTree(), "Custom screen is blank");
        Require(toggle.IsVisibleInTree() && !toggle.Disabled, "Toggle unavailable");
        Require(screen.GetGlobalRect().Encloses(toggle.GetGlobalRect()), "Toggle is off-screen");
        Require(screen.GetNode<Control>("%ModifiersList").IsVisibleInTree(), "Modifiers hidden");
        Require(screen.GetNode<Control>("%SeedInput").IsVisibleInTree(), "Seed input hidden");
        Require(screen.GetChildren().Count(n => n.Name == "GravityRunToggle") == 1, "Duplicate toggle");
        foreach (var portrait in portraits.GetChildren().OfType<NCharacterSelectButton>().Where(p => p.Visible))
        {
            Require(!toggle.GetGlobalRect().Intersects(portrait.GetGlobalRect()), "Toggle overlaps portraits");
            if (!portrait.IsLocked)
                Require(portrait.GetNode(portrait.FocusNeighborBottom) == toggle, "Controller cannot reach toggle");
        }
        return toggle;
    }

    private static void ReparentPortraits(Control portraits)
    {
        // Reproduce BaseLib's >5-character custom-screen layout without depending
        // on BaseLib or a particular character mod. The game's field stays valid;
        // the original node path no longer exists.
        var clip = new Control
        {
            Name = "ButtonScrollContainer", ClipContents = true,
            AnchorLeft = .5f, AnchorRight = .5f, AnchorTop = .5f, AnchorBottom = .5f,
            OffsetLeft = -330, OffsetRight = 330, OffsetTop = -177, OffsetBottom = -10,
        };
        portraits.GetParent().AddChild(clip);
        portraits.Reparent(clip, false);
        portraits.AnchorLeft = portraits.AnchorRight = portraits.AnchorTop = portraits.AnchorBottom = 0;
        portraits.Position = Vector2.Zero;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
