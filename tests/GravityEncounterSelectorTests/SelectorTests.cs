using Godot;
using Gravity;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

// This fixture starts a run and alters its map. Use a disposable copy/profile only.
[ModInitializer(nameof(Init))]
public static class SelectorTests
{
    private static bool _started;
    private static readonly List<NMapPoint> Selected = [];

    public static void Init()
    {
        var harmony = new Harmony("Gravity.EncounterSelectorTests");
        harmony.Patch(AccessTools.Method(typeof(NMainMenu), "_Ready"),
            postfix: new HarmonyMethod(typeof(SelectorTests), nameof(Ready)));
        // Capture the native selection boundary without actually entering 100 rooms.
        harmony.Patch(AccessTools.Method(typeof(NMapScreen), nameof(NMapScreen.OnMapPointSelectedLocally)),
            prefix: new HarmonyMethod(typeof(SelectorTests), nameof(Capture)) { priority = Priority.First });
    }

    private static bool Capture(NMapPoint point) { Selected.Add(point); return false; }
    private static void Ready(NMainMenu __instance)
    {
        if (_started) return;
        _started = true;
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
            GravitySettings.Enabled = true;
            var setup = menu.SubmenuStack.GetSubmenuType<NCustomRunScreen>();
            setup.InitializeSingleplayer();
            menu.SubmenuStack.Push(setup);
            await Frames(tree);
            AccessTools.Method(typeof(NCustomRunScreen), "OnEmbarkPressed").Invoke(setup, [null]);
            await Frames(tree, 240);
            for (var i = 0; i < 1200 && (NMapScreen.Instance == null || GravityMapView.Get(NMapScreen.Instance) == null); i++)
                await Frames(tree, 1);
            var run = RunManager.Instance.DebugOnlyGetState() ?? throw new Exception("No run");
            var screen = NMapScreen.Instance!;
            var rows = screen.GetNode<Control>("MapLegend/LegendItems").GetChildren().OfType<NMapLegendItem>().ToArray();
            Require(rows.Length == 6, "Missing legend rows");
            Require(screen.GetNode<MegaLabel>("MapLegend/Header").Text == "Choose", "Header not renamed: " + screen.GetNode<MegaLabel>("MapLegend/Header").Text);
            GravityRunSettings.Set(run, 0);
            if (!run.VisitedMapCoords.Contains(run.Map.StartingMapPoint.coord)) run.AddVisitedMapCoord(run.Map.StartingMapPoint.coord);
            // Ensure even the early-game map exercises all six encounter types.
            var points = ((Dictionary<MapCoord, NMapPoint>)AccessTools.Field(typeof(NMapScreen), "_mapPointDictionary").GetValue(screen)!).Values.ToArray();
            var ordinary = points.Where(p => p.Point.coord != run.Map.StartingMapPoint.coord && p.Point.PointType != MapPointType.Boss).ToArray();
            for (var i = 0; i < rows.Length; i++) ordinary[i].Point.PointType = Type(rows[i]);
            screen.Open();
            GravityMapView.Get(screen)!.Finish();
            screen.IsTraveling = false;
            screen.SetTravelEnabled(true);
            screen.Call("RecalculateTravelability");
            await Frames(tree, 60);
            foreach (var row in rows)
            {
                var type = Type(row);
                var expected = ordinary.Count(p => p.Point.PointType == type && !run.VisitedMapCoords.Contains(p.Point.coord));
                Require(row.GetNode<MegaLabel>("MegaLabel").Text.EndsWith($"({expected})"), "Incorrect count for " + type);
                // The native label and icon include vertical padding outside the row.
                foreach (var child in new[] { row.GetNode<Control>("MegaLabel"), row.GetNode<Control>("Icon") })
                {
                    Require(row.GetGlobalRect().HasPoint(child.GetGlobalRect().GetCenter()), "Content outside click target");
                    Require(child.MouseFilter == Control.MouseFilterEnum.Ignore, "Child intercepts row clicks");
                }
                for (var i = 0; i < 20; i++)
                {
                    var before = Selected.Count;
                    Click(row);
                    Require(Selected.Count == before + 1, "Click did not select exactly one encounter: " + type);
                    Require(Selected[^1].Point.PointType == type && GravityRules.Progress(run).Available.Contains(Selected[^1].Point.coord), "Wrong or unavailable selection");
                }
            }
            var selectedCount = Selected.Count;
            screen.SetTravelEnabled(false);
            Click(rows[0]);
            Require(Selected.Count == selectedCount, "Selected while travel disabled");
            screen.SetTravelEnabled(true);
            screen.IsTraveling = true;
            Click(rows[0]);
            Require(Selected.Count == selectedCount, "Selected while traveling");
            screen.IsTraveling = false;
            foreach (var point in ordinary.Where(p => p.Point.PointType == Type(rows[0]))) run.AddVisitedMapCoord(point.Point.coord);
            screen.Call("RecalculateTravelability");
            Require(rows[0].GetNode<MegaLabel>("MegaLabel").Text.EndsWith("(0)") && rows[0].Modulate.A < 1, "Exhausted row not gray/zero");
            Click(rows[0]);
            Require(Selected.Count == selectedCount, "Selected exhausted type");
            GravityRunSettings.Set(run, 1, true);
            screen.Call("RecalculateTravelability");
            Require(rows.All(row => row.GetNode<MegaLabel>("MegaLabel").Text.EndsWith("(0)")), "Boss lock leaves nonzero counts");
            foreach (var row in rows) Click(row);
            Require(Selected.Count == selectedCount, "Selected after boss lock");
            GravityRunSettings.Set(run, 0);
            // Reattach twice, as when the map is replaced or an act changes.
            var dictionary = points.ToDictionary(p => p.Point.coord);
            GravityMapView.Attach(screen, run, 1, dictionary);
            GravityMapView.Attach(screen, run, 1, dictionary);
            screen.Call("RecalculateTravelability");
            Click(rows[1]);
            Require(Selected.Count == selectedCount + 1, "Reattach duplicated or lost handlers");
            if (System.Environment.GetEnvironmentVariable("GRAVITY_SELECTOR_SCREENSHOT") is { } screenshot)
            {
                await Frames(tree, 60);
                await screen.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                screen.GetViewport().GetTexture().GetImage().SavePng(screenshot);
            }
            GravityRunSettings.Set(run, 0, disabled: true);
            GravityMapView.Attach(screen, run, 1, dictionary);
            Require(screen.GetNode<MegaLabel>("MapLegend/Header").Text != "Choose", "Disabled run retained header");
            Require(rows.All(row => !row.GetNode<MegaLabel>("MegaLabel").Text.Contains('(') && row.Modulate.A == 1), "Disabled run retained counts or gray state");
            Click(rows[1]);
            Require(Selected.Count == selectedCount + 1, "Disabled run retained shortcut");
            GD.Print("ENCOUNTER SELECTOR TEST PASS six types, native clicks, counts, exhaustion, travel guards, boss lock and lifecycle");
            tree.Quit();
        }
        catch (Exception error)
        {
            GD.PrintErr("ENCOUNTER SELECTOR TEST FAIL " + error);
            tree.Quit(1);
        }
    }

    private static MapPointType Type(NMapLegendItem row) => (MapPointType)AccessTools.Field(typeof(NMapLegendItem), "_pointType").GetValue(row)!;
    private static void Click(NMapLegendItem row)
    {
        row.EmitSignal(Control.SignalName.MouseEntered);
        row.DebugPress();
        row.DebugRelease();
        row.EmitSignal(Control.SignalName.MouseExited);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
