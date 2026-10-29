using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Animation.Easings;
using Beutl.Configuration;
using Beutl.Editor.Components;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.ObjectPropertyTab.ViewModels;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Components.TransitionTab.ViewModels;
using Beutl.Editor.Components.TransitionTab.Views;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Transitions;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;
using GradientSpreadMethod = Beutl.Media.GradientSpreadMethod;
using RectShape = Beutl.Graphics.Shapes.RectShape;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ElementTransitionTimelineTests
{
    // The menu has one item per edge, which adds the default transition when the edge has none and opens
    // it in the transition tool, where its type and properties are edited.
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
            outgoing.OpenTransition(ElementEdge.End);
            HeadlessTestHelpers.Settle(3);

            Assert.Multiple(() =>
            {
                Assert.That(texts, Is.EqualTo(new[] { Strings.EnterTransition, Strings.ExitTransition }));
                Assert.That(incoming.Model.EnterTransition.Value, Is.TypeOf<CrossDissolveTransition>());
                Assert.That(outgoing.Model.ExitTransition.Value, Is.TypeOf<CrossDissolveTransition>(),
                    "a new boundary transition is centred on the cut");
                Assert.That(undoAfterAdding, Is.EqualTo(undoCount + 1));
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoAfterAdding), "opening it again adds nothing");
                Assert.That(TransitionTabOf(editor).Transition.Value, Is.SameAs(incoming.Model.EnterTransition.Value),
                    "from the outgoing side too, the side that decides how the boundary blends opens");
                Assert.That(editor.FindToolTab<ObjectPropertyTabViewModel>(), Is.Null, "the property tab is not used");
            });
        }
        finally
        {
            TimelineTestHost.Close(view, window);
        }
    }

    // Picking a type in the transition tool changes both sides of the boundary as one edit, and none
    // removes the transition from both.
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
            TransitionTabViewModel transitionEditor = TransitionTabOf(editor);
            int undoCount = editor.HistoryManager.UndoCount;

            transitionEditor.ChangeType(typeof(WipeTransition));
            HeadlessTestHelpers.Settle(3);
            string? typeName = transitionEditor.TypeName.Value;
            bool showsDirection = transitionEditor.Properties.Value?.Properties
                .OfType<PropertyEditorViewModel>()
                .Any(item => item.Header == GraphicsStrings.WipeTransition_LinearDirection) == true;
            Type? exitAfterChange = outgoing.Model.ExitTransition.Value?.GetType();
            int undoAfterChange = editor.HistoryManager.UndoCount;
            transitionEditor.ChangeType(null);
            HeadlessTestHelpers.Settle(3);

            Assert.Multiple(() =>
            {
                Assert.That(typeName, Is.EqualTo(TypeDisplayHelpers.GetLocalizedName(typeof(WipeTransition))));
                Assert.That(showsDirection, Is.True, "a wipe shows its direction beneath the type");
                Assert.That(exitAfterChange, Is.EqualTo(typeof(WipeTransition)));
                Assert.That(undoAfterChange, Is.EqualTo(undoCount + 1));
                Assert.That(incoming.Model.EnterTransition.Value, Is.Null);
                Assert.That(outgoing.Model.ExitTransition.Value, Is.Null);
                Assert.That(transitionEditor.TypeName.Value, Is.Null);
            });
        }
        finally
        {
            TimelineTestHost.Close(view, window);
        }
    }

    [AvaloniaTest]
    [TestCase("ja", 320)]
    [TestCase("en", 480)]
    public async Task AddedTypes_AreAvailableAndEditableInTheTransitionTool(string language, int width)
    {
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        Window? window = null;
        TransitionTabView? view = null;
        try
        {
            (EditViewModel editor, ElementViewModel outgoing, ElementViewModel incoming) = await OpenCut();
            incoming.OpenTransition(ElementEdge.Start);
            TransitionTabViewModel model = TransitionTabOf(editor);
            view = new TransitionTabView { DataContext = model };
            window = new Window { Content = view, Width = width, Height = 420 };
            window.Show();
            (Type Type, string[] Properties)[] choices =
            [
                (typeof(BlindsTransition), [$"{GraphicsStrings.Angle} (°)", GraphicsStrings.BlindsTransition_Count]),
                (typeof(CheckerboardTransition), [GraphicsStrings.CheckerboardTransition_Columns, GraphicsStrings.CheckerboardTransition_Rows]),
                (typeof(WipeTransition), [GraphicsStrings.WipeTransition_Shape, GraphicsStrings.WipeTransition_LinearDirection, GraphicsStrings.WipeTransition_Clockwise]),
                (typeof(IrisTransition), [GraphicsStrings.Direction]),
                (typeof(ZoomTransition), [GraphicsStrings.Direction, GraphicsStrings.TileBrush_TileMode]),
                (typeof(ZoomBlurTransition), [GraphicsStrings.ClipTransition_Strength, GraphicsStrings.Direction, GraphicsStrings.TileBrush_TileMode]),
                (typeof(WhipPanTransition), [GraphicsStrings.ClipTransition_Strength, GraphicsStrings.Direction]),
                (typeof(GlitchTransition), [GraphicsStrings.ClipTransition_Strength]),
                (typeof(LightLeakTransition), [GraphicsStrings.ClipTransition_Strength]),
            ];
            foreach ((Type type, string[] properties) in choices)
            {
                Assert.That(LibraryService.Current.FindItem(type), Is.Not.Null, "available in the type picker");
                Type? previousType = model.Transition.Value?.GetType();
                model.ChangeType(type);
                HeadlessTestHelpers.Render(3);
                Assert.That(model.TypeName.Value, Is.EqualTo(TypeDisplayHelpers.GetLocalizedName(type)));
                Assert.That(outgoing.Model.ExitTransition.Value, Is.TypeOf(type), "both sides change together");
                string[] headers = model.Properties.Value!.Properties.OfType<PropertyEditorViewModel>()
                    .Select(property => property.Header).ToArray();
                Assert.That(headers, Is.SupersetOf(properties));
                var picker = view.FindControl<Beutl.Controls.PropertyEditors.DropDownPickerEditor>("typeEditor")!;
                Assert.That(picker.Bounds.Width, Is.GreaterThan(0));
                Assert.That(picker.Bounds.Right, Is.LessThanOrEqualTo(view.Bounds.Width + 1));
                HeadlessCapture.SaveIfRequested(view, nameof(ElementTransitionTimelineTests), $"{type.Name}-{language}-{width}");

                // Edit through the same property contexts used by the controls, including undo and redo.
                switch (model.Transition.Value)
                {
                    case WipeTransition wipe:
                        var shapeEditor = model.Properties.Value.Properties.OfType<EnumEditorViewModel<WipeTransitionShape>>().Single();
                        foreach (WipeTransitionShape shape in new[] { WipeTransitionShape.Box, WipeTransitionShape.Diamond, WipeTransitionShape.Clock })
                            CheckSetting(shapeEditor, () => wipe.Shape.Value, shape);
                        break;
                    case BlindsTransition blinds:
                        var angleEditor = model.Properties.Value.Properties.OfType<NumberEditorViewModel<float>>().Single();
                        CheckSetting(angleEditor, () => blinds.Angle.Value, -37.5f);
                        break;
                    case IrisTransition iris:
                        var irisEditor = model.Properties.Value.Properties.OfType<EnumEditorViewModel<IrisTransitionDirection>>().Single();
                        CheckSetting(irisEditor, () => iris.Direction.Value, IrisTransitionDirection.Out);
                        break;
                    case ZoomTransition zoom:
                        CheckSetting(model.Properties.Value.Properties.OfType<EnumEditorViewModel<ZoomTransitionDirection>>().Single(),
                            () => zoom.Direction.Value, ZoomTransitionDirection.Out);
                        foreach (GradientSpreadMethod tileMode in new[] { GradientSpreadMethod.Pad, GradientSpreadMethod.Reflect, GradientSpreadMethod.Repeat })
                            CheckSetting(model.Properties.Value.Properties.OfType<EnumEditorViewModel<GradientSpreadMethod>>().Single(),
                                () => zoom.TileMode.Value, tileMode);
                        break;
                    case ZoomBlurTransition zoomBlur:
                        CheckSetting(model.Properties.Value.Properties.OfType<EnumEditorViewModel<ZoomTransitionDirection>>().Single(),
                            () => zoomBlur.Direction.Value, ZoomTransitionDirection.Out);
                        foreach (GradientSpreadMethod tileMode in new[] { GradientSpreadMethod.Pad, GradientSpreadMethod.Reflect, GradientSpreadMethod.Repeat })
                            CheckSetting(model.Properties.Value.Properties.OfType<EnumEditorViewModel<GradientSpreadMethod>>().Single(),
                                () => zoomBlur.TileMode.Value, tileMode);
                        break;
                }

                Assert.That(editor.HistoryManager.Undo(), Is.True);
                HeadlessTestHelpers.Settle(3);
                Assert.That(model.Transition.Value?.GetType(), Is.EqualTo(previousType));
                Assert.That(editor.HistoryManager.Redo(), Is.True);
                HeadlessTestHelpers.Settle(3);
                Assert.That(incoming.Model.EnterTransition.Value, Is.TypeOf(type));

                void CheckSetting<T>(PropertyEditorViewModel<T> property, Func<T> read, T value)
                {
                    T original = read();
                    property.SetValue(original, value);
                    HeadlessTestHelpers.Render(3);
                    Assert.That(read(), Is.EqualTo(value));
                    HeadlessCapture.SaveIfRequested(view, nameof(ElementTransitionTimelineTests), $"{type.Name}-{value}-{language}-{width}");
                    Assert.That(editor.HistoryManager.Undo(), Is.True);
                    HeadlessTestHelpers.Settle(3);
                    Assert.That(read(), Is.EqualTo(original));
                    Assert.That(editor.HistoryManager.Redo(), Is.True);
                    HeadlessTestHelpers.Settle(3);
                    Assert.That(read(), Is.EqualTo(value));
                    Assert.That(editor.HistoryManager.Undo(), Is.True);
                    HeadlessTestHelpers.Settle(3);
                }
            }
        }
        finally
        {
            if (view != null) view.DataContext = null;
            window?.Close();
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [AvaloniaTest]
    public async Task ThePartsDrawOneRampAcrossTheCut_AndTheHandleDragsOneSide()
    {
        bool originalSnap = GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled.Value;
        GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled.Value = false;
        Window? window = null;
        TimelineTabView? view = null;
        try
        {
            (EditViewModel editor, ElementViewModel outgoing, ElementViewModel incoming) = await OpenCut();
            (view, window) = Show(incoming);
            incoming.ApplyTransition(ElementEdge.Start, typeof(CrossDissolveTransition));
            HeadlessTestHelpers.Settle(3);
            HeadlessTestHelpers.Render(5);
            float scale = incoming.Timeline.Options.Value.Scale;
            Panel exitPart = FindElementView(view, outgoing).FindControl<Panel>("exitTransitionPart")!;
            Panel enterPart = FindElementView(view, incoming).FindControl<Panel>("enterTransitionPart")!;
            double quarter = MediaTime.FromSeconds(0.25).TimeToPixel(scale);

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
            TimelineTestHost.WaitForHitTest(window, handle, press);
            window.MouseMove(press);
            window.MouseDown(press, MouseButton.Left);
            window.MouseMove(release, RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Settle(2);
            double previewWidth = enterPart.Bounds.Width;
            MediaTime durationDuringDrag = incoming.Model.EnterTransition.Value!.Duration.Value;
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
                Assert.That(durationDuringDrag, Is.EqualTo(MediaTime.FromSeconds(0.25)), "the drag previews without editing");
                Assert.That(incomingDuringDrag.StartFraction, Is.EqualTo(1d / 3).Within(1e-6));
                Assert.That(incomingDuringDrag.EndFraction, Is.EqualTo(1).Within(1e-6));
                Assert.That(outgoingDuringDrag.StartFraction, Is.Zero);
                Assert.That(outgoingDuringDrag.EndFraction, Is.EqualTo(1d / 3).Within(1e-6));
                Assert.That(incoming.Model.EnterTransition.Value!.Duration.Value, Is.EqualTo(MediaTime.FromSeconds(0.5)));
                Assert.That(outgoing.Model.ExitTransition.Value!.Duration.Value, Is.EqualTo(MediaTime.FromSeconds(0.25)));
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount + 1));
                Assert.That(incoming.Model.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2)), "the handle must not move the clip");
                Assert.That(enterPart.Bounds.Width, Is.EqualTo(quarter * 2).Within(0.5));
            });

            // The side that decides how the boundary blends also decides the curve both halves follow.
            var easing = new CubicEaseInOut();
            incoming.Model.EnterTransition.Value!.Easing.Value = easing;
            HeadlessTestHelpers.Settle(3);
            HeadlessTestHelpers.Render(3);
            Assert.Multiple(() =>
            {
                Assert.That(enterPart.GetVisualDescendants().OfType<TransitionRamp>().Single().Easing, Is.SameAs(easing));
                Assert.That(exitPart.GetVisualDescendants().OfType<TransitionRamp>().Single().Easing, Is.SameAs(easing));
            });
        }
        finally
        {
            GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled.Value = originalSnap;
            TimelineTestHost.Close(view, window);
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
            incoming.Model.Name.Value = "An element name long enough to run past the transition handle";
            (view, window) = Show(incoming);
            incoming.ApplyTransition(ElementEdge.Start, typeof(CrossDissolveTransition));
            HeadlessTestHelpers.Settle(3);
            HeadlessTestHelpers.Render(5);
            int undoCount = editor.HistoryManager.UndoCount;
            Border handle = FindElementView(view, incoming).FindControl<Border>("enterTransitionHandle")!;
            Point press = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), window)!.Value;
            TimelineTestHost.WaitForHitTest(window, handle, press);

            window.MouseMove(press);
            window.MouseDown(press, MouseButton.Left);
            window.MouseUp(press, MouseButton.Left);
            HeadlessTestHelpers.Settle(3);

            Assert.Multiple(() =>
            {
                Assert.That(incoming.Model.EnterTransition.Value!.Duration.Value, Is.EqualTo(MediaTime.FromSeconds(0.25)));
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount));
            });
        }
        finally
        {
            TimelineTestHost.Close(view, window);
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
            var transition = new FadeTransition { IsEnabled = { Value = false } };
            incoming.Model.EnterTransition.Value = transition;
            HeadlessTestHelpers.Settle(3);

            bool partVisible = incoming.EnterTransitionPart.Value.IsVisible;
            Type? type = incoming.GetTransitionType(ElementEdge.Start);
            incoming.EditTransition(ElementEdge.Start);
            object? edited = TransitionTabOf(editor).Transition.Value;
            TransitionTabOf(editor).ChangeType(null);

            Assert.Multiple(() =>
            {
                Assert.That(partVisible, Is.False);
                Assert.That(type, Is.EqualTo(typeof(FadeTransition)));
                Assert.That(edited, Is.SameAs(transition));
                Assert.That(incoming.Model.EnterTransition.Value, Is.Null);
            });
        }
        finally
        {
            TimelineTestHost.Close(view, window);
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
            (EditViewModel editor, ElementViewModel outgoing, ElementViewModel incoming) = await OpenCut();
            (view, window) = Show(incoming);
            incoming.ApplyTransition(ElementEdge.Start, typeof(CrossDissolveTransition));
            HeadlessTestHelpers.Settle(3);

            incoming.EditTransition(ElementEdge.Start);
            PropertyEditorViewModel unlocked = EditorOf(editor);
            bool readOnlyWhileUnlocked = !unlocked.CanEdit.Value;
            bool typeEditableWhileUnlocked = TransitionTabOf(editor).CanEdit.Value;
            incoming.Model.IsLocked.Value = true;
            HeadlessTestHelpers.Settle(3);
            bool readOnlyOnceLocked = !unlocked.CanEdit.Value;
            bool typeEditableOnceLocked = TransitionTabOf(editor).CanEdit.Value;
            // From the outgoing side the tool shows the locked incoming side, which decides the boundary.
            outgoing.EditTransition(ElementEdge.End);
            bool typeEditableFromOutgoing = TransitionTabOf(editor).CanEdit.Value;

            Assert.Multiple(() =>
            {
                Assert.That(readOnlyWhileUnlocked, Is.False);
                Assert.That(typeEditableWhileUnlocked, Is.True);
                Assert.That(readOnlyOnceLocked, Is.True);
                Assert.That(typeEditableOnceLocked, Is.False);
                Assert.That(typeEditableFromOutgoing, Is.False);
            });
        }
        finally
        {
            TimelineTestHost.Close(view, window);
        }

        static PropertyEditorViewModel EditorOf(EditViewModel editor)
            => TransitionTabOf(editor).Properties.Value!.Properties
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
            var top = new Element { Start = { Value = MediaTime.FromSeconds(0.5) }, Length = { Value = MediaTime.FromSeconds(1) }, ZIndex = { Value = 0 } };
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
                Assert.That(top.EnterTransition.Value, Is.TypeOf<FadeTransition>());
                Assert.That(outgoing.Model.EnterTransition.Value, Is.Null);
                Assert.That(outgoing.Model.ExitTransition.Value, Is.Null);
                Assert.That(over.EnterTransition.Value, Is.Null);
            });
        }
        finally
        {
            TimelineTestHost.Close(view, window);
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
            string name = incoming.Model.Name.Value;

            Point edge = border.TranslatePoint(new Point(3, border.Bounds.Height / 2), window)!.Value;
            await DoubleClick(window, edge);

            Assert.Multiple(() =>
            {
                Assert.That(incoming.Model.EnterTransition.Value, Is.TypeOf<CrossDissolveTransition>());
                Assert.That(incoming.Model.EnterTransition.Value?.Duration.Value, Is.EqualTo(MediaTime.FromSeconds(0.25)));
                Assert.That(outgoing.Model.ExitTransition.Value?.Duration.Value, Is.EqualTo(MediaTime.FromSeconds(0.25)));
                Assert.That(incoming.Model.Name.Value, Is.EqualTo(name));
            });

            await DoubleClick(window, edge);
            TransitionTabViewModel? tab = editor.FindToolTab<TransitionTabViewModel>();
            Assert.Multiple(() =>
            {
                Assert.That(tab?.Transition.Value, Is.SameAs(incoming.Model.EnterTransition.Value));
                Assert.That(tab?.Properties.Value?.Properties,
                    Has.Some.TypeOf<EasingEditorViewModel<Easing>>());
            });
        }
        finally
        {
            TimelineTestHost.Close(view, window);
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
                Assert.That(incoming.Model.ExitTransition.Value, Is.Null);
                Assert.That(element.FindControl<TextBox>("textBox")!.IsVisible, Is.True);
            });
        }
        finally
        {
            TimelineTestHost.Close(view, window);
        }
    }

    // The transition tool, which the timeline opens on a transition.
    private static TransitionTabViewModel TransitionTabOf(EditViewModel editor)
    {
        TransitionTabViewModel? tab = editor.FindToolTab<TransitionTabViewModel>();
        Assert.That(tab, Is.Not.Null);
        return tab!;
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
        (EditViewModel editor, TimelineTabViewModel timeline) = await TimelineTestHost.OpenTimelineAsync(
            $"element-transition-{Guid.NewGuid():N}",
            [
                new(MediaTime.Zero, MediaTime.FromSeconds(2), 0, new ElementSource.EngineObject(() => new RectShape())),
                new(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3), 0, new ElementSource.EngineObject(() => new RectShape())),
            ]);
        Element outgoing = editor.Scene.Children.Single(model => model.Start.Value == MediaTime.Zero);
        Element incoming = editor.Scene.Children.Single(model => model.Start.Value == MediaTime.FromSeconds(2));
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
}
