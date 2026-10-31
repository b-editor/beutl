using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.DockLayoutTab;
using Beutl.Editor.Components.LibraryTab;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.Serialization;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using Dock.Model.Controls;
using Dock.Model.Core;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class DockLayoutPresetTests
{
    internal static DockLayoutPresetService NewService()
    {
        string path = Path.Combine(
            BeutlHomeIsolation.CurrentHome!, $"dock-layout-presets-{Guid.NewGuid():N}.json");
        return new DockLayoutPresetService(path);
    }

    internal static string[] ToolExtensionNames(EditViewModel editor)
    {
        return editor.DockHost.Factory.EnumerateTools()
            .Select(t => t.ToolContext.Extension.GetType().FullName!)
            .OrderBy(n => n)
            .ToArray();
    }

    [AvaloniaTest]
    public async Task Saved_layout_can_be_applied_to_another_scene()
    {
        await TestReset.ResetShellAsync();
        EditViewModel source = await TestShell.OpenNewSceneEditorAsync("preset-source", 1920, 1080);

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

        EditViewModel target = await TestShell.OpenNewSceneEditorAsync("preset-target", 1920, 1080);
        Assert.That(ToolExtensionNames(target), Does.Contain(typeof(LibraryTabExtension).FullName));

        Assert.That(target.DockHost.ApplyLayout(preset!.Layout), Is.True);
        HeadlessTestHelpers.Settle();

        Assert.That(ToolExtensionNames(target), Is.EqualTo(expected));
        Assert.That(target.DockHost.Layout.Value.Id, Is.EqualTo(DockIds.Root));
    }

    [AvaloniaTest]
    public async Task Captured_layout_does_not_carry_per_tool_state()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("preset-strip-state", 1920, 1080);

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
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("preset-malformed", 1920, 1080);

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
    public async Task A_failed_restore_disposes_the_tools_it_already_built()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("preset-partial-restore", 1920, 1080);

        JsonObject captured = editor.DockHost.CaptureLayout();

        // Corrupt the root so restoration walks the tree, builds tool contexts, and only then
        // fails to produce an IRootDock.
        var broken = (JsonObject)captured.DeepClone();
        ((JsonObject)broken["DockLayout"]!)["$type"] = "proportional";

        IRootDock layoutBefore = editor.DockHost.Layout.Value;
        BeutlToolDockable[] liveBefore = editor.DockHost.Factory.EnumerateTools().ToArray();

        bool[] selectedBefore = liveBefore.Select(t => t.ToolContext.IsSelected.Value).ToArray();

        Assert.That(editor.DockHost.ApplyLayout(broken), Is.False);
        Assert.That(editor.DockHost.Layout.Value, Is.SameAs(layoutBefore), "the live layout must survive");
        Assert.That(editor.DockHost.Factory.EnumerateTools(), Is.EquivalentTo(liveBefore));

        // A disposed BeutlToolDockable stops mirroring IsSelected onto its context, so flipping it
        // here proves the surviving tools were not caught up in the cleanup.
        for (int i = 0; i < liveBefore.Length; i++)
        {
            liveBefore[i].IsSelected = !selectedBefore[i];
            Assert.That(
                liveBefore[i].ToolContext.IsSelected.Value, Is.EqualTo(!selectedBefore[i]),
                "a surviving tool must still be wired to its context");
            liveBefore[i].IsSelected = selectedBefore[i];
        }
    }

    // A tool tab type that moves keeps its layouts through FormerPersistedNameAttribute, and is saved under its
    // current name again.
    [AvaloniaTest]
    public async Task A_layout_saved_under_a_former_tool_type_name_opens_the_tool()
    {
        const int packageId = -42_879;
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("preset-former-name", 1920, 1080);
        var renamed = new RenamedToolTabExtension();
        TestShell.Extensions.AddExtensions(packageId, [renamed]);
        try
        {
            JsonObject layout = editor.DockHost.CaptureLayout();
            FirstToolList(layout)!.Add(new JsonObject
            {
                ["$type"] = "tool",
                ["extension"] = new JsonObject { ["$type"] = RenamedToolTabExtension.FormerName }
            });

            Assert.That(editor.DockHost.ApplyLayout(layout), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(editor.DockHost.Factory.EnumerateTools().Select(tool => tool.ToolContext.Extension),
                    Does.Contain(renamed));
                string saved = editor.DockHost.CaptureLayout().ToJsonString();
                Assert.That(saved, Does.Contain(TypeFormat.ToString(typeof(RenamedToolTabExtension))));
                Assert.That(saved, Does.Not.Contain(RenamedToolTabExtension.FormerName));
            });
        }
        finally
        {
            _ = TestShell.Extensions.RemoveExtensions(packageId);
            HeadlessTestHelpers.Settle();
        }

        static JsonArray? FirstToolList(JsonNode? node) => node switch
        {
            JsonObject obj when obj["tools"] is JsonArray tools => tools,
            JsonObject obj => obj.Select(pair => FirstToolList(pair.Value)).FirstOrDefault(found => found is not null),
            JsonArray array => array.Select(FirstToolList).FirstOrDefault(found => found is not null),
            _ => null
        };
    }

    [AvaloniaTest]
    public async Task A_layout_from_an_incompatible_version_is_refused()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("preset-version", 1920, 1080);

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
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("preset-no-side-effects", 1920, 1080);

        // The element property tab writes a per-element config from its serializer, but only once
        // an element is selected — so select one to arm the side effect.
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        ElementAddResult addResult = await adder.AddAsync(
        [
            new ElementDescription(
                Start: MediaTime.Zero,
                Length: MediaTime.FromSeconds(1),
                Layer: 0,
                Source: new ElementSource.EngineObject(() => new RectShape())),
        ], CancellationToken.None);
        Assert.That(addResult.IsSuccess, Is.True);
        HeadlessTestHelpers.Settle();

        var selection = (IEditorSelection)editor.GetService(typeof(IEditorSelection))!;
        selection.Select(editor.Scene.Children[0]);
        HeadlessTestHelpers.Settle();
        // Adding the element started an auto-save, which saves the view state, the selected element's config
        // included, before it lets go of the project's files.
        await TestShell.WaitForProjectWritesAsync();

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
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("preset-pinned", 1920, 1080);

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

    // One file is not JSON at all, the other parses but its root is not the expected array.
    [TestCase("{ this is not json")]
    [TestCase("{ \"not\": \"an array\" }")]
    public void A_corrupt_store_file_still_accepts_later_saves(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-dock-presets-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, content);

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
    public void A_read_only_store_file_still_loads()
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-dock-presets-{Guid.NewGuid():N}.json");
        try
        {
            new DockLayoutPresetService(path)
                .Save("Editing", new JsonObject { ["DockLayout"] = new JsonObject { ["$type"] = "root" } });

            // Applying a layout needs read access only, so a read-only file must still load.
            File.SetAttributes(path, FileAttributes.ReadOnly);

            Assert.That(
                new DockLayoutPresetService(path).Items.Select(i => i.Name.Value),
                Is.EqualTo(new[] { "Editing" }));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
    }

    [FormerPersistedName(FormerName)]
    private sealed class RenamedToolTabExtension : ToolTabExtension
    {
        public const string FormerName = "[Beutl.HeadlessUITests]Former.Namespace:FormerLayoutToolTabExtension";

        public override string Name => "Renamed layout tool";

        public override string DisplayName => "Renamed layout tool";

        public override bool CanMultiple => false;

        public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
        {
            control = new Border();
            return true;
        }

        public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
        {
            context = new ToolContext(this);
            return true;
        }
    }

    private sealed class ToolContext(ToolTabExtension extension) : IToolContext
    {
        public ToolTabExtension Extension => extension;

        public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

        public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>("Renamed layout tool");

        public void Dispose() => IsSelected.Dispose();

        public object? GetService(Type serviceType) => null;

        public void ReadFromJson(JsonObject json)
        {
        }

        public void WriteToJson(JsonObject json)
        {
        }
    }
}
