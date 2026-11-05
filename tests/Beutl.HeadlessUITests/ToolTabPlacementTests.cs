using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.ColorGradingTab.ViewModels;
using Beutl.Editor.Components.ColorScopesTab.ViewModels;
using Beutl.Editor.Components.CurvesTab.ViewModels;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Components.SceneSettingsTab.ViewModels;
using Beutl.Extensibility;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Dock.Model.Controls;
using Moq;
using PinKeyProbe;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ToolTabPlacementTests
{
    private static void CloseAll<T>(EditViewModel editor) where T : class, IToolContext
    {
        while (editor.FindToolTab<T>() is { } tab)
        {
            editor.CloseToolTab(tab);
        }
    }

    private static BeutlToolDockable DockableOf(EditViewModel editor, IToolContext context)
    {
        return editor.DockHost.Factory.EnumerateTools().Single(tool => tool.ToolContext == context);
    }

    [AvaloniaTest]
    public async Task Tab_opened_from_code_goes_to_its_tools_default_dock()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("tab-placement-default-anchor");
        CloseAll<GraphEditorTabViewModel>(editor);
        CloseAll<SceneSettingsTabViewModel>(editor);

        var graphEditor = new GraphEditorTabViewModel(editor);
        var sceneSettings = new SceneSettingsTabViewModel(editor);
        Assert.That(editor.OpenToolTab(graphEditor), Is.True);
        Assert.That(editor.OpenToolTab(sceneSettings), Is.True);

        IToolDock bottom = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Bottom)!;
        IToolDock right = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Right)!;
        Assert.Multiple(() =>
        {
            Assert.That(DockableOf(editor, graphEditor).Owner, Is.SameAs(bottom));
            Assert.That(DockableOf(editor, sceneSettings).Owner, Is.SameAs(right));
        });
    }

    [AvaloniaTest]
    public async Task Editing_tools_open_in_their_default_dock()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("tab-placement-editor-tools");
        (IToolContext Tab, DockAnchor Anchor)[] tools =
        [
            (new CurvesTabViewModel(editor), DockAnchor.Left),
            (new ColorScopesTabViewModel(editor), DockAnchor.Left),
            (new NodeGraphTabViewModel(editor), DockAnchor.Bottom),
            (new ColorGradingTabViewModel(editor), DockAnchor.Bottom),
            (new PathEditorTabViewModel(editor), DockAnchor.Bottom),
        ];

        foreach ((IToolContext tab, DockAnchor anchor) in tools)
        {
            Assert.That(editor.OpenToolTab(tab), Is.True);
            Assert.That(
                DockableOf(editor, tab).Owner,
                Is.SameAs(editor.DockHost.Factory.GetAnchoredDock(anchor)),
                tab.Extension.Name);
        }
    }

    [AvaloniaTest]
    public async Task Tab_without_a_default_dock_keeps_the_fallback()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("tab-placement-no-anchor");
        var context = new Mock<IToolContext>();
        context.SetupGet(c => c.Extension).Returns(new PinKeyProbeToolTabExtension());
        context.SetupGet(c => c.Header).Returns(new ReactivePropertySlim<string>("Probe"));
        context.SetupGet(c => c.IsSelected).Returns(new ReactivePropertySlim<bool>());

        Assert.That(editor.OpenToolTab(context.Object), Is.True);

        Assert.That(
            DockableOf(editor, context.Object).Owner,
            Is.SameAs(editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Left)));
    }
}
