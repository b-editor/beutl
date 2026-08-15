using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.ElementPropertyTab;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.Editor.Components.LibraryTab;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class DefaultLayoutTests
{
    private static Task ResetProjectAsync() => TestReset.ResetShellAsync();

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<EditViewModel> OpenEditorForNewScene(int width, int height, string name)
    {
        Project project = (await TestShell.Project.CreateProject(
            width, height, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    [AvaloniaTest]
    public async Task Landscape_scene_uses_landscape_default_layout()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene(1920, 1080, "layout-landscape");

        IRootDock root = editor.DockHost.Layout.Value;
        var docks = BeutlDockFactory.Traverse(root).OfType<IDock>().ToList();

        var rootSplit = docks.Single(d => d.Id == DockIds.RootSplit);
        Assert.That(((IProportionalDock)rootSplit).Orientation, Is.EqualTo(Orientation.Vertical));

        // Landscape: tools | preview | properties on top, timeline below.
        Assert.That(docks.Any(d => d.Id == DockIds.TopSplit), Is.True);
        Assert.That(docks.Any(d => d.Id == DockIds.RightColumn), Is.False);
        Assert.That(docks.Any(d => d.Id == DockIds.ToolsRow), Is.False);

        Assert.That(GetToolDockIds(editor, typeof(LibraryTabExtension)), Does.Contain(DockIds.Left));
        Assert.That(GetToolDockIds(editor, typeof(FileBrowserTabExtension)), Does.Contain(DockIds.Left));
        Assert.That(GetToolDockIds(editor, typeof(ElementPropertyTabExtension)), Does.Contain(DockIds.Right));
        Assert.That(GetToolDockIds(editor, typeof(TimelineTabExtension)), Does.Contain(DockIds.Bottom));
    }

    [AvaloniaTest]
    public async Task Portrait_scene_uses_portrait_default_layout()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene(1080, 1920, "layout-portrait");

        IRootDock root = editor.DockHost.Layout.Value;
        var docks = BeutlDockFactory.Traverse(root).OfType<IDock>().ToList();

        var rootSplit = docks.Single(d => d.Id == DockIds.RootSplit);
        Assert.That(((IProportionalDock)rootSplit).Orientation, Is.EqualTo(Orientation.Horizontal));

        // Portrait: preview on the left, tools | properties on top right, timeline below.
        var rightColumn = docks.Single(d => d.Id == DockIds.RightColumn);
        Assert.That(((IProportionalDock)rightColumn).Orientation, Is.EqualTo(Orientation.Vertical));

        var toolsRow = docks.Single(d => d.Id == DockIds.ToolsRow);
        Assert.That(((IProportionalDock)toolsRow).Orientation, Is.EqualTo(Orientation.Horizontal));
        Assert.That(docks.Any(d => d.Id == DockIds.TopSplit), Is.False);

        // Preview : tools column = 1 : 2 (proportions are normalized by the dock panel).
        var playerDock = docks.OfType<IToolDock>().Single(d => d.Id == DockIds.Player);
        Assert.That(playerDock.Proportion, Is.EqualTo(0.5));
        Assert.That(rightColumn.Proportion, Is.EqualTo(1.0));

        Assert.That(GetToolDockIds(editor, typeof(LibraryTabExtension)), Does.Contain(DockIds.Left));
        Assert.That(GetToolDockIds(editor, typeof(FileBrowserTabExtension)), Does.Contain(DockIds.Left));
        Assert.That(GetToolDockIds(editor, typeof(ElementPropertyTabExtension)), Does.Contain(DockIds.Right));
        Assert.That(GetToolDockIds(editor, typeof(TimelineTabExtension)), Does.Contain(DockIds.Bottom));
    }

    private static string[] GetToolDockIds(EditViewModel editor, Type extensionType)
    {
        IRootDock root = editor.DockHost.Layout.Value;
        return BeutlDockFactory.Traverse(root)
            .OfType<BeutlToolDockable>()
            .Where(tool => tool.ToolContext.Extension.GetType() == extensionType)
            .Select(tool => FindToolDockId(root, tool))
            .OfType<string>()
            .ToArray();
    }

    private static string? FindToolDockId(IDockable root, BeutlToolDockable tool)
    {
        foreach (var dock in BeutlDockFactory.Traverse(root).OfType<IToolDock>())
        {
            if (dock.VisibleDockables?.Contains(tool) == true)
                return dock.Id;
        }
        return null;
    }
}
