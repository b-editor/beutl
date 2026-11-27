using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Editor.Components.LibraryTab.Views;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.Views;
using Dock.Avalonia.Controls;
using Dock.Model.Core;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class DockTabMoveTests
{
    [AvaloniaTest]
    public async Task A_tab_split_below_the_editor_is_visible_when_it_rejoins_the_timeline()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("dock-tab-split-and-rejoin");
        BeutlToolDockable library = editor.DockHost.Factory.EnumerateTools().Single(tab => tab.ToolContext is LibraryTabViewModel);
        BeutlToolDockable timeline = editor.DockHost.Factory.EnumerateTools().Single(tab => tab.ToolContext is TimelineTabViewModel);
        var timelineDock = (IDock)timeline.Owner!;
        IDockable? libraryDock = library.Owner;

        var window = new Window
        {
            Width = 1200,
            Height = 760,
            Content = new EditView { DataContext = editor },
            RequestedThemeVariant = ThemeVariant.Dark,
        };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            StartDrag();
            DropOn<GlobalDockTarget>("PART_BottomSelector");
            Assert.That(library.Owner, Is.Not.SameAs(libraryDock), "The first drop should split off the Library tab.");
            Assert.That(library.Owner, Is.Not.SameAs(timelineDock));
            HeadlessCapture.SaveIfRequested(window, nameof(DockTabMoveTests), "split");

            StartDrag();
            DropOn<DockTarget>("PART_CenterSelector");
            HeadlessCapture.SaveIfRequested(window, nameof(DockTabMoveTests), "rejoined-library");

            Assert.That(library.Owner, Is.SameAs(timelineDock));
            Assert.That(timelineDock.VisibleDockables, Is.EqualTo(new[] { timeline, library }));
            AssertSelectedTab<LibraryTabView>(library);

            HeadlessTestHelpers.Click(Header(timeline), move: true, ticks: 3);
            AssertSelectedTab<TimelineTabView>(timeline);
            HeadlessCapture.SaveIfRequested(window, nameof(DockTabMoveTests), "rejoined-timeline");

            HeadlessTestHelpers.Click(Header(library), move: true, ticks: 3);
            AssertSelectedTab<LibraryTabView>(library);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }

        ToolControl TimelineControl() => window.GetVisualDescendants().OfType<ToolControl>()
            .Single(control => ReferenceEquals(control.DataContext, timelineDock));

        ToolTabStripItem Header(BeutlToolDockable tab) => window.GetVisualDescendants().OfType<ToolTabStripItem>()
            .Single(header => ReferenceEquals(header.DataContext, tab));

        void StartDrag()
        {
            Point start = HeadlessTestHelpers.Center(Header(library), window);
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(12, 12), RawInputModifiers.LeftMouseButton);
            window.MouseMove(HeadlessTestHelpers.Center(TimelineControl(), window), RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Render(3);
        }

        void DropOn<T>(string selectorName) where T : Control
        {
            T adorner = window.GetVisualDescendants().OfType<T>().Single(control => control.IsEffectivelyVisible);
            var selector = adorner.GetVisualDescendants().OfType<Control>().Single(control => control.Name == selectorName);
            Point end = HeadlessTestHelpers.Center(selector, window);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Render(3);
            window.MouseUp(end, MouseButton.Left);
            HeadlessTestHelpers.Render(3);
        }

        void AssertSelectedTab<T>(BeutlToolDockable tab) where T : Control
        {
            ToolTabStripItem header = Header(tab);
            Point point = HeadlessTestHelpers.Center(header, window);
            var hit = (window.InputHitTest(point) as Visual)?.FindAncestorOfType<ToolTabStripItem>(includeSelf: true);
            T content = TimelineControl().GetVisualDescendants().OfType<T>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(timelineDock.ActiveDockable, Is.SameAs(tab));
                Assert.That(header.IsSelected, Is.True);
                Assert.That(hit, Is.SameAs(header), "The selected tab's header should be accessible by pointer.");
                Assert.That(content.DataContext, Is.SameAs(tab.ToolContext));
            });
        }
    }
}
