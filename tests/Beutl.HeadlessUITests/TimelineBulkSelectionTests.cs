using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public class TimelineBulkSelectionTests
{
    [AvaloniaTest]
    [TestCase(false, 1f)]
    [TestCase(true, 1f)]
    [TestCase(false, 0.05f)]
    [TestCase(true, 0.05f)]
    public async Task Both_menus_select_from_the_right_click_position_and_offer_other_selections(bool onElement, float scale)
    {
        using TimelineTestHost host = await CreateAsync(onElement ? ThemeVariant.Dark : ThemeVariant.Light);
        Element[] elements = [.. host.Scene.Children];
        elements[2].IsLocked.Value = true;
        host.Scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 1 }, IsLocked = { Value = true } });
        host.Model.Options.Value = host.Model.Options.Value with { Scale = scale };
        host.Model.Seek(MediaTime.FromSeconds(7));
        var editor = (EditViewModel)host.Model.EditorContext;
        editor.HistoryManager.Commit();
        int undoCount = editor.HistoryManager.UndoCount;
        HeadlessTestHelpers.Render(3);

        FAMenuFlyoutSubItem menu = await OpenMenuAsync(host, onElement ? elements[1] : null, onElement ? 2.5 : 3.5);
        Assert.That(menu.Items.OfType<FAMenuFlyoutItem>().Select(item => item.Text), Is.EqualTo(new[]
        {
            Strings.SelectAll,
            Strings.SelectGroupElements, Strings.SelectLayerElements, Strings.SelectElementsAtPosition,
            Strings.SelectPrecedingElements, Strings.SelectFollowingElements,
            Strings.SelectPrecedingElementsAllLayers, Strings.SelectFollowingElementsAllLayers,
            Strings.InvertSelection, Strings.DeselectAll
        }));
        HeadlessCapture.SaveIfRequested(Item(menu, Strings.SelectFollowingElements), nameof(TimelineBulkSelectionTests),
            $"submenu-{onElement}-{scale}");
        HeadlessCapture.SaveIfRequested(menu, nameof(TimelineBulkSelectionTests), $"context-{onElement}-{scale}");

        // Moving back over the timeline after opening the menu must not change its reference position.
        host.Window.MouseMove(ToWindow(host, 2.9, 0));
        HeadlessTestHelpers.Render();
        HeadlessTestHelpers.Click(Item(menu, Strings.SelectFollowingElementsAllLayers), move: true);

        Assert.Multiple(() =>
        {
            Assert.That(Selected(host), Is.EquivalentTo(onElement
                ? elements.Skip(1)
                : new[] { elements[2], elements[3], elements[5], elements[6] }));
            Assert.That(host.Model.CurrentTime.Value, Is.EqualTo(MediaTime.FromSeconds(7)));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount));
            Assert.That(elements.Select(element => element.Start.Value.ToSeconds()),
                Is.EqualTo(new double[] { 0, 2, 4, 100, 1, 5, 2 }));
        });
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Group_and_selection_management_items_follow_the_menu_context(bool onElement)
    {
        using TimelineTestHost host = await CreateAsync();
        Element[] elements = [.. host.Scene.Children];
        host.Scene.Groups.Add([elements[1].Id.Value, elements[4].Id.Value]);
        host.Scene.Groups.Add([elements[0].Id.Value, elements[2].Id.Value]);
        host.Model.ClearSelected();
        host.Model.SelectElement(host.Model.GetViewModelFor(elements[0])!);
        host.Model.SelectElement(host.Model.GetViewModelFor(elements[5])!);
        HeadlessTestHelpers.Render(3);

        FAMenuFlyoutSubItem menu = await OpenMenuAsync(host, onElement ? elements[1] : null, onElement ? 2.5 : 3.5);
        Assert.That(Item(menu, Strings.SelectGroupElements).IsEnabled, Is.True);
        HeadlessTestHelpers.Click(Item(menu, Strings.SelectGroupElements), move: true);
        Assert.That(Selected(host), Is.EquivalentTo(onElement
            ? new[] { elements[1], elements[4] }
            : new[] { elements[0], elements[2] }));

        menu = await OpenMenuAsync(host, null, 3.5);
        Element[] previous = Selected(host);
        HeadlessTestHelpers.Click(Item(menu, Strings.InvertSelection), move: true);
        Assert.That(Selected(host), Is.EquivalentTo(elements.Except(previous)));

        menu = await OpenMenuAsync(host, null, 3.5);
        HeadlessTestHelpers.Click(Item(menu, Strings.SelectAll), move: true);
        Assert.That(Selected(host), Is.EquivalentTo(elements));
        menu = await OpenMenuAsync(host, null, 3.5);
        HeadlessTestHelpers.Click(Item(menu, Strings.DeselectAll), move: true);
        Assert.That(Selected(host), Is.Empty);
        menu = await OpenMenuAsync(host, null, 3.5);
        Assert.That(Item(menu, Strings.SelectGroupElements).IsEnabled, Is.False);
        ((FAMenuFlyout)host.Timeline.ContextFlyout!).Hide();
    }

    [AvaloniaTest]
    public async Task Selection_from_the_menu_can_be_dragged_normally_with_groups_locks_and_undo()
    {
        using TimelineTestHost host = await CreateAsync();
        Element[] elements = [.. host.Scene.Children];
        host.Scene.Groups.Add([elements[0].Id.Value, elements[1].Id.Value]);
        elements[2].IsLocked.Value = true;
        host.Scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 2 }, IsLocked = { Value = true } });
        var editor = (EditViewModel)host.Model.EditorContext;
        editor.HistoryManager.Commit();
        int undoCount = editor.HistoryManager.UndoCount;
        HeadlessTestHelpers.Render(3);
        FAMenuFlyoutSubItem menu = await OpenMenuAsync(host, elements[1], 2.5);
        HeadlessTestHelpers.Click(Item(menu, Strings.SelectFollowingElementsAllLayers), move: true);
        Point from = ToWindow(host, 2.5, 0);
        Point to = from + new Vector(MediaTime.FromSeconds(1).TimeToPixel(1), 0);
        host.Window.MouseMove(from);
        host.Window.MouseDown(from, MouseButton.Left);
        host.Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        host.Window.MouseUp(to, MouseButton.Left);
        await HeadlessTestHelpers.WaitUntilAsync(() => !editor.HistoryManager.IsEditing);

        Assert.That(elements.Select(element => element.Start.Value.ToSeconds()),
            Is.EqualTo(new double[] { 1, 3, 4, 101, 2, 6, 2 }));
        Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + 1));
        Assert.That(await editor.UndoAsync(), Is.True);
        Assert.That(elements.Select(element => element.Start.Value.ToSeconds()),
            Is.EqualTo(new double[] { 0, 2, 4, 100, 1, 5, 2 }));
    }

    private static async Task<TimelineTestHost> CreateAsync(ThemeVariant? theme = null)
    {
        TimelineTestHost host = await TimelineTestHost.CreateAsync("bulk-selection", MediaTime.FromSeconds(10),
            [Describe(0, 2, 0), Describe(2, 1, 0), Describe(4, 1, 0), Describe(100, 1, 0),
                Describe(1, 1.75, 1), Describe(5, 1, 1), Describe(2, 2, 2)], theme: theme);
        host.Scene.Duration.Value = MediaTime.FromSeconds(10);
        return host;

        static ElementDescription Describe(double start, double length, int layer)
            => new(MediaTime.FromSeconds(start), MediaTime.FromSeconds(length), layer,
                new ElementSource.EngineObject(() => new RectShape()));
    }

    private static async Task<FAMenuFlyoutSubItem> OpenMenuAsync(TimelineTestHost host, Element? reference, double time)
    {
        Point point = ToWindow(host, time, 0);
        host.Window.MouseDown(point, MouseButton.Right);
        host.Window.MouseUp(point, MouseButton.Right);
        HeadlessTestHelpers.Render(3);
        FAMenuFlyoutSubItem menu = reference is null
            ? host.View.FindControl<FAMenuFlyoutSubItem>("BulkSelectionMenu")!
            : host.View.GetVisualDescendants().OfType<ElementView>()
                .Single(view => ReferenceEquals(((ElementViewModel)view.DataContext!).Model, reference))
                .FindControl<FAMenuFlyoutSubItem>("bulkSelectionMenu")!;
        Assert.That(menu.Focus(), Is.True);
        TopLevel root = TopLevel.GetTopLevel(menu)!;
        root.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        root.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        await HeadlessTestHelpers.WaitUntilAsync(() => TopLevel.GetTopLevel(Item(menu, Strings.SelectFollowingElements)) is not null);
        HeadlessTestHelpers.Render(3);
        return menu;
    }

    private static FAMenuFlyoutItem Item(FAMenuFlyoutSubItem menu, string text)
        => menu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Text == text);

    private static Element[] Selected(TimelineTestHost host) => [.. host.Model.SelectedElements.Select(item => item.Model)];

    private static Point ToWindow(TimelineTestHost host, double time, int layer)
        => host.Timeline.TranslatePoint(new Point(MediaTime.FromSeconds(time).TimeToPixel(host.Model.Options.Value.Scale),
            host.Model.CalculateLayerTop(layer) + 12), host.Window)!.Value;
}
