using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class CompactTimelineInteractionTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Narrow_clips_keep_group_drag_resize_undo_rename_and_context_menu(bool hoverFirst)
    {
        bool snap = GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled.Value;
        bool ripple = GlobalConfiguration.Instance.EditorConfig.IsRippleEnabled.Value;
        GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled.Value = false;
        GlobalConfiguration.Instance.EditorConfig.IsRippleEnabled.Value = false;
        Window? window = null;
        TimelineTabView? view = null;
        try
        {
            ElementDescription[] descriptions = Enumerable.Range(0, 3)
                .Select(layer => new ElementDescription(MediaTime.FromSeconds(2), MediaTime.FromSeconds(1.5), layer,
                    new ElementSource.EngineObject(() => new RectShape()))).ToArray();
            var (editor, timeline) = await TimelineTestHost.OpenTimelineAsync($"compact-input-{Guid.NewGuid():N}", descriptions);
            Scene scene = editor.Scene;
            Element[] elements = scene.Children.OrderBy(element => element.ZIndex.Value).ToArray();
            timeline.ClearSelected();
            scene.Groups.Add(ImmutableHashSet.Create(elements[0].Id.Value, elements[1].Id.Value));
            timeline.Options.Value = timeline.Options.Value with { Scale = 0.1f };
            view = new TimelineTabView { DataContext = timeline };
            window = new Window { Content = view, Width = 1100, Height = 420 };
            window.Show();
            HeadlessTestHelpers.Render(3);
            Assert.That(view.GetVisualDescendants().OfType<ElementView>(), Is.Empty);
            editor.HistoryManager.Commit();
            IPointer? pointer = null;
            window.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
                RoutingStrategies.Tunnel, handledEventsToo: true);

            Point point = ClipCenter(0);
            if (hoverFirst)
            {
                window.MouseMove(point);
                HeadlessTestHelpers.Render();
            }
            window.MouseDown(point, MouseButton.Left);
            Assert.That(timeline.GetViewModelFor(elements[0])!.IsSelected.Value, Is.True);
            Assert.That(pointer!.Captured, Is.SameAs(ViewFor(0).FindControl<Border>("border")));
            window.MouseMove(point + new Vector(15, 0), RawInputModifiers.LeftMouseButton);
            window.MouseUp(point + new Vector(15, 0), MouseButton.Left);
            await HeadlessTestHelpers.WaitUntilAsync(() => !editor.HistoryManager.IsEditing);
            Assert.That(elements.Take(2).Select(e => e.Start.Value),
                Is.All.EqualTo(MediaTime.FromSeconds(3)), "Both members use the existing grouped drag path.");
            Assert.That(await editor.UndoAsync(), Is.True);
            HeadlessTestHelpers.Render(3);
            Assert.That(elements.Take(2).Select(e => e.Start.Value), Is.All.EqualTo(MediaTime.FromSeconds(2)));

            point = ClipCenter(2) + new Vector(timeline.GetViewModelFor(elements[2])!.Width.Value / 2 - 0.01, 0);
            if (hoverFirst)
            {
                window.MouseMove(point);
                HeadlessTestHelpers.Render(3);
            }
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(point + new Vector(15, 0), RawInputModifiers.LeftMouseButton);
            window.MouseUp(point + new Vector(15, 0), MouseButton.Left);
            await HeadlessTestHelpers.WaitUntilAsync(() => !editor.HistoryManager.IsEditing);
            Assert.That(elements[2].Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(elements[2].Length.Value, Is.EqualTo(MediaTime.FromSeconds(2.5)),
                "An edge press must initialize the resize cursor even without a preceding hover.");
            Assert.That(await editor.UndoAsync(), Is.True);
            HeadlessTestHelpers.Render(3);
            Assert.That(elements[2].Length.Value, Is.EqualTo(MediaTime.FromSeconds(1.5)));

            timeline.GetViewModelFor(elements[1])!.RenameRequested();
            HeadlessTestHelpers.Render(3);
            ElementView renamed = ViewFor(1);
            Assert.That(renamed.FindControl<TextBox>("textBox")!.IsFocused, Is.True);
            view.TimelinePanel.Focusable = true;
            view.TimelinePanel.Focus();

            point = ClipCenter(2);
            window.MouseDown(point, MouseButton.Right);
            HeadlessTestHelpers.Render();
            window.MouseUp(point, MouseButton.Right);
            HeadlessTestHelpers.Render(3);
            var menu = (FAMenuFlyout)ViewFor(2).FindControl<Border>("border")!.ContextFlyout!;
            Assert.That(menu.IsOpen, Is.True, "Right-click promotes the clip and opens its normal menu.");
            menu.Hide();
            HeadlessCapture.SaveIfRequested(window, nameof(CompactTimelineInteractionTests), $"narrow-input-{hoverFirst}");

            ElementView ViewFor(int index) => view.GetVisualDescendants().OfType<ElementView>()
                .Single(v => ReferenceEquals(((ElementViewModel)v.DataContext!).Model, elements[index]));
            Point ClipCenter(int index)
            {
                ElementViewModel vm = timeline.GetViewModelFor(elements[index])!;
                return view.TimelinePanel.TranslatePoint(new Point(vm.BorderMargin.Value.Left + vm.Width.Value / 2,
                    vm.Margin.Value.Top + 12.5), window)!.Value;
            }
        }
        finally
        {
            if (view is not null) view.DataContext = null;
            window?.Close();
            await TestReset.ResetShellAsync();
            GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled.Value = snap;
            GlobalConfiguration.Instance.EditorConfig.IsRippleEnabled.Value = ripple;
        }
    }
}
