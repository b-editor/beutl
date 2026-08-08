using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.LibraryTab;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class DockLayoutPresetTests
{
    private static Task ResetProjectAsync() => TestReset.ResetShellAsync();

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<EditViewModel> OpenEditorForNewScene(string name, int width = 1920, int height = 1080)
    {
        Project project = (await TestShell.Project.CreateProject(
            width, height, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    private static DockLayoutPresetService NewService()
    {
        string path = Path.Combine(
            BeutlHomeIsolation.CurrentHome!, $"dock-layout-presets-{Guid.NewGuid():N}.json");
        return new DockLayoutPresetService(path);
    }

    private static string[] ToolExtensionNames(EditViewModel editor)
    {
        return editor.DockHost.Factory.EnumerateTools()
            .Select(t => t.ToolContext.Extension.GetType().FullName!)
            .OrderBy(n => n)
            .ToArray();
    }

    [AvaloniaTest]
    public async Task Saved_layout_can_be_applied_to_another_scene()
    {
        await ResetProjectAsync();
        EditViewModel source = await OpenEditorForNewScene("preset-source");

        // Make the layout distinguishable from the default: close the library tab.
        BeutlToolDockable library = source.DockHost.Factory.EnumerateTools()
            .First(t => t.ToolContext.Extension is LibraryTabExtension);
        source.DockHost.CloseToolTab(library.ToolContext);
        HeadlessTestHelpers.Settle();

        string[] expected = ToolExtensionNames(source);
        Assert.That(expected, Does.Not.Contain(typeof(LibraryTabExtension).FullName));

        DockLayoutPresetService service = NewService();
        DockLayoutPresetItem? preset = service.Save("No library", source.DockHost.CaptureLayout());
        Assert.That(preset, Is.Not.Null);

        EditViewModel target = await OpenEditorForNewScene("preset-target");
        Assert.That(ToolExtensionNames(target), Does.Contain(typeof(LibraryTabExtension).FullName));

        Assert.That(target.DockHost.ApplyLayout(preset!.Layout), Is.True);
        HeadlessTestHelpers.Settle();

        Assert.That(ToolExtensionNames(target), Is.EqualTo(expected));
        Assert.That(target.DockHost.Layout.Value.Id, Is.EqualTo(DockIds.Root));
    }

    [AvaloniaTest]
    public async Task Captured_layout_does_not_carry_per_tool_state()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("preset-strip-state");

        JsonObject captured = editor.DockHost.CaptureLayout();

        var toolNodes = new List<JsonObject>();
        Collect(captured, toolNodes);
        Assert.That(toolNodes, Is.Not.Empty);

        foreach (JsonObject tool in toolNodes)
        {
            Assert.That(
                tool.Select(p => p.Key),
                Is.SubsetOf(new[] { "$type", "id", "extension" }),
                "A preset must only carry what identifies a tab.");
        }

        static void Collect(JsonNode? node, List<JsonObject> result)
        {
            switch (node)
            {
                case JsonObject obj:
                    if (obj["$type"]?.GetValue<string>() == "tool") result.Add(obj);
                    foreach ((string _, JsonNode? child) in obj) Collect(child, result);
                    break;
                case JsonArray array:
                    foreach (JsonNode? item in array) Collect(item, result);
                    break;
            }
        }
    }

    [AvaloniaTest]
    public async Task Applying_a_malformed_layout_keeps_the_current_one()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("preset-malformed");

        string[] before = ToolExtensionNames(editor);
        IRootDock layoutBefore = editor.DockHost.Layout.Value;

        Assert.That(editor.DockHost.ApplyLayout(new JsonObject()), Is.False);
        Assert.That(
            editor.DockHost.ApplyLayout(new JsonObject { ["DockLayout"] = new JsonObject { ["$type"] = "tool" } }),
            Is.False);

        Assert.That(editor.DockHost.Layout.Value, Is.SameAs(layoutBefore));
        Assert.That(ToolExtensionNames(editor), Is.EqualTo(before));
    }

    [AvaloniaTest]
    public async Task A_layout_from_an_incompatible_version_is_refused()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("preset-version");

        JsonObject captured = editor.DockHost.CaptureLayout();
        Assert.That(editor.DockHost.ApplyLayout(captured), Is.True, "the captured layout should apply as-is");

        IRootDock layoutBefore = editor.DockHost.Layout.Value;
        var stale = (JsonObject)captured.DeepClone();
        stale["_dockVersion"] = 1;

        Assert.That(editor.DockHost.ApplyLayout(stale), Is.False);
        Assert.That(editor.DockHost.Layout.Value, Is.SameAs(layoutBefore));
    }

    [Test]
    public void Presets_round_trip_through_the_store()
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-dock-presets-{Guid.NewGuid():N}.json");
        try
        {
            var service = new DockLayoutPresetService(path);
            var layout = new JsonObject { ["DockLayout"] = new JsonObject { ["$type"] = "root" } };

            Assert.That(service.Save("Editing", layout), Is.Not.Null);
            Assert.That(service.Items, Has.Count.EqualTo(1));

            // Same name overwrites instead of adding a duplicate.
            var other = new JsonObject
            {
                ["DockLayout"] = new JsonObject { ["$type"] = "root", ["id"] = "Root" }
            };
            Assert.That(service.Save("editing", other), Is.Not.Null);
            Assert.That(service.Items, Has.Count.EqualTo(1));
            Assert.That(service.Items[0].Layout["DockLayout"]!["id"]!.GetValue<string>(), Is.EqualTo("Root"));

            Assert.That(service.Save("  ", layout), Is.Null, "A blank name must be rejected.");

            var reloaded = new DockLayoutPresetService(path);
            Assert.That(reloaded.Items, Has.Count.EqualTo(1));
            Assert.That(reloaded.Items[0].Name.Value, Is.EqualTo("Editing"));

            Assert.That(reloaded.Rename(reloaded.Items[0], "Grading"), Is.True);
            Assert.That(new DockLayoutPresetService(path).Items[0].Name.Value, Is.EqualTo("Grading"));

            Assert.That(reloaded.Remove(reloaded.Items[0]), Is.True);
            Assert.That(new DockLayoutPresetService(path).Items, Is.Empty);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [AvaloniaTest]
    public async Task Capturing_a_layout_does_not_write_per_scene_view_state()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("preset-no-side-effects");

        // The element property tab writes a per-element config from its serializer, but only once
        // an element is selected — so select one to arm the side effect.
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        adder.AddElement(new ElementDescription(
            Start: TimeSpan.Zero,
            Length: TimeSpan.FromSeconds(1),
            Layer: 0,
            EngineObjectFactory: () => new RectShape()));
        HeadlessTestHelpers.Settle();

        var selection = (IEditorSelection)editor.GetService(typeof(IEditorSelection))!;
        selection.SelectedObject.Value = editor.Scene.Children[0];
        HeadlessTestHelpers.Settle();

        // Some tool serializers write their own per-scene config as a side effect. Capturing a
        // layout discards tool state anyway, so it must not invoke them at all.
        string sceneDir = Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!;
        string[] before = ConfigFiles(sceneDir);

        editor.DockHost.CaptureLayout();
        HeadlessTestHelpers.Settle();

        Assert.That(ConfigFiles(sceneDir), Is.EqualTo(before),
            "capturing a layout must not touch the scene's view-state files");

        static string[] ConfigFiles(string dir)
        {
            return Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.config", SearchOption.AllDirectories)
                    .Select(f => $"{f}:{new FileInfo(f).Length}")
                    .OrderBy(f => f)
                    .ToArray()
                : [];
        }
    }

    [AvaloniaTest]
    public async Task Pinned_tools_are_enumerated_when_replacing_a_layout()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("preset-pinned");

        IRootDock root = editor.DockHost.Layout.Value;
        BeutlToolDockable pinned = editor.DockHost.Factory.EnumerateTools()
            .First(t => t.ToolContext.Extension is LibraryTabExtension);

        // Pin the tool the way the dock does: out of VisibleDockables, into a pinned collection.
        (pinned.Owner as IDock)?.VisibleDockables?.Remove(pinned);
        root.LeftPinnedDockables ??= editor.DockHost.Factory.CreateList<IDockable>();
        root.LeftPinnedDockables.Add(pinned);
        HeadlessTestHelpers.Settle();

        // A pinned tool is still owned by the layout, so enumeration must find it — otherwise its
        // context leaks on replace and an all-pinned layout looks empty.
        Assert.That(editor.DockHost.Factory.EnumerateTools(), Does.Contain(pinned));
        Assert.That(
            ToolExtensionNames(editor), Does.Contain(typeof(LibraryTabExtension).FullName));
    }

    [Test]
    public void A_failed_write_rolls_back_the_in_memory_change()
    {
        // Pointing the store at a directory makes every write throw, standing in for an unwritable
        // BEUTL_HOME. Reporting success there would lose the preset at the next restart.
        string directoryAsFile = Path.Combine(Path.GetTempPath(), $"beutl-dock-presets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryAsFile);
        try
        {
            var service = new DockLayoutPresetService(directoryAsFile);
            var layout = new JsonObject { ["DockLayout"] = new JsonObject { ["$type"] = "root" } };

            Assert.That(service.Save("Editing", layout), Is.Null, "an unwritable store must report failure");
            Assert.That(service.Items, Is.Empty, "the failed save must not linger in memory");
        }
        finally
        {
            Directory.Delete(directoryAsFile, recursive: true);
        }
    }

    [Test]
    public void Rename_and_remove_roll_back_when_the_write_fails()
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-dock-presets-{Guid.NewGuid():N}.json");
        try
        {
            var service = new DockLayoutPresetService(path);
            var layout = new JsonObject { ["DockLayout"] = new JsonObject { ["$type"] = "root" } };
            service.Save("Editing", layout);
            service.Save("Grading", layout);

            // Replacing the file with a directory makes the next write throw.
            File.Delete(path);
            Directory.CreateDirectory(path);

            DockLayoutPresetItem editing = service.Items[0];
            Assert.That(service.Rename(editing, "Rough cut"), Is.False);
            Assert.That(editing.Name.Value, Is.EqualTo("Editing"), "a failed rename must not stick");

            Assert.That(service.Remove(editing), Is.False);
            Assert.That(service.Items.Select(i => i.Name.Value), Is.EqualTo(new[] { "Editing", "Grading" }));
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void An_unparsable_store_file_still_accepts_later_saves()
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-dock-presets-{Guid.NewGuid():N}.json");
        try
        {
            // Syntactically invalid, so this exercises the JsonNode.Parse failure path.
            File.WriteAllText(path, "{ this is not json");

            var service = new DockLayoutPresetService(path);
            Assert.That(service.Items, Is.Empty);

            var layout = new JsonObject { ["DockLayout"] = new JsonObject { ["$type"] = "root" } };
            Assert.That(service.Save("Editing", layout), Is.Not.Null, "a bad file must not wedge the store");
            Assert.That(new DockLayoutPresetService(path).Items.Select(i => i.Name.Value),
                Is.EqualTo(new[] { "Editing" }));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void A_store_file_that_is_not_an_array_still_accepts_later_saves()
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-dock-presets-{Guid.NewGuid():N}.json");
        try
        {
            // Parses, but the root is not the expected array.
            File.WriteAllText(path, "{ \"not\": \"an array\" }");

            var service = new DockLayoutPresetService(path);
            Assert.That(service.Items, Is.Empty);

            var layout = new JsonObject { ["DockLayout"] = new JsonObject { ["$type"] = "root" } };
            Assert.That(service.Save("Editing", layout), Is.Not.Null);
            Assert.That(new DockLayoutPresetService(path).Items.Select(i => i.Name.Value),
                Is.EqualTo(new[] { "Editing" }));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
