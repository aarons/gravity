using Godot;
using HarmonyLib;
using System.Text.Json;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using Environment = System.Environment;

[ModInitializer(nameof(Init))]
public static class HistoryLayoutTests
{
    public static void Init() => new Harmony("Gravity.RunHistoryTests").Patch(AccessTools.Method(typeof(NMainMenu), "_Ready"), postfix: new HarmonyMethod(typeof(HistoryLayoutTests), nameof(Ready)));
    private static bool started;
    static void Ready(NMainMenu __instance)
    {
        if (started) return;
        started = true;
        _ = Run(__instance);
    }
    static async Task Frames(Node node, int count = 10)
    {
        for (int i = 0; i < count; i++) await node.ToSignal(node.GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    static async Task Run(NMainMenu menu)
    {
        try
        {
            await Frames(menu);
            var fixture = Environment.GetEnvironmentVariable("GRAVITY_HISTORY_FIXTURE")
                ?? throw new Exception("Set GRAVITY_HISTORY_FIXTURE to an existing .run file.");
            var json = JsonNode.Parse(System.IO.File.ReadAllText(fixture))!;
            // The test runs offline, even when the read-only fixture came from Steam.
            json["platform_type"] = "none";
            var history = json.Deserialize<RunHistory>(JsonSerializationUtility.Options)!;
            var host = new Control { Size = new Vector2(1920, 1080) };
            menu.AddChild(host);
            var screen = NRunHistory.Create()!;
            host.AddChild(screen);
            screen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var display = AccessTools.Method(typeof(NRunHistory), "DisplayRun");
            foreach (int count in new[] { 17, 18, 31, 100, 17, 0 })
            {
                var sample = JsonSerializer.Deserialize<RunHistory>(JsonSerializer.Serialize(history, JsonSerializationUtility.Options), JsonSerializationUtility.Options)!;
                foreach (var act in sample.MapPointHistory)
                {
                    var entry = act.First(e => e.MapPointType == MegaCrit.Sts2.Core.Map.MapPointType.Monster);
                    act.Clear();
                    act.AddRange(Enumerable.Repeat(entry, count));
                }
                // Empty histories are supported; don't leave empty acts for native HP lookup.
                if (count == 0) sample.MapPointHistory.Clear();
                display.Invoke(screen, new object[] { sample });
                await Frames(menu);
                Check(screen, sample, count.ToString());
            }
            display.Invoke(screen, new object[] { history });
            await Frames(menu);
            Check(screen, history, "original fixture");
            if (Environment.GetEnvironmentVariable("GRAVITY_HISTORY_SCREENSHOT") is { } screenshot)
            {
                await menu.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                screen.GetViewport().GetTexture().GetImage().SavePng(screenshot);
            }
            // Repeated reopening uses fresh act nodes and must retain the same bounds.
            screen.QueueFree();
            await Frames(menu);
            screen = NRunHistory.Create()!;
            host.AddChild(screen);
            screen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            display.Invoke(screen, new object[] { history });
            await Frames(menu);
            Check(screen, history, "reopened fixture");
            GD.Print("HISTORY TEST PASS native history layout, entry order, and focus links");
            menu.GetTree().Quit();
        }
        catch (Exception ex) { GD.PrintErr("HISTORY TEST FAIL " + ex); menu.GetTree().Quit(1); }
    }
    private static void Check(NRunHistory screen, RunHistory history, string scenario)
    {
        var content = screen.GetNode<Control>("ScreenContents/Content");
        var acts = screen.GetNode<NMapPointHistory>("%MapPointHistory").GetNode<Control>("%Acts")
            .GetChildren().OfType<NActHistoryEntry>().ToArray();
        GD.Print($"HISTORY TEST {scenario}: content={content.GetRect()} screen={screen.Size}");
        Require(Math.Abs(content.Position.X) < 1 && Math.Abs(content.Size.X - screen.Size.X) < 1,
            "Page displaced or widened");
        Require(acts.Length == history.MapPointHistory.Count, "Missing acts");
        var floor = 1;
        for (var a = 0; a < acts.Length; a++)
        {
            var act = acts[a];
            Require(act.Entries.Count == history.MapPointHistory[a].Count, "Missing entries");
            for (var i = 0; i < act.Entries.Count; i++)
            {
                var entry = act.Entries[i];
                var rect = entry.GetGlobalRect();
                var bounds = screen.GetGlobalRect();
                Require(rect.Position.X >= bounds.Position.X && rect.End.X <= bounds.End.X,
                    "Entry outside horizontal viewport");
                Require(entry.FloorNum == floor++, "Floor order changed");
                Require(entry.Size.X >= 60 && entry.Size.Y >= 60, "Encounter hit target shrunk");
                if (i + 1 < act.Entries.Count)
                {
                    var next = act.Entries[i + 1];
                    Require(entry.GetNode(entry.FocusNeighborRight) == next
                        && next.GetNode(next.FocusNeighborLeft) == entry, "Visit navigation broken");
                    var nextRect = next.GetGlobalRect();
                    Require(nextRect.Position.Y > rect.Position.Y || nextRect.Position.X >= rect.End.X,
                        "Encounter order or spacing broken");
                }
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

}
