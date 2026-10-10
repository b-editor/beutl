using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Beutl.Animation.Easings;
using Beutl.Configuration;
using Beutl.Editor.Components;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.ObjectPropertyTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics.Transitions;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;
using RectShape = Beutl.Graphics.Shapes.RectShape;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ElementTransitionTimelineTests
{
    // The menu has one item per edge, which adds the default transition when the edge has none and opens
    // the element in the property tab, where the type and properties are edited.
    [AvaloniaTest]
    public async Task TheContextMenu_OpensTheTransitionAtEitherEdge()
    {
        Window? window = null;
        TimelineTabView? view = null;
        try
        {
            (EditViewModel editor, ElementViewModel outgoing, ElementViewModel incoming) = await OpenCut();
            (view, window) = Show(incoming);
            ElementView element = FindElementView(view, incoming);
            int undoCount = editor.HistoryManager.UndoCount;

            var flyout = (FAMenuFlyout)element.FindControl<Border>("border")!.ContextFlyout!;
            flyout.ShowAt(element);
            HeadlessTestHelpers.Render(3);
            FAMenuFlyoutItem[] items = element.FindControl<FAMenuFlyoutSubItem>("transitionMenu")!
                .Items.OfType<FAMenuFlyoutItem>().ToArray();
            string[] texts = [.. items.Select(item => item.Text ?? string.Empty)];
            items[0].RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
            flyout.Hide();
            HeadlessTestHelpers.Settle(3);
            int undoAfterAdding = editor.HistoryManager.UndoCount;
            incoming.OpenTransition(ElementEdge.Start);
            HeadlessTestHelpers.Settle(3);

            Assert.Multiple(() =>
            {
                Assert.That(texts, Is.EqualTo(new[] { Strings.EnterTransition, Strings.ExitTransition }));
                Assert.That(incoming.Model.EnterTransition, Is.TypeOf<CrossDissolveTransition>());
                Assert.That(outgoing.Model.ExitTransition, Is.TypeOf<CrossDissolveTransition>(),
                    "a new boundary transition is centred on the cut");
                Assert.That(undoAfterAdding, Is.EqualTo(undoCount + 1));
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoAfterAdding), "opening it again adds nothing");
                Assert.That(editor.FindToolTab<ObjectPropertyTabViewModel>()?.ChildContext.Value?.Target, Is.SameAs(incoming.Model));
                Assert.That(TransitionEditorOf(editor, ElementEdge.Start).Value.Value, Is.SameAs(incoming.Model.EnterTransition));
            });
        }
        finally
        {
            Close(view, window);
        }
    }

    // Picking a type in the property tab changes both sides of the boundary as one edit, and none removes
    // the transition from both.
    [AvaloniaTest]
    public async Task TheTypeEditor_ChangesAndRemovesTheTransitionOnBothSides()
    {
        Window? window = null;
        TimelineTabView? view = null;
        try
        {
            (EditViewModel editor, ElementViewModel outgoing, ElementViewModel incoming) = await OpenCut();
            (view, window) = Show(incoming);
            incoming.OpenTransition(ElementEdge.Start);
            HeadlessTestHelpers.Settle(3);
            ClipTransitionEditorViewModel transitionEditor = TransitionEditorOf(editor, ElementEdge.Start);
            int undoCount = editor.HistoryManager.UndoCount;

            transitionEditor.ChangeType(typeof(WipeTransition));
            HeadlessTestHelpers.Settle(3);
            Type? selected = transitionEditor.SelectedType.Value?.Type;
            bool showsDirection = transitionEditor.Properties.Value?.Properties
                .OfType<BaseEditorViewModel>()
                .Any(item => item.Header == GraphicsStrings.Direction) == true;
            Type? exitAfterChange = outgoing.Model.ExitTransition?.GetType();
            int undoAfterChange = editor.HistoryManager.UndoCount;
            transitionEditor.ChangeType(null);
            HeadlessTestHelpers.Settle(3);

            Assert.Multiple(() =>
            {
                Assert.That(transitionEditor.Types[0].Type, Is.Null, "the first entry is none");
                Assert.That(transitionEditor.Types.Select(item => item.Type), Does.Contain(typeof(IrisTransition)));
                Assert.That(selected, Is.EqualTo(typeof(WipeTransition)));
                Assert.That(showsDirection, Is.True, "a wipe shows its direction beneath the type");
                Assert.That(exitAfterChange, Is.EqualTo(typeof(WipeTransition)));
                Assert.That(undoAfterChange, Is.EqualTo(undoCount + 1));
                Assert.That(incoming.Model.EnterTransition, Is.Null);
                Assert.That(outgoing.Model.ExitTransition, Is.Null);
                Assert.That(transitionEditor.SelectedType.Value?.Type, Is.Null);
            });
        }
        finally
        {
            Close(view, window);
        }
    }

    [AvaloniaTest]
    public async Task ThePartsDrawOneRampAcrossTheCut_AndTheHandleDragsOneSide()
    {
        bool originalSnap = GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled;
        GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled = false;
        Window? window = null;
        TimelineTabView? view = null;
        try
        {
            (EditViewModel editor, ElementViewModel outgoing, ElementViewModel incoming) = await OpenCut();
            (view, window) = Show(incoming);
            incoming.ApplyTransition(ElementEdge.Start, typeof(CrossDissolveTransition));
            HeadlessTestHelpers.Settle(3);
            HeadlessTestHelpers.Render(5);
            Capture(window, "transition-parts");
            float scale = incoming.Timeline.Options.Value.Scale;
            Panel exitPart = FindElementView(view, outgoing).FindControl<Panel>("exitTransitionPart")!;
            Panel enterPart = FindElementView(view, incoming).FindControl<Panel>("enterTransitionPart")!;
            double quarter = TimeSpan.FromSeconds(0.25).TimeToPixel(scale);

            Assert.Multiple(() =>
            {
                Assert.That(exitPart.IsVisible, Is.True);
                Assert.That(enterPart.IsVisible, Is.True);
                Assert.That(exitPart.Bounds.Width, Is.EqualTo(quarter).Within(0.5));
                Assert.That(enterPart.Bounds.Width, Is.EqualTo(quarter).Within(0.5));
                Assert.That(outgoing.ExitTransitionPart.Value.EndFraction, Is.EqualTo(0.5).Within(1e-6));
                Assert.That(incoming.EnterTransitionPart.Value.StartFraction, Is.EqualTo(0.5).Within(1e-6));
                Assert.That(incoming.EnterTransitionPart.Value.EndFraction, Is.EqualTo(1).Within(1e-6));
            });

            int undoCount = editor.HistoryManager.UndoCount;
            Border handle = FindElementView(view, incoming).FindControl<Border>("enterTransitionHandle")!;
            Point press = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), window)!.Value;
            Point release = press + new Vector(quarter, 0);
            WaitForHitTest(window, handle, press);
            window.MouseMove(press);
            window.MouseDown(press, MouseButton.Left);
            window.MouseMove(release, RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Settle(2);
            Capture(window, "transition-parts-dragging");
            double previewWidth = enterPart.Bounds.Width;
            TimeSpan durationDuringDrag = incoming.Model.EnterTransition!.Duration.CurrentValue;
            // The span grows to 0.75 s with the cut a third of the way in, so both halves of the ramp
            // change their slope while the pointer moves, not only the dragged one.
            TransitionPartLayout incomingDuringDrag = incoming.EnterTransitionPart.Value;
            TransitionPartLayout outgoingDuringDrag = outgoing.ExitTransitionPart.Value;
            window.MouseUp(release, MouseButton.Left);
            HeadlessTestHelpers.Settle(3);
            HeadlessTestHelpers.Render(3);

            Assert.Multiple(() =>
            {
                Assert.That(previewWidth, Is.EqualTo(quarter * 2).Within(0.5));
                Assert.That(durationDuringDrag, Is.EqualTo(TimeSpan.FromSeconds(0.25)), "the drag previews without editing");
                Assert.That(incomingDuringDrag.StartFraction, Is.EqualTo(1d / 3).Within(1e-6));
                Assert.That(incomingDuringDrag.EndFraction, Is.EqualTo(1).Within(1e-6));
                Assert.That(outgoingDuringDrag.StartFraction, Is.Zero);
                Assert.That(outgoingDuringDrag.EndFraction, Is.EqualTo(1d / 3).Within(1e-6));
                Assert.That(incoming.Model.EnterTransition!.Duration.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(0.5)));
                Assert.That(outgoing.Model.ExitTransition!.Duration.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(0.25)));
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + 1));
                Assert.That(incoming.Model.Start, Is.EqualTo(TimeSpan.FromSeconds(2)), "the handle must not move the clip");
                Assert.That(enterPart.Bounds.Width, Is.EqualTo(quarter * 2).Within(0.5));
            });

            // The side that decides how the boundary blends also decides the curve both halves follow.
            var easing = new CubicEaseInOut();
            incoming.Model.EnterTransition!.Easing.CurrentValue = easing;
            HeadlessTestHelpers.Settle(3);
            HeadlessTestHelpers.Render(3);
            Capture(window, "transition-parts-eased");
            Assert.Multiple(() =>
            {
                Assert.That(enterPart.GetVisualDescendants().OfType<TransitionRamp>().Single().Easing, Is.SameAs(easing));
                Assert.That(exitPart.GetVisualDescendants().OfType<TransitionRamp>().Single().Easing, Is.SameAs(easing));
            });
        }
        finally
        {
            GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled = originalSnap;
            Close(view, window);
        }
    }

    // 0.25 s is seven and a half frames at 30 fps, so snapping the duration on a press without a drag
    // would change it. The element's name runs past the handle, which must stay above it.
    [AvaloniaTest]
    public async Task PressingAHandleWithoutDragging_LeavesTheDurationAlone()
    {
        Window? window = null;
        TimelineTabView? view = null;
        try
        {
            (EditViewModel editor, _, ElementViewModel incoming) = await OpenCut();
            incoming.Model.Name = "An element name long enough to run past the transition handle";
            (view, window) = Show(incoming);
            incoming.ApplyTransition(ElementEdge.Start, typeof(CrossDissolveTransition));
            HeadlessTestHelpers.Settle(3);
            HeadlessTestHelpers.Render(5);
            int undoCount = editor.HistoryManager.UndoCount;
            Border handle = FindElementView(view, incoming).FindControl<Border>("enterTransitionHandle")!;
            Point press = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), window)!.Value;
            WaitForHitTest(window, handle, press);

            window.MouseMove(press);
            window.MouseDown(press, MouseButton.Left);
            window.MouseUp(press, MouseButton.Left);
            HeadlessTestHelpers.Settle(3);

            Assert.Multiple(() =>
            {
                Assert.That(incoming.Model.EnterTransition!.Duration.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(0.25)));
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount));
            });
        }
        finally
        {
            Close(view, window);
        }
    }

    // A disabled transition draws no ramp, but it can still be opened, edited and removed.
    [AvaloniaTest]
    public async Task ADisabledTransition_StaysEditableAndRemovable()
    {
        Window? window = null;
        TimelineTabView? view = null;
        try
        {
            (EditViewModel editor, _, ElementViewModel incoming) = await OpenCut();
            (view, window) = Show(incoming);
            var transition = new FadeTransition { IsEnabled = false };
            incoming.Model.EnterTransition = transition;
            HeadlessTestHelpers.Settle(3);

            bool partVisible = incoming.EnterTransitionPart.Value.IsVisible;
            Type? type = incoming.GetTransitionType(ElementEdge.Start);
            incoming.EditTransition(ElementEdge.Start);
            object? edited = TransitionEditorOf(editor, ElementEdge.Start).Value.Value;
            TransitionEditorOf(editor, ElementEdge.Start).ChangeType(null);

            Assert.Multiple(() =>
            {
                Assert.That(partVisible, Is.False);
                Assert.That(type, Is.EqualTo(typeof(FadeTransition)));
                Assert.That(edited, Is.SameAs(transition));
                Assert.That(incoming.Model.EnterTransition, Is.Null);
            });
        }
        finally
        {
            Close(view, window);
        }
    }

    // The transition editor follows the lock of the element that owns the transition.
    [AvaloniaTest]
    public async Task TheTransitionOfALockedElement_OpensReadOnly()
    {
        Window? window = null;
        TimelineTabView? view = null;
        try
        {
            (EditViewModel editor, _, ElementViewModel incoming) = await OpenCut();
            (view, window) = Show(incoming);
            incoming.ApplyTransition(ElementEdge.Start, typeof(CrossDissolveTransition));
            HeadlessTestHelpers.Settle(3);

            incoming.EditTransition(ElementEdge.Start);
            BaseEditorViewModel unlocked = EditorOf(editor);
            bool readOnlyWhileUnlocked = unlocked.IsReadOnly.Value;
            bool typeEditableWhileUnlocked = TransitionEditorOf(editor, ElementEdge.Start).CanChangeType.Value;
            incoming.Model.IsLocked = true;
            HeadlessTestHelpers.Settle(3);
            bool readOnlyOnceLocked = unlocked.IsReadOnly.Value;
            bool typeEditableOnceLocked = TransitionEditorOf(editor, ElementEdge.Start).CanChangeType.Value;

            Assert.Multiple(() =>
            {
                Assert.That(readOnlyWhileUnlocked, Is.False);
                Assert.That(typeEditableWhileUnlocked, Is.True);
                Assert.That(readOnlyOnceLocked, Is.True);
                Assert.That(typeEditableOnceLocked, Is.False);
            });
        }
        finally
        {
            Close(view, window);
        }

        static BaseEditorViewModel EditorOf(EditViewModel editor)
            => TransitionEditorOf(editor, ElementEdge.Start).Properties.Value!.Properties
                .OfType<EasingEditorViewModel<Easing>>()
                .Single();
    }

    // A clip that overlaps another on its layer is drawn above it; a transition dropped on it goes to it,
    // not to the clip beneath.
    [AvaloniaTest]
    public async Task ATransitionDroppedOnOverlappingClips_GoesToTheClipUnderThePointer()
    {
        Window? window = null;
        TimelineTabView? view = null;
        try
        {
            (EditViewModel editor, ElementViewModel outgoing, _) = await OpenCut();
            Element over = editor.Scene.Children.Last();
            var shape = new RectShape();
            var top = new Element { Start = TimeSpan.FromSeconds(0.5), Length = TimeSpan.FromSeconds(1), ZIndex = 0 };
            top.Objects.Add(shape);
            editor.Scene.Children.Add(top);
            HeadlessTestHelpers.Settle(3);
            ElementViewModel topViewModel = outgoing.Timeline.Elements.Single(item => item.Model == top);
            (view, window) = Show(topViewModel);
            Border border = FindElementView(view, topViewModel).FindControl<Border>("border")!;
            Point point = new(3, border.Bounds.Height / 2);
            using var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(BeutlDataFormats.ClipTransition, TypeFormat.ToString(typeof(FadeTransition))));

            border.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, transfer, border, point, KeyModifiers.None));
            HeadlessTestHelpers.Settle(3);

            Assert.Multiple(() =>
            {
                Assert.That(top.EnterTransition, Is.TypeOf<FadeTransition>());
                Assert.That(outgoing.Model.EnterTransition, Is.Null);
                Assert.That(outgoing.Model.ExitTransition, Is.Null);
                Assert.That(over.EnterTransition, Is.Null);
            });
        }
        finally
        {
            Close(view, window);
        }
    }

    [AvaloniaTest]
    public async Task DoubleClickingTheCut_AddsTheDefaultTransition_ThenEditsIt()
    {
        Window? window = null;
        TimelineTabView? view = null;
        try
        {
            (EditViewModel editor, ElementViewModel outgoing, ElementViewModel incoming) = await OpenCut();
            (view, window) = Show(incoming);
            Border border = FindElementView(view, incoming).FindControl<Border>("border")!;
            string name = incoming.Model.Name;

            Point edge = border.TranslatePoint(new Point(3, border.Bounds.Height / 2), window)!.Value;
            await DoubleClick(window, edge);

            Assert.Multiple(() =>
            {
                Assert.That(incoming.Model.EnterTransition, Is.TypeOf<CrossDissolveTransition>());
                Assert.That(incoming.Model.EnterTransition?.Duration.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(0.25)));
                Assert.That(outgoing.Model.ExitTransition?.Duration.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(0.25)));
                Assert.That(incoming.Model.Name, Is.EqualTo(name));
            });

            await DoubleClick(window, edge);
            ObjectPropertyTabViewModel? tab = editor.FindToolTab<ObjectPropertyTabViewModel>();
            Assert.Multiple(() =>
            {
                Assert.That(tab?.ChildContext.Value?.Target, Is.SameAs(incoming.Model));
                Assert.That(TransitionEditorOf(editor, ElementEdge.Start).Value.Value, Is.SameAs(incoming.Model.EnterTransition));
                Assert.That(TransitionEditorOf(editor, ElementEdge.Start).Properties.Value?.Properties,
                    Has.Some.TypeOf<EasingEditorViewModel<Easing>>());
            });
        }
        finally
        {
            Close(view, window);
        }
    }

    [AvaloniaTest]
    public async Task DoubleClickingAnEdgeWithNothingBeside_StillRenames()
    {
        Window? window = null;
        TimelineTabView? view = null;
        try
        {
            (_, _, ElementViewModel incoming) = await OpenCut();
            (view, window) = Show(incoming);
            ElementView element = FindElementView(view, incoming);
            Border border = element.FindControl<Border>("border")!;

            Point edge = border.TranslatePoint(new Point(border.Bounds.Width - 3, border.Bounds.Height / 2), window)!.Value;
            await DoubleClick(window, edge);

            Assert.Multiple(() =>
            {
                Assert.That(incoming.Model.ExitTransition, Is.Null);
                Assert.That(element.FindControl<TextBox>("textBox")!.IsVisible, Is.True);
            });
        }
        finally
        {
            Close(view, window);
        }
    }

    // The editor of the shown element's transition at edge in the property tab; the element lists its
    // enter transition before its exit transition.
    private static ClipTransitionEditorViewModel TransitionEditorOf(EditViewModel editor, ElementEdge edge)
    {
        ClipTransitionEditorViewModel[] editors = [.. editor.FindToolTab<ObjectPropertyTabViewModel>()!.ChildContext.Value!.Properties
            .OfType<ClipTransitionEditorViewModel>()];
        Assert.That(editors, Has.Length.EqualTo(2));
        return editors[edge == ElementEdge.Start ? 0 : 1];
    }

    // Avalonia's headless hit test answers only after the window has rendered a frame, so a press made
    // before then lands on nothing.
    private static void WaitForHitTest(Window window, Visual target, Point point)
    {
        for (int i = 0; i < 20; i++)
        {
            HeadlessTestHelpers.Render();
            if (window.InputHitTest(point) is Visual hit && (hit == target || target.IsVisualAncestorOf(hit)))
                return;
        }

        Assert.Fail($"{target.Name} never became hit-testable at {point}.");
    }

    // Clicks once and lets the double-click window (500 ms by default) pass first. The first press on an
    // element selects it, which on a cold run can take longer than that window, and after an earlier
    // double-click the presses would count as a third and fourth click.
    private static async Task DoubleClick(Window window, Point point)
    {
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        HeadlessTestHelpers.Settle(4);
        await Task.Delay(1000);

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        HeadlessTestHelpers.Settle(4);
    }

    // Two rectangles on layer 0 meeting at two seconds.
    private static async Task<(EditViewModel Editor, ElementViewModel Outgoing, ElementViewModel Incoming)> OpenCut()
    {
        await TestReset.ResetShellAsync();
        string name = $"element-transition-{Guid.NewGuid():N}";
        string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(directory);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, name, directory))!;
        Scene scene = project.Items.OfType<Scene>().Single();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        ElementDescription[] descriptions =
        [
            new(TimeSpan.Zero, TimeSpan.FromSeconds(2), 0, new ElementSource.EngineObject(() => new RectShape())),
            new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), 0, new ElementSource.EngineObject(() => new RectShape())),
        ];
        Assert.That((await adder.AddAsync(descriptions, CancellationToken.None)).IsSuccess, Is.True);
        scene.Duration = TimeSpan.FromSeconds(10);
        TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        timeline.Options.Value = timeline.Options.Value with { Scale = 1, Offset = System.Numerics.Vector2.Zero };
        HeadlessTestHelpers.Settle();
        Element outgoing = scene.Children.Single(model => model.Start == TimeSpan.Zero);
        Element incoming = scene.Children.Single(model => model.Start == TimeSpan.FromSeconds(2));
        editor.HistoryManager.Commit();
        return (editor, timeline.GetViewModelFor(outgoing)!, timeline.GetViewModelFor(incoming)!);
    }

    private static (TimelineTabView View, Window Window) Show(ElementViewModel element)
    {
        var view = new TimelineTabView { DataContext = element.Timeline };
        var window = new Window { Content = view, Width = 1200, Height = 420 };
        window.Show();
        HeadlessTestHelpers.Render(5);
        return (view, window);
    }

    private static ElementView FindElementView(TimelineTabView view, ElementViewModel element)
    {
        return view.GetVisualDescendants().OfType<ElementView>()
            .Single(v => ReferenceEquals(v.DataContext, element));
    }

    private static void Close(TimelineTabView? view, Window? window)
    {
        window?.MouseUp(new Point(5, 5), MouseButton.Left);
        if (view is not null) view.DataContext = null;
        window?.Close();
        HeadlessTestHelpers.Settle();
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("BEUTL_DRAG_CAPTURE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null);
        frame!.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
    }
}
