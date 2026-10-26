using System.Reactive.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class EditorSelectionTests
{
    [AvaloniaTest]
    public async Task The_timeline_shows_the_editor_selection_which_another_object_replaces()
    {
        (EditViewModel editor, TimelineTabViewModel timeline) = await OpenAsync("editor-selection", 3);
        IEditorSelection selection = editor.GetRequiredService<IEditorSelection>();
        ElementViewModel[] items = [.. timeline.Elements.OrderBy(item => item.Model.ZIndex.Value)];

        timeline.SelectElement(items[0]);
        timeline.SelectElement(items[1]);
        Assert.That(selection.SelectedElements, Is.EqualTo(new[] { items[0].Model, items[1].Model }));
        using (var reopened = new TimelineTabViewModel(editor))
            Assert.That(reopened.SelectedElements.Select(item => item.Model), Is.EquivalentTo(selection.SelectedElements));

        selection.Select(items[1].Model);
        Assert.That(selection.SelectedElements, Has.Count.EqualTo(2), "An object inside the selection keeps it.");
        selection.Select(items[2].Model);
        Assert.Multiple(() =>
        {
            Assert.That(selection.SelectedElements, Is.EqualTo(new[] { items[2].Model }));
            Assert.That(items.Select(item => item.IsSelected.Value), Is.EqualTo(new[] { false, false, true }));
        });

        editor.GetRequiredService<IElementStructureService>().Delete(editor.Scene, [items[2].Model]);
        Assert.That(selection.SelectedElements, Is.Empty);
    }

    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public async Task Delete_removes_the_selection_and_the_groups_of_every_selected_element(bool fromTimeline)
    {
        (EditViewModel editor, TimelineTabViewModel timeline) = await OpenAsync("editor-selection-delete", 5);
        Element[] elements = [.. editor.Scene.Children.OrderBy(element => element.ZIndex.Value)];
        editor.Scene.Groups.Add([elements[0].Id.Value, elements[1].Id.Value]);
        editor.Scene.Groups.Add([elements[2].Id.Value, elements[3].Id.Value]);
        editor.GetRequiredService<IEditorSelection>().SelectedElements.AddRange([elements[0], elements[2]]);

        if (fromTimeline)
        {
            await TimelineTabExtension.Instance.RunAsync(timeline, new ContextCommandExecution("timeline.delete"));
        }
        else
        {
            // The menu asks for no confirmation and needs no timeline.
            editor.CloseToolTab(timeline);
            HeadlessTestHelpers.Settle();
            await TestShell.MainViewModel.MenuBar.DeleteLayer.ExecuteAsync();
        }

        Assert.That(editor.Scene.Children, Is.EqualTo(new[] { elements[4] }));
    }

    [AvaloniaTest]
    [TestCase("copy")]
    [TestCase("nudge")]
    [TestCase("cut")]
    [TestCase("split")]
    [TestCase("duplicate")]
    [TestCase("delete")]
    [TestCase("drag")]
    [TestCase("ctrl-drag")]
    [TestCase("alt-drag")]
    public async Task Operations_include_every_selected_group_and_leave_unselected_groups_alone(string operation)
    {
        bool originalSnap = GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled.Value;
        bool originalRipple = GlobalConfiguration.Instance.EditorConfig.IsRippleEnabled.Value;
        GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled.Value = false;
        GlobalConfiguration.Instance.EditorConfig.IsRippleEnabled.Value = false;
        try
        {
            using TimelineTestHost host = await TimelineTestHost.CreateAsync(
                $"selection-groups-{operation}", MediaTime.FromSeconds(10),
                [.. Enumerable.Range(0, 6).Select(layer => new ElementDescription(
                    MediaTime.Zero, MediaTime.FromSeconds(1), layer, new ElementSource.EngineObject(() => new RectShape())))]);
            TimelineTabViewModel timeline = host.Model;
            var editor = (EditViewModel)timeline.EditorContext;
            ElementViewModel[] items = [.. timeline.Elements.OrderBy(item => item.Model.ZIndex.Value)];
            for (int i = 0; i < items.Length; i++)
                items[i].Model.Name.Value = ((char)('A' + i)).ToString();
            host.Scene.Groups.Add([items[0].Model.Id.Value, items[1].Model.Id.Value]);
            host.Scene.Groups.Add([items[2].Model.Id.Value, items[3].Model.Id.Value]);
            host.Scene.Groups.Add([items[4].Model.Id.Value, items[5].Model.Id.Value]);
            timeline.ClearSelected();
            ClipboardHelper.OverrideClipboard = host.Window.Clipboard;

            ClickClip(host, items[1]);
            ClickClip(host, items[2], TimelineTestHost.CommandModifier);
            Assert.That(timeline.SelectedElements, Is.EquivalentTo(new[] { items[1], items[2] }));
            editor.HistoryManager.Commit();
            int undoCount = editor.HistoryManager.UndoCount;

            string[] affected;
            if (operation.EndsWith("drag"))
            {
                Point press = ClipCenter(host, items[1]);
                Point release = press + new Vector(MediaTime.FromSeconds(3).TimeToPixel(1), 0);
                RawInputModifiers modifiers = operation switch
                {
                    "ctrl-drag" => TimelineTestHost.CommandModifier,
                    "alt-drag" => RawInputModifiers.Alt,
                    _ => RawInputModifiers.None
                };
                host.Window.MouseMove(press, modifiers);
                host.Window.MouseDown(press, MouseButton.Left, modifiers);
                Assert.That(timeline.SelectedElements, Is.EquivalentTo(new[] { items[1], items[2] }));
                host.Window.MouseMove(release, modifiers | RawInputModifiers.LeftMouseButton);
                HeadlessTestHelpers.Render(5);
                host.Window.MouseUp(release, MouseButton.Left, modifiers);
                HeadlessTestHelpers.Settle(5);
                affected = operation == "alt-drag"
                    ? [.. host.Scene.Children.Except(items.Select(item => item.Model)).Select(e => e.Name.Value)]
                    : [.. items.Where(item => item.Model.Start.Value != MediaTime.Zero).Select(item => item.Model.Name.Value)];
            }
            else
            {
                if (operation == "split") editor.GetRequiredService<IEditorClock>().Seek(MediaTime.FromSeconds(0.5));
                string command = operation == "nudge" ? "timeline.nudgeRightFrame" : $"timeline.{operation}";
                await TimelineTabExtension.Instance.RunAsync(operation == "split" ? items[1] : timeline,
                    new ContextCommandExecution(command));
                if (operation == "nudge") editor.GetRequiredService<IElementNudgeService>().Flush();
                HeadlessTestHelpers.Settle();
                affected = operation switch
                {
                    "copy" => JsonNode.Parse((await editor.GetRequiredService<IClipboardGateway>()
                        .TryGetStringAsync(BeutlClipboardFormats.Elements))!)!.AsArray()
                        .Select(node => node!["Name"]!.GetValue<string>()).ToArray(),
                    "nudge" => [.. items.Where(item => item.Model.Start.Value != MediaTime.Zero).Select(item => item.Model.Name.Value)],
                    "cut" or "delete" => [.. items.Where(item => !host.Scene.Children.Contains(item.Model)).Select(item => item.Model.Name.Value)],
                    "split" => [.. items.Where(item => item.Model.Length.Value != MediaTime.FromSeconds(1)).Select(item => item.Model.Name.Value)],
                    "duplicate" => [.. host.Scene.Children.Except(items.Select(item => item.Model)).Select(e => e.Name.Value)],
                    _ => throw new InvalidOperationException(operation)
                };
            }

            Assert.That(affected, Is.EquivalentTo(new[] { "A", "B", "C", "D" }));
            if (operation != "copy")
            {
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + 1));
                Assert.That(await editor.UndoAsync(), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(host.Scene.Children, Is.EquivalentTo(items.Select(item => item.Model)));
                    Assert.That(host.Scene.Children.Select(e => e.Start.Value), Is.All.EqualTo(MediaTime.Zero));
                    Assert.That(host.Scene.Children.Select(e => e.Length.Value), Is.All.EqualTo(MediaTime.FromSeconds(1)));
                    Assert.That(host.Scene.Groups, Has.Count.EqualTo(3));
                });
            }
        }
        finally
        {
            ClipboardHelper.OverrideClipboard = null;
            GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled.Value = originalSnap;
            GlobalConfiguration.Instance.EditorConfig.IsRippleEnabled.Value = originalRipple;
        }
    }

    [AvaloniaTest]
    public async Task Clicking_a_selected_clip_keeps_the_selection_and_an_unselected_clip_replaces_it()
    {
        using TimelineTestHost host = await TimelineTestHost.CreateAsync(
            "selection-click", MediaTime.FromSeconds(10),
            [.. Enumerable.Range(0, 3).Select(layer => new ElementDescription(
                MediaTime.Zero, MediaTime.FromSeconds(1), layer, new ElementSource.EngineObject(() => new RectShape())))]);
        ElementViewModel[] items = [.. host.Model.Elements.OrderBy(item => item.Model.ZIndex.Value)];
        IEditorSelection selection = host.Model.EditorContext.GetRequiredService<IEditorSelection>();
        host.Model.ClearSelected();

        ClickClip(host, items[0]);
        Assert.That(host.Model.SelectedElements.Select(item => item.Model.ZIndex.Value), Is.EqualTo(new[] { 0 }));
        ClickClip(host, items[1], TimelineTestHost.CommandModifier);
        Assert.That(host.Model.SelectedElements.Select(item => item.Model.ZIndex.Value), Is.EquivalentTo(new[] { 0, 1 }));
        ClickClip(host, items[0]);
        Assert.Multiple(() =>
        {
            Assert.That(host.Model.SelectedElements, Is.EquivalentTo(new[] { items[0], items[1] }));
            Assert.That(selection.SelectedObject.Value, Is.SameAs(items[0].Model));
        });

        ClickClip(host, items[2]);
        Assert.That(host.Model.SelectedElements, Is.EqualTo(new[] { items[2] }));
        ClickClip(host, items[0], TimelineTestHost.CommandModifier);
        ClickClip(host, items[2], TimelineTestHost.CommandModifier);
        Assert.That(host.Model.SelectedElements, Is.EqualTo(new[] { items[0] }));
        Assert.That(selection.SelectedObject.Value, Is.SameAs(items[2].Model));

        // The primary object can still be the clip that Ctrl+click just deselected.
        ClickClip(host, items[0], TimelineTestHost.CommandModifier);
        Assert.That(host.Model.SelectedElements, Is.Empty);
        ClickClip(host, items[2]);
        Assert.That(host.Model.SelectedElements, Is.EqualTo(new[] { items[2] }));
    }

    [AvaloniaTest]
    public async Task Selecting_from_another_thread_selects_on_the_UI_thread()
    {
        (EditViewModel editor, _) = await OpenAsync("editor-selection-thread", 1);
        IEditorSelection selection = editor.GetRequiredService<IEditorSelection>();
        Element element = editor.Scene.Children.Single();
        selection.Select(null);
        var notified = new List<bool>();
        using IDisposable subscription = selection.SelectedObject.Skip(1)
            .Subscribe(_ => notified.Add(Dispatcher.UIThread.CheckAccess()));

        await Task.Run(() => selection.Select(element));
        HeadlessTestHelpers.Settle();

        Assert.That(selection.SelectedObject.Value, Is.SameAs(element));
        Assert.That(notified, Is.EqualTo(new[] { true }));
    }

    [AvaloniaTest]
    public async Task Selecting_the_selected_object_notifies_only_when_asked()
    {
        (EditViewModel editor, _) = await OpenAsync("editor-selection-again", 1);
        IEditorSelection selection = editor.GetRequiredService<IEditorSelection>();
        Element element = editor.Scene.Children.Single();
        selection.Select(element);
        int notifications = 0;
        using IDisposable subscription = selection.SelectedObject.Skip(1).Subscribe(_ => notifications++);

        selection.Select(element);
        Assert.That(notifications, Is.Zero);
        selection.Select(element, notifyIfSelected: true);
        Assert.That(notifications, Is.EqualTo(1));
    }

    private static Point ClipCenter(TimelineTestHost host, ElementViewModel item)
    {
        Border border = host.View.GetVisualDescendants().OfType<ElementView>()
            .Single(view => ReferenceEquals(view.DataContext, item)).FindControl<Border>("border")!;
        Point point = border.TranslatePoint(new Point(border.Bounds.Width / 2, border.Bounds.Height / 2), host.Window)!.Value;
        TimelineTestHost.WaitForHitTest(host.Window, border, point);
        return point;
    }

    private static void ClickClip(TimelineTestHost host, ElementViewModel item, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Point point = ClipCenter(host, item);
        host.Window.MouseMove(point, modifiers);
        host.Window.MouseDown(point, MouseButton.Left, modifiers);
        host.Window.MouseUp(point, MouseButton.Left, modifiers);
        HeadlessTestHelpers.Render(3);
    }

    private static Task<(EditViewModel Editor, TimelineTabViewModel Timeline)> OpenAsync(string name, int count)
        => TimelineTestHost.OpenTimelineAsync(name, [.. Enumerable.Range(0, count).Select(layer => new ElementDescription(
            MediaTime.Zero, MediaTime.FromSeconds(1), layer, new ElementSource.EngineObject(() => new RectShape())))]);
}
