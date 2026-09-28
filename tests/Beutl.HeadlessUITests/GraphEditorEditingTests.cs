using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.GraphEditorTab.Views;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Language;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using Moq;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using GraphScope = Beutl.HeadlessUITests.GraphEditorContextMenuTests.GraphScope;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class GraphEditorEditingTests
{
    [Test]
    public void Speed_graph_uses_units_per_second_and_ease_derivative()
    {
        var ease = new SplineEasing(1f / 3, 0, 2f / 3, 1);
        Assert.Multiple(() =>
        {
            Assert.That(GraphEditorCurveMath.Velocity(new LinearEasing(), 0.5, 100, 2), Is.EqualTo(50));
            Assert.That(GraphEditorCurveMath.Velocity(ease, 0, 100, 2), Is.Zero);
            Assert.That(GraphEditorCurveMath.Velocity(ease, 0.5, 100, 2), Is.EqualTo(75).Within(0.001));
            Assert.That(GraphEditorCurveMath.Velocity(ease, 1, 100, 2), Is.Zero);
            Assert.That(GraphEditorCurveMath.Velocity(ease, 0.5, -100, 2), Is.EqualTo(-75).Within(0.001));
            Assert.That(GraphEditorCurveMath.Velocity(ease, 0.5, 0, 2), Is.Zero);
            Assert.That(GraphEditorCurveMath.Velocity(ease, 0.5, 100, 0), Is.Zero);
            Assert.That(GraphEditorCurveMath.Velocity(new HoldEasing(), 1, 100, 2), Is.Zero);
        });
    }

    [AvaloniaTest]
    [TestCase("active")]
    [TestCase("switch")]
    [TestCase("switch-back")]
    [TestCase("detach")]
    [TestCase("reattach")]
    [TestCase("close")]
    [TestCase("dispose")]
    [TestCase("channel")]
    [TestCase("channel-back")]
    [TestCase("value")]
    [TestCase("value-back")]
    [TestCase("time")]
    [TestCase("easing")]
    [TestCase("tangent")]
    [TestCase("selection")]
    [TestCase("selection-back")]
    [TestCase("failure")]
    public async Task Pending_cut_only_deletes_from_the_original_active_graph(string change)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        var otherAnimation = new KeyFrameAnimation<float>();
        otherAnimation.KeyFrames.Add(new KeyFrame<float> { Value = 50 });
        using var otherModel = new GraphEditorViewModel<float>(graph.Model.EditorContext, otherAnimation, graph.Model.Element);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clipboard = new Mock<IClipboard>();
        IAsyncDataTransfer? data = null;
        clipboard.Setup(x => x.SetDataAsync(It.IsAny<IAsyncDataTransfer?>()))
            .Callback<IAsyncDataTransfer?>(value => data = value).Returns(completed.Task);
        int undo = graph.Model.HistoryManager.UndoCount;
        try
        {
            Task cut = graph.View.CutSelectionAsync(clipboard.Object);
            Assert.That(cut.IsCompleted, Is.False);
            Assert.That(data, Is.Not.Null);
            Assert.That(graph.Animation.KeyFrames, Has.Count.EqualTo(2));
            switch (change)
            {
                case "switch": graph.View.DataContext = otherModel; break;
                case "switch-back":
                    graph.View.DataContext = otherModel;
                    graph.View.DataContext = graph.Model;
                    break;
                case "detach": graph.Window.Content = null; break;
                case "reattach":
                    graph.Window.Content = null;
                    graph.Window.Content = graph.View;
                    break;
                case "close": graph.Window.Close(); break;
                case "dispose": graph.Model.Dispose(); break;
                case "channel": graph.Model.SelectedView.Value = null; break;
                case "channel-back":
                    var channel = graph.Model.SelectedView.Value;
                    graph.Model.SelectedView.Value = null;
                    graph.Model.SelectedView.Value = channel;
                    break;
                case "value": graph.First.Value = 120; break;
                case "value-back": graph.First.Value = 120; graph.First.Value = 100; break;
                case "time": graph.First.KeyTime = TimeSpan.FromSeconds(0.7); break;
                case "easing": graph.Second.Easing = new CubicEaseIn(); break;
                case "tangent": ((SplineEasing)graph.Second.Easing).Y1 = 0.1f; break;
                case "selection": graph.Model.SelectedView.Value!.SetSelection([graph.First]); break;
                case "selection-back":
                    graph.Model.SelectedView.Value!.SetSelection([]);
                    graph.Model.SelectedView.Value.SetSelection([graph.First, graph.Second]);
                    break;
            }
            string beforeCompletion = CoreSerializer.SerializeToJsonObject(graph.Animation).ToJsonString();
            if (change == "failure") completed.SetException(new IOException("Clipboard write failed."));
            else completed.SetResult();
            await cut;
            Assert.That(otherAnimation.KeyFrames, Has.Count.EqualTo(1));
            Assert.That(graph.Animation.KeyFrames.Count, Is.EqualTo(change == "active" ? 0 : 2));
            Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo + (change == "active" ? 1 : 0)));
            if (change != "active")
                Assert.That(CoreSerializer.SerializeToJsonObject(graph.Animation).ToJsonString(), Is.EqualTo(beforeCompletion));
            if (change == "active")
            {
                graph.Model.HistoryManager.Undo();
                Assert.That(graph.Animation.KeyFrames, Is.EqualTo(new[] { graph.First, graph.Second }));
            }
        }
        finally { data?.Dispose(); }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Pending_cut_cancels_when_switching_color_channels_and_back(bool navigate)
    {
        using var graph = await GraphScope.CreateAsync();
        var animation = new KeyFrameAnimation<Beutl.Media.Color>();
        animation.KeyFrames.Add(new KeyFrame<Beutl.Media.Color> { Value = Beutl.Media.Color.FromArgb(255, 10, 20, 30) });
        animation.KeyFrames.Add(new KeyFrame<Beutl.Media.Color> { KeyTime = TimeSpan.FromSeconds(1), Value = Beutl.Media.Color.FromArgb(255, 30, 40, 50) });
        var shape = graph.Model.Element!.Objects.OfType<Beutl.Graphics.Shapes.RectShape>().Single();
        var brush = new Beutl.Media.SolidColorBrush();
        brush.Color.Animation = animation;
        shape.Fill.CurrentValue = brush;
        graph.Model.HistoryManager.Commit();
        using var model = new GraphEditorViewModel<Beutl.Media.Color>(graph.Model.EditorContext, animation, graph.Model.Element);
        graph.View.DataContext = model;
        var firstChannel = model.Views[0];
        firstChannel.SetSelection(animation.KeyFrames);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clipboard = new Mock<IClipboard>();
        IAsyncDataTransfer? data = null;
        clipboard.Setup(x => x.SetDataAsync(It.IsAny<IAsyncDataTransfer?>()))
            .Callback<IAsyncDataTransfer?>(value => data = value).Returns(completed.Task);
        int undo = model.HistoryManager.UndoCount;
        try
        {
            Task cut = graph.View.CutSelectionAsync(clipboard.Object);
            Assert.That(cut.IsCompleted, Is.False);
            if (navigate)
            {
                model.SelectedView.Value = model.Views[1];
                model.SelectedView.Value = firstChannel;
            }
            completed.SetResult();
            await cut;
            Assert.That(animation.KeyFrames.Count, Is.EqualTo(navigate ? 2 : 0));
            Assert.That(model.HistoryManager.UndoCount, Is.EqualTo(undo + (navigate ? 0 : 1)));
        }
        finally { data?.Dispose(); graph.View.DataContext = null; }
    }

    [AvaloniaTest]
    [TestCase(KeyModifiers.Control)]
    [TestCase(KeyModifiers.Meta)]
    public async Task Cut_keyboard_shortcut_copies_and_deletes_with_one_undo(KeyModifiers command)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        var hotkeys = Application.Current!.PlatformSettings!.HotkeyConfiguration;
        KeyModifiers previous = hotkeys.CommandModifiers;
        hotkeys.CommandModifiers = command;
        try
        {
            graph.View.Focus();
            int undo = graph.Model.HistoryManager.UndoCount;
            var modifier = command == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control;
            graph.Window.KeyPress(Key.X, modifier, PhysicalKey.X, null);
            graph.Window.KeyRelease(Key.X, modifier, PhysicalKey.X, null);
            for (int i = 0; i < 20 && graph.Animation.KeyFrames.Count != 0; i++)
            {
                await Task.Yield();
                HeadlessTestHelpers.Render();
            }
            Assert.That(await graph.Window.Clipboard!.TryGetValueAsync(BeutlDataFormats.KeyFrameAnimation), Is.Not.Null);
            Assert.That(graph.Animation.KeyFrames, Is.Empty);
            Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
            graph.Model.HistoryManager.Undo();
            Assert.That(graph.Animation.KeyFrames, Has.Count.EqualTo(2));
        }
        finally { hotkeys.CommandModifiers = previous; }
    }

    [AvaloniaTest]
    public async Task Selected_clipboard_paste_keeps_spacing_and_existing_keys_and_undo()
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        Assert.That(await graph.Model.CopySelectionAsync(graph.Window.Clipboard), Is.True);
        graph.Model.CurrentTime.Value = TimeSpan.FromSeconds(3);
        int undo = graph.Model.HistoryManager.UndoCount;
        await graph.Model.PasteSelectionAsync(graph.Window.Clipboard);
        HeadlessTestHelpers.Render();
        Assert.That(graph.Animation.KeyFrames.Select(x => x.KeyTime.TotalSeconds), Is.EqualTo(new[] { 0.5, 1.5, 3, 4 }));
        Assert.That(graph.Animation.KeyFrames.Select(x => x.Id).Distinct().Count(), Is.EqualTo(4));
        Assert.That(graph.Animation.KeyFrames[2].Value, Is.EqualTo(100f));
        Assert.That(graph.Animation.KeyFrames[3].Value, Is.EqualTo(500f));
        Assert.That(graph.Model.SelectedView.Value!.KeyFrames.Count(x => x.IsSelected.Value), Is.EqualTo(2));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        graph.Model.HistoryManager.Undo();
        Assert.That(graph.Animation.KeyFrames, Has.Count.EqualTo(2));
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Keyframe_menu_paste_targets_the_clicked_key_and_preserves_cross_type_values(bool crossType, bool animationFormat)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        IKeyFrame source = crossType ? new KeyFrame<int> { Value = 42 } : new KeyFrame<float> { Value = 42 };
        source.KeyTime = TimeSpan.FromSeconds(8);
        source.Easing = new SplineEasing(0.2f, 0.1f, 0.6f, 0.9f);
        ICoreSerializable payload = source;
        if (animationFormat)
        {
            KeyFrameAnimation animation = crossType ? new KeyFrameAnimation<int>() : new KeyFrameAnimation<float>();
            animation.KeyFrames.Add(source);
            payload = animation;
        }
        ObjectRegenerator.Regenerate(payload, out string json);
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(animationFormat ? BeutlDataFormats.KeyFrameAnimation : BeutlDataFormats.KeyFrame, json));
        await graph.Window.Clipboard!.SetDataAsync(data);
        graph.Model.CurrentTime.Value = TimeSpan.FromSeconds(0.5);
        var original = graph.Second.Easing;
        int undo = graph.Model.HistoryManager.UndoCount;
        var key = graph.KeyFrame(graph.Second);
        graph.RightClick(key.TranslatePoint(default, graph.Window)!.Value);
        var menuItem = key.ContextMenu!.Items.OfType<MenuItem>().Single(x => Equals(x.Header, Strings.Paste));
        var keyModel = (GraphEditorKeyFrameViewModel)key.DataContext!;
        Assert.That(menuItem.Command, Is.SameAs(keyModel.PasteCommand));
        await keyModel.PasteAsync(graph.Window.Clipboard);
        Assert.That(graph.Animation.KeyFrames, Has.Count.EqualTo(2));
        Assert.That(graph.Second.KeyTime, Is.EqualTo(TimeSpan.FromSeconds(1.5)));
        Assert.That(graph.Second.Value, Is.EqualTo(crossType ? 500f : 42f));
        Assert.That(graph.First.Value, Is.EqualTo(100f));
        Assert.That(graph.Second.Easing.Ease(0.4f), Is.EqualTo(source.Easing.Ease(0.4f)).Within(0.0001));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        graph.Model.HistoryManager.Undo();
        Assert.That(graph.Second.Value, Is.EqualTo(500f));
        Assert.That(graph.Second.Easing, Is.SameAs(original));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Background_paste_keeps_pointer_position_and_full_animation_replacement(bool fullAnimation)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        var source = new KeyFrame<float> { Value = 42, KeyTime = TimeSpan.FromSeconds(8) };
        ICoreSerializable payload = source;
        if (fullAnimation)
        {
            var animation = new KeyFrameAnimation<float>();
            animation.KeyFrames.Add(source);
            payload = animation;
        }
        ObjectRegenerator.Regenerate(payload, out string json);
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(fullAnimation ? BeutlDataFormats.KeyFrameAnimation : BeutlDataFormats.KeyFrame, json));
        await graph.Window.Clipboard!.SetDataAsync(data);
        graph.Model.CurrentTime.Value = TimeSpan.Zero;
        await graph.Model.PasteKeyFrameAtPositionAsync(TimeSpan.FromSeconds(3), graph.Window.Clipboard);
        Assert.That(graph.Animation.KeyFrames.Select(x => x.KeyTime.TotalSeconds),
            Is.EqualTo(fullAnimation ? new[] { 8d } : new[] { 0.5, 1.5, 3 }));
        Assert.That(graph.Animation.KeyFrames[^1].Value, Is.EqualTo(42f));
    }

    [AvaloniaTest]
    public async Task Delete_undo_restores_the_selected_keys()
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        var channel = graph.Model.SelectedView.Value!;
        graph.View.Focus();
        graph.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.None, null);
        graph.Window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.That(graph.Animation.KeyFrames, Is.Empty);
        Assert.That(channel.SelectionCount.Value, Is.Zero);
        graph.Model.HistoryManager.Undo();
        Assert.That(channel.KeyFrames.Where(x => x.IsSelected.Value).Select(x => x.Model), Is.EqualTo(new[] { graph.First, graph.Second }));
        graph.Model.HistoryManager.Redo();
        Assert.That(channel.KeyFrames, Is.Empty);
        Assert.That(channel.SelectionCount.Value, Is.Zero);
    }

    [AvaloniaTest]
    public async Task Speed_curve_is_retained_during_selection_redraws_and_rebuilt_after_edits()
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        graph.Model.IsSpeedGraph.Value = true;
        HeadlessTestHelpers.Render(3);
        var speed = graph.View.FindControl<GraphEditorSpeedGraph>("SpeedGraph")!;
        var channel = graph.Model.SelectedView.Value!;
        var first = channel.KeyFrames[0];
        var second = channel.KeyFrames[1];
        var geometry = speed.GetCurveGeometry(first, second);
        channel.SetSelection([graph.Second]);
        HeadlessTestHelpers.Render(3);
        Assert.That(speed.GetCurveGeometry(first, second), Is.SameAs(geometry));
        foreach (Action edit in new Action[]
        {
            () => graph.Second.Value += 50,
            () => graph.First.KeyTime += TimeSpan.FromSeconds(0.1),
            () => ((SplineEasing)graph.Second.Easing).Y1 = -0.3f,
            () => ((SplineEasing)graph.Second.Easing).X2 = 0.9f,
            () => graph.Model.ScaleY.Value *= 1.2,
            () => graph.Model.Options.Value = graph.Model.Options.Value with { Scale = 1.2f },
            () => graph.Second.Easing = new LinearEasing()
        })
        {
            edit();
            HeadlessTestHelpers.Render(3);
            var updated = speed.GetCurveGeometry(first, second);
            Assert.That(updated, Is.Not.SameAs(geometry));
            Assert.That(speed.GetCurveGeometry(first, second), Is.SameAs(updated));
            geometry = updated;
        }
        graph.Capture("speed-cached-after-value-edit");
    }

    [AvaloniaTest]
    public async Task Only_selected_keys_show_handles_and_escape_reverts_handle_edit()
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true, selectAll: false);
        Assert.That(graph.Handle("ControlPoint1").IsVisible, Is.False);
        Assert.That(graph.Handle("ControlPoint2").IsVisible, Is.False);
        graph.Model.SelectedView.Value!.SetSelection([graph.Second]);
        HeadlessTestHelpers.Render();
        Assert.That(graph.Handle("ControlPoint1").IsVisible, Is.False);
        Assert.That(graph.Handle("ControlPoint2").IsVisible, Is.True);
        Assert.That(graph.KeyFrame(graph.Second).Fill, Is.Not.SameAs(graph.KeyFrame(graph.First).Fill));
        Assert.That(((ISolidColorBrush)graph.KeyFrame(graph.Second).Fill!).Color,
            Is.EqualTo(((ISolidColorBrush)graph.KeyFrame(graph.Second).Stroke!).Color));
        var spline = (SplineEasing)graph.Second.Easing;
        float x = spline.X2, y = spline.Y2;
        int undo = graph.Model.HistoryManager.UndoCount;
        Point point = graph.Handle("ControlPoint2").TranslatePoint(default, graph.Window)!.Value;
        graph.HitTest(point);
        graph.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.Alt);
        graph.Window.MouseMove(point + new Vector(-20, -20), RawInputModifiers.Alt | RawInputModifiers.LeftMouseButton);
        graph.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        graph.Window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        graph.Window.MouseUp(point + new Vector(-20, -20), MouseButton.Left);
        Assert.That(spline.X2, Is.EqualTo(x));
        Assert.That(spline.Y2, Is.EqualTo(y));
        Assert.That(graph.View.ControlPointMoveState, Is.Null);
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo));
    }

    [AvaloniaTest]
    public async Task Auto_height_tracks_values_and_resizing_and_blocks_vertical_navigation()
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        graph.Model.AutoZoomHeight.Value = true;
        HeadlessTestHelpers.Render();
        double previous = graph.Model.ScaleY.Value;
        graph.Second.Value = 1500;
        HeadlessTestHelpers.Render(3);
        Assert.That(graph.Model.ScaleY.Value, Is.LessThan(previous));
        previous = graph.Model.ScaleY.Value;
        graph.Window.Height = 600;
        HeadlessTestHelpers.Render(3);
        Assert.That(graph.Model.ScaleY.Value, Is.GreaterThan(previous));
        var scroll = graph.View.FindControl<ScrollViewer>("scroll")!;
        Point point = scroll.TranslatePoint(new Point(150, 150), graph.Window)!.Value;
        var offset = scroll.Offset;
        graph.HitTest(point);
        graph.Window.MouseWheel(point, new Vector(0, -1));
        HeadlessTestHelpers.Render();
        Assert.That(scroll.Offset.Y, Is.EqualTo(offset.Y));
        graph.Second.Value = graph.First.Value;
        graph.Model.IsSpeedGraph.Value = true;
        HeadlessTestHelpers.Render(3);
        Assert.That(double.IsFinite(graph.Model.ScaleY.Value), Is.True);
        Assert.That(double.IsFinite(graph.Model.Baseline.Value), Is.True);
    }

    [AvaloniaTest]
    [TestCase(360, false)]
    [TestCase(360, true)]
    [TestCase(960, false)]
    [TestCase(960, true)]
    public async Task Graph_toolbar_keeps_every_action_reachable_at_small_and_large_widths(int width, bool light)
    {
        using var graph = await GraphScope.CreateAsync(light, separateHandles: true);
        graph.Window.Width = width;
        HeadlessTestHelpers.Render(3);
        var toolbar = graph.View.FindControl<WrapPanel>("GraphToolbar")!;
        var actions = toolbar.GetVisualDescendants().OfType<Button>().Where(button => button.Tag is string).ToArray();
        Assert.That(actions.Length, Is.GreaterThanOrEqualTo(10));
        foreach (var button in actions)
        {
            var bounds = new Rect(button.TranslatePoint(default, graph.Window)!.Value, button.Bounds.Size);
            Assert.That(bounds.Right, Is.LessThanOrEqualTo(graph.Window.Bounds.Width + 1));
            Assert.That(bounds.Bottom, Is.LessThanOrEqualTo(graph.Window.Bounds.Height + 1));
            Assert.That(button.Bounds.Width, Is.GreaterThan(15));
        }
        graph.Capture($"toolbar-{width}-{light}");
    }
    [AvaloniaTest]
    [TestCase(640, false, "Button")]
    [TestCase(360, true, "Button")]
    [TestCase(640, true, "Keyboard")]
    [TestCase(360, false, "ContextMenu")]
    public async Task Velocity_picker_flyout_applies_both_influences_as_one_undo(int width, bool light, string source)
    {
        using var graph = await GraphScope.CreateAsync(light, separateHandles: true);
        graph.Window.Width = width;
        HeadlessTestHelpers.Render(3);
        int undo = graph.Model.HistoryManager.UndoCount;
        var original = graph.Second.Easing;
        var flyout = OpenVelocityFlyout(graph, source);
        Assert.That(flyout.Popup.PlacementTarget, Is.SameAs(graph.View.FindControl<Button>("VelocityButton")));
        var presenter = (DraggablePickerFlyoutPresenter)flyout.Popup.Child!;
        var numbers = presenter.GetVisualDescendants().OfType<NumericUpDown>().ToDictionary(x => x.Name!);
        Assert.That(numbers, Has.Count.EqualTo(4));
        Assert.That(presenter.Bounds.Width, Is.LessThanOrEqualTo(width));
        foreach (var number in numbers.Values)
        {
            var bounds = new Rect(number.TranslatePoint(default, presenter)!.Value, number.Bounds.Size);
            Assert.That(bounds.Width, Is.GreaterThan(100));
            Assert.That(bounds.Left, Is.GreaterThanOrEqualTo(0));
            Assert.That(bounds.Right, Is.LessThanOrEqualTo(presenter.Bounds.Width));
        }
        numbers["IncomingSpeed"].Value = 0;
        numbers["IncomingInfluence"].Value = 45;
        numbers["OutgoingSpeed"].Value = 0;
        numbers["OutgoingInfluence"].Value = 45;
        HeadlessTestHelpers.Render(3);
        CaptureVelocityFlyout(graph, presenter, $"velocity-flyout-{width}-{light}-{source}");
        var preview = (SplineEasing)graph.Second.Easing;
        Assert.That(preview.X1, Is.EqualTo(0.45f).Within(0.001), "Editing the picker must update the curve immediately.");
        Assert.That(preview.X2, Is.EqualTo(0.55f).Within(0.001));
        Assert.That(preview.Y1, Is.Zero);
        Assert.That(preview.Y2, Is.EqualTo(1));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo));
        Assert.That(graph.Model.HistoryManager.HasPendingOperations, Is.False, "A preview must not leak into an unrelated history commit.");
        PickerButton(flyout, "AcceptButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(flyout.IsOpen, Is.False);
        var easing = (SplineEasing)graph.Second.Easing;
        Assert.That(easing.X1, Is.EqualTo(0.45f).Within(0.001));
        Assert.That(easing.X2, Is.EqualTo(0.55f).Within(0.001));
        Assert.That(easing.Y1, Is.Zero);
        Assert.That(easing.Y2, Is.EqualTo(1));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        graph.Model.HistoryManager.Undo();
        Assert.That(graph.Second.Easing, Is.SameAs(original));
        Assert.That(((SplineEasing)graph.Second.Easing).X1, Is.EqualTo(0.25f));
        Assert.That(((SplineEasing)graph.Second.Easing).Y1, Is.EqualTo(0.25f));
        graph.Model.HistoryManager.Redo();
        Assert.That(((SplineEasing)graph.Second.Easing).X1, Is.EqualTo(0.45f).Within(0.001));
    }

    [AvaloniaTest]
    [TestCase("DismissButton")]
    [TestCase("CloseButton")]
    [TestCase("Escape")]
    [TestCase("Outside")]
    [TestCase("Detach")]
    public async Task Velocity_picker_dismissal_discards_edits(string dismiss)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        int undo = graph.Model.HistoryManager.UndoCount;
        var original = graph.Second.Easing;
        var flyout = OpenVelocityFlyout(graph, "Button");
        var presenter = (DraggablePickerFlyoutPresenter)flyout.Popup.Child!;
        foreach (var number in presenter.GetVisualDescendants().OfType<NumericUpDown>())
            number.Value = 45;
        Assert.That(graph.Second.Easing, Is.Not.SameAs(original));
        Assert.That(((SplineEasing)graph.Second.Easing).X1, Is.EqualTo(0.45f).Within(0.001));
        Assert.That(graph.Model.HistoryManager.HasPendingOperations, Is.False);
        switch (dismiss)
        {
            case "DismissButton":
            case "CloseButton":
                Assert.That(PickerButton(flyout, dismiss).IsEffectivelyVisible, Is.True);
                PickerButton(flyout, dismiss).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                break;
            case "Escape":
                graph.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                graph.Window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                break;
            case "Outside":
                graph.Window.MouseDown(new Point(10, 100), MouseButton.Left);
                graph.Window.MouseUp(new Point(10, 100), MouseButton.Left);
                break;
            case "Detach":
                graph.View.DataContext = null;
                break;
        }
        HeadlessTestHelpers.Render(3);
        Assert.That(flyout.IsOpen, Is.False);
        Assert.That(graph.View.VelocityFlyout, Is.Null);
        var easing = (SplineEasing)graph.Second.Easing;
        Assert.That(easing, Is.SameAs(original));
        Assert.That((easing.X1, easing.Y1, easing.X2, easing.Y2), Is.EqualTo((0.25f, 0.25f, 0.75f, 0.75f)));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo));
        Assert.That(graph.Model.SelectedView.Value!.KeyFrames.Count(x => x.IsSelected.Value), Is.EqualTo(2));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Velocity_picker_header_drag_moves_the_flyout_and_release_ends_the_drag(bool light)
    {
        using var graph = await GraphScope.CreateAsync(light, separateHandles: true);
        var flyout = OpenVelocityFlyout(graph, "Button");
        var header = flyout.Popup.Child!.GetVisualDescendants().OfType<Panel>().Single(x => x.Name == "DragArea");
        Point from = header.TranslatePoint(new Point(100, 20), graph.Window)!.Value;
        double x = flyout.Popup.HorizontalOffset, y = flyout.Popup.VerticalOffset;
        graph.HitTest(from);
        graph.Window.MouseDown(from, MouseButton.Left);
        foreach (var delta in new[] { new Vector(12, -12), new Vector(30, -55) })
        {
            graph.Window.MouseMove(from + delta, RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Render(3);
            Assert.That(flyout.Popup.HorizontalOffset, Is.EqualTo(x + delta.X).Within(0.01));
            Assert.That(flyout.Popup.VerticalOffset, Is.EqualTo(y + delta.Y).Within(0.01));
            Assert.That(flyout.IsOpen, Is.True);
        }
        graph.Window.MouseUp(from + new Vector(30, -55), MouseButton.Left);
        graph.Window.MouseMove(header.TranslatePoint(new Point(130, 20), graph.Window)!.Value);
        Assert.That(flyout.Popup.HorizontalOffset, Is.EqualTo(x + 30).Within(0.01));
        Assert.That(flyout.Popup.VerticalOffset, Is.EqualTo(y - 55).Within(0.01));
        CaptureVelocityFlyout(graph, flyout.Popup.Child!, $"velocity-flyout-dragged-{light}");
        flyout.Hide();
    }

    [AvaloniaTest]
    public async Task Velocity_picker_cancel_restores_non_spline_easing_and_notifies_the_scene()
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        var original = graph.Second.Easing = new LinearEasing();
        graph.Model.HistoryManager.Commit();
        int edits = 0;
        EventHandler edited = (_, _) => edits++;
        graph.Model.Scene.Edited += edited;
        try
        {
            var flyout = OpenVelocityFlyout(graph, "Button");
            flyout.Popup.Child!.GetVisualDescendants().OfType<NumericUpDown>().Single(x => x.Name == "IncomingSpeed").Value = 0;
            Assert.That(graph.Second.Easing, Is.TypeOf<SplineEasing>());
            Assert.That(edits, Is.GreaterThan(0), "Preview edits must notify the renderer without a history commit.");
            int previews = edits;
            flyout.Hide();
            Assert.That(graph.Second.Easing, Is.SameAs(original));
            Assert.That(edits, Is.GreaterThan(previews), "Cancel must redraw the original easing too.");
        }
        finally { graph.Model.Scene.Edited -= edited; }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Velocity_picker_closes_without_history_when_nothing_changed(bool editAndRevert)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        var original = graph.Second.Easing;
        int undo = graph.Model.HistoryManager.UndoCount;
        var flyout = OpenVelocityFlyout(graph, "Button");
        if (editAndRevert)
        {
            var number = flyout.Popup.Child!.GetVisualDescendants().OfType<NumericUpDown>().Single(x => x.Name == "IncomingInfluence");
            decimal? value = number.Value;
            number.Value = 45;
            number.Value = value;
        }
        PickerButton(flyout, "AcceptButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(graph.Second.Easing, Is.SameAs(original));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo));
        Assert.That(graph.Model.HistoryManager.HasPendingOperations, Is.False);
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Velocity_picker_does_not_discard_or_combine_an_earlier_pending_edit(bool confirm)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        int undo = graph.Model.HistoryManager.UndoCount;
        graph.First.Value = 120;
        var original = graph.Second.Easing;
        var flyout = OpenVelocityFlyout(graph, "Button");
        flyout.Popup.Child!.GetVisualDescendants().OfType<NumericUpDown>().Single(x => x.Name == "IncomingSpeed").Value = 0;
        if (confirm) PickerButton(flyout, "AcceptButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        else flyout.Hide();
        Assert.That(graph.First.Value, Is.EqualTo(120));
        if (confirm)
        {
            Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 2));
            graph.Model.HistoryManager.Undo();
            Assert.That(graph.First.Value, Is.EqualTo(120));
            Assert.That(graph.Second.Easing, Is.SameAs(original));
        }
        else
        {
            Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo));
            Assert.That(graph.Model.HistoryManager.HasPendingOperations, Is.True);
            Assert.That(graph.Second.Easing, Is.SameAs(original));
            graph.Model.HistoryManager.Commit();
        }
        graph.Model.HistoryManager.Undo();
        Assert.That(graph.First.Value, Is.EqualTo(100));
    }

    [AvaloniaTest]
    public async Task Undo_closes_the_velocity_preview_before_reverting_the_previous_edit()
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        graph.First.Value = 120;
        graph.Model.HistoryManager.Commit();
        var original = graph.Second.Easing;
        var flyout = OpenVelocityFlyout(graph, "Button");
        flyout.Popup.Child!.GetVisualDescendants().OfType<NumericUpDown>().Single(x => x.Name == "IncomingSpeed").Value = 0;
        graph.Model.HistoryManager.Undo();
        Assert.That(flyout.IsOpen, Is.False);
        Assert.That(graph.First.Value, Is.EqualTo(100));
        Assert.That(graph.Second.Easing, Is.SameAs(original));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Velocity_picker_disables_the_missing_side_at_endpoints(bool first)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        graph.Model.SelectedView.Value!.SetSelection([first ? graph.First : graph.Second]);
        var flyout = OpenVelocityFlyout(graph, "Button");
        var numbers = flyout.Popup.Child!.GetVisualDescendants().OfType<NumericUpDown>().ToDictionary(x => x.Name!);
        Assert.That(numbers["IncomingSpeed"].IsEffectivelyEnabled, Is.EqualTo(!first));
        Assert.That(numbers["IncomingInfluence"].IsEffectivelyEnabled, Is.EqualTo(!first));
        Assert.That(numbers["OutgoingSpeed"].IsEffectivelyEnabled, Is.EqualTo(first));
        Assert.That(numbers["OutgoingInfluence"].IsEffectivelyEnabled, Is.EqualTo(first));
        flyout.Hide();
    }

    private static KeyFrameVelocityFlyout OpenVelocityFlyout(GraphScope graph, string source)
    {
        if (source == "Keyboard")
        {
            graph.View.Focus();
            RawInputModifiers command = KeyGestureHelper.GetCommandModifier() == KeyModifiers.Meta
                ? RawInputModifiers.Meta : RawInputModifiers.Control;
            graph.Window.KeyPress(Key.K, command | RawInputModifiers.Shift, PhysicalKey.None, "k");
            graph.Window.KeyRelease(Key.K, command | RawInputModifiers.Shift, PhysicalKey.None, "k");
        }
        else if (source == "ContextMenu")
        {
            graph.BackgroundMenu.Open(graph.View.FindControl<Panel>("graphPanel")!);
            HeadlessTestHelpers.Render();
            graph.BackgroundMenu.Items.OfType<MenuItem>().Single(x => Equals(x.Tag, "Velocity"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            graph.BackgroundMenu.Close();
        }
        else graph.View.FindControl<Button>("VelocityButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        HeadlessTestHelpers.Render(3);
        Assert.That(graph.View.VelocityFlyout, Is.Not.Null);
        Assert.That(graph.View.VelocityFlyout!.IsOpen, Is.True);
        Assert.That(graph.View.VelocityFlyout.Popup.Child, Is.TypeOf<DraggablePickerFlyoutPresenter>());
        Assert.That(graph.Window.GetVisualDescendants().OfType<FAContentDialog>(), Is.Empty);
        return graph.View.VelocityFlyout;
    }

    private static Button PickerButton(KeyFrameVelocityFlyout flyout, string name) => flyout.Popup.Child!
        .GetVisualDescendants().OfType<Button>().Single(x => x.Name == name);

    private static void CaptureVelocityFlyout(GraphScope graph, Control presenter, string name)
    {
        graph.Capture(name + "-host");
        if (Environment.GetEnvironmentVariable("BEUTL_GRAPH_CONTEXT_CAPTURE") is not { Length: > 0 } directory) return;
        using var frame = TopLevel.GetTopLevel(presenter)!.CaptureRenderedFrame();
        frame?.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    [AvaloniaTest]
    [TestCase(typeof(CustomQuadraticEasing))]
    [TestCase(typeof(CustomSplineEasing))]
    [TestCase(typeof(HoldEasing))]
    public async Task Unsupported_easing_reversal_leaves_the_entire_animation_unchanged(Type easingType)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        var original = (Easing)Activator.CreateInstance(easingType)!;
        graph.Second.Easing = original;
        var third = new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(2.5),
            Value = 200,
            Easing = new SplineEasing(0.25f, 0.2f, 0.75f, 0.8f)
        };
        graph.Animation.KeyFrames.Add(third);
        graph.Model.SelectedView.Value!.SetSelection(graph.Animation.KeyFrames);
        graph.Model.HistoryManager.Commit();
        var snapshot = new GraphEditorDragSnapshot(graph.Model.SelectedView.Value);
        string before = CoreSerializer.SerializeToJsonObject(graph.Animation).ToJsonString();
        int undo = graph.Model.HistoryManager.UndoCount;
        graph.Model.HistoryManager.ExecuteInTransaction(() => snapshot.Apply(
            entry => (3 - entry.Time.TotalSeconds, entry.Number * 2), -1, 2, transformHandles: true));
        Assert.That(CoreSerializer.SerializeToJsonObject(graph.Animation).ToJsonString(), Is.EqualTo(before));
        Assert.That(graph.Second.Easing, Is.SameAs(original));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo));
        Assert.That(graph.Model.HistoryManager.HasPendingOperations, Is.False);
        Assert.That(graph.Model.SelectedView.Value.SelectionCount.Value, Is.EqualTo(3));

        graph.Model.HistoryManager.ExecuteInTransaction(() => snapshot.Apply(entry => (entry.Time.TotalSeconds + 0.2, entry.Number + 10)));
        Assert.That(graph.First.KeyTime.TotalSeconds, Is.EqualTo(0.7).Within(0.00001));
        Assert.That(graph.First.Value, Is.EqualTo(110));
        string moved = CoreSerializer.SerializeToJsonObject(graph.Animation).ToJsonString();
        graph.Model.HistoryManager.ExecuteInTransaction(() => snapshot.Apply(
            entry => (3 - entry.Time.TotalSeconds, entry.Number * 2), -1, 2, transformHandles: true));
        Assert.That(CoreSerializer.SerializeToJsonObject(graph.Animation).ToJsonString(), Is.EqualTo(moved));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        graph.Model.HistoryManager.Undo();
        Assert.That(CoreSerializer.SerializeToJsonObject(graph.Animation).ToJsonString(), Is.EqualTo(before));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Dragging_an_unsupported_easing_past_the_anchor_does_not_commit_an_edit(bool cancel)
    {
        using var graph = await GraphScope.CreateAsync(light: cancel, separateHandles: true);
        graph.Second.Easing = new CustomQuadraticEasing();
        graph.Model.HistoryManager.Commit();
        HeadlessTestHelpers.Render();
        string before = CoreSerializer.SerializeToJsonObject(graph.Animation).ToJsonString();
        Point first = graph.KeyFrame(graph.First).TranslatePoint(default, graph.Window)!.Value;
        Point second = graph.KeyFrame(graph.Second).TranslatePoint(default, graph.Window)!.Value;
        Point start = new(second.X, (first.Y + second.Y) / 2);
        Point end = new(first.X - (second.X - first.X) / 2, start.Y);
        int undo = graph.Model.HistoryManager.UndoCount;
        graph.HitTest(start);
        graph.Window.MouseDown(start, MouseButton.Left);
        graph.Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        Assert.That(CoreSerializer.SerializeToJsonObject(graph.Animation).ToJsonString(), Is.EqualTo(before));
        if (cancel)
        {
            graph.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            graph.Window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        }
        graph.Window.MouseUp(end, MouseButton.Left);
        HeadlessTestHelpers.Render(3);
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo));
        Assert.That(CoreSerializer.SerializeToJsonObject(graph.Animation).ToJsonString(), Is.EqualTo(before));
        graph.Capture($"unsupported-reversal-{cancel}");
    }

    [AvaloniaTest]
    public async Task Unsupported_easing_outside_the_selection_does_not_block_reversal()
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        graph.Second.Easing = new CubicEaseIn();
        var third = new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(2.5), Value = 200, Easing = new CustomQuadraticEasing() };
        graph.Animation.KeyFrames.Add(third);
        graph.Model.HistoryManager.Commit();
        var original = third.Easing;
        var snapshot = new GraphEditorDragSnapshot(graph.Model.SelectedView.Value!);
        snapshot.Apply(entry => (2 - entry.Time.TotalSeconds, entry.Number), -1, transformHandles: true);
        Assert.That(graph.Animation.KeyFrames[0], Is.SameAs(graph.Second));
        Assert.That(graph.First.Easing, Is.TypeOf<CubicEaseOut>());
        Assert.That(third.Easing, Is.SameAs(original));
        Assert.That(third.KeyTime.TotalSeconds, Is.EqualTo(2.5));
    }

    public sealed class CustomQuadraticEasing : Easing
    {
        public override float Ease(float progress) => progress * progress;
    }

    public sealed class CustomSplineEasing() : SplineEasing(0.25f, 0.2f, 0.75f, 0.8f)
    {
        public override float Ease(float progress) => progress * progress;
    }

    [AvaloniaTest]
    [TestCase(typeof(LinearEasing), typeof(LinearEasing))]
    [TestCase(typeof(QuadraticEaseInOut), typeof(QuadraticEaseInOut))]
    [TestCase(typeof(CubicEaseInOut), typeof(CubicEaseInOut))]
    [TestCase(typeof(QuarticEaseInOut), typeof(QuarticEaseInOut))]
    [TestCase(typeof(QuinticEaseInOut), typeof(QuinticEaseInOut))]
    [TestCase(typeof(SineEaseInOut), typeof(SineEaseInOut))]
    [TestCase(typeof(CircularEaseInOut), typeof(CircularEaseInOut))]
    [TestCase(typeof(ExponentialEaseInOut), typeof(ExponentialEaseInOut))]
    [TestCase(typeof(ElasticEaseInOut), typeof(ElasticEaseInOut))]
    [TestCase(typeof(BackEaseInOut), typeof(BackEaseInOut))]
    [TestCase(typeof(BounceEaseInOut), typeof(BounceEaseInOut))]
    [TestCase(typeof(QuadraticEaseIn), typeof(QuadraticEaseOut))]
    [TestCase(typeof(QuadraticEaseOut), typeof(QuadraticEaseIn))]
    [TestCase(typeof(CubicEaseIn), typeof(CubicEaseOut))]
    [TestCase(typeof(CubicEaseOut), typeof(CubicEaseIn))]
    [TestCase(typeof(QuarticEaseIn), typeof(QuarticEaseOut))]
    [TestCase(typeof(QuarticEaseOut), typeof(QuarticEaseIn))]
    [TestCase(typeof(QuinticEaseIn), typeof(QuinticEaseOut))]
    [TestCase(typeof(QuinticEaseOut), typeof(QuinticEaseIn))]
    [TestCase(typeof(SineEaseIn), typeof(SineEaseOut))]
    [TestCase(typeof(SineEaseOut), typeof(SineEaseIn))]
    [TestCase(typeof(CircularEaseIn), typeof(CircularEaseOut))]
    [TestCase(typeof(CircularEaseOut), typeof(CircularEaseIn))]
    [TestCase(typeof(ExponentialEaseIn), typeof(ExponentialEaseOut))]
    [TestCase(typeof(ExponentialEaseOut), typeof(ExponentialEaseIn))]
    [TestCase(typeof(ElasticEaseIn), typeof(ElasticEaseOut))]
    [TestCase(typeof(ElasticEaseOut), typeof(ElasticEaseIn))]
    [TestCase(typeof(BackEaseIn), typeof(BackEaseOut))]
    [TestCase(typeof(BackEaseOut), typeof(BackEaseIn))]
    [TestCase(typeof(BounceEaseIn), typeof(BounceEaseOut))]
    [TestCase(typeof(BounceEaseOut), typeof(BounceEaseIn))]
    public async Task Reversing_selection_mirrors_directional_easing_and_preserves_undo(Type sourceType, Type reversedType)
    {
        using var graph = await GraphScope.CreateAsync(light: sourceType == typeof(CubicEaseOut), separateHandles: true);
        var original = (Easing)Activator.CreateInstance(sourceType)!;
        var firstEasing = graph.First.Easing;
        graph.Second.Easing = original;
        graph.Model.HistoryManager.Commit();
        HeadlessTestHelpers.Render();
        Point first = graph.KeyFrame(graph.First).TranslatePoint(default, graph.Window)!.Value;
        Point second = graph.KeyFrame(graph.Second).TranslatePoint(default, graph.Window)!.Value;
        Point start = new(second.X, (first.Y + second.Y) / 2);
        Point end = new(first.X - (second.X - first.X) / 2, start.Y);
        int undo = graph.Model.HistoryManager.UndoCount;
        graph.HitTest(start);
        graph.Window.MouseDown(start, MouseButton.Left);
        graph.Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        graph.Window.MouseUp(end, MouseButton.Left);
        HeadlessTestHelpers.Render(3);
        Assert.That(graph.Animation.KeyFrames[0], Is.SameAs(graph.Second));
        Assert.That(graph.First.Easing.GetType(), Is.EqualTo(reversedType));
        for (int i = 0; i <= 100; i++)
            Assert.That(graph.First.Easing.Ease(i / 100f), Is.EqualTo(1 - original.Ease(1 - i / 100f)).Within(0.00001));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        // The reversed result must remain an ordinary, serializable built-in easing.
        ObjectRegenerator.Regenerate(graph.Animation, out KeyFrameAnimation<float> restored);
        Assert.That(restored.KeyFrames[1].Easing.GetType(), Is.EqualTo(reversedType));
        graph.Capture($"reverse-{sourceType.Name}");
        graph.Model.HistoryManager.Undo();
        Assert.That(graph.Second.Easing, Is.SameAs(original));
        Assert.That(graph.First.Easing, Is.SameAs(firstEasing));
        Assert.That(graph.First.KeyTime.TotalSeconds, Is.EqualTo(0.5));
        Assert.That(graph.Second.KeyTime.TotalSeconds, Is.EqualTo(1.5));
        graph.Model.HistoryManager.Redo();
        Assert.That(graph.First.Easing.GetType(), Is.EqualTo(reversedType));
    }

    [AvaloniaTest]
    public async Task Reversing_selection_reverses_bezier_handles_and_key_order()
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        graph.Second.Easing = new SplineEasing(0.25f, 0.1f, 0.8f, 0.9f);
        graph.Model.HistoryManager.Commit();
        var reference = new SplineEasing(0.25f, 0.1f, 0.8f, 0.9f);
        HeadlessTestHelpers.Render();
        Point first = graph.KeyFrame(graph.First).TranslatePoint(default, graph.Window)!.Value;
        Point second = graph.KeyFrame(graph.Second).TranslatePoint(default, graph.Window)!.Value;
        Point start = new(second.X, (first.Y + second.Y) / 2);
        Point end = new(first.X - (second.X - first.X) / 2, start.Y);
        graph.HitTest(start);
        graph.Window.MouseDown(start, MouseButton.Left);
        graph.Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        graph.Window.MouseUp(end, MouseButton.Left);
        Assert.That(graph.Animation.KeyFrames[0], Is.SameAs(graph.Second));
        Assert.That(graph.Animation.KeyFrames[1], Is.SameAs(graph.First));
        Assert.That(graph.First.Easing, Is.TypeOf<SplineEasing>());
        Assert.That(graph.First.Easing.Ease(0.25f), Is.EqualTo(1 - reference.Ease(0.75f)).Within(0.001));
        Assert.That(graph.First.Value, Is.EqualTo(100));
        Assert.That(graph.Second.Value, Is.EqualTo(500));
    }

    [AvaloniaTest]
    public async Task Fit_speed_selection_includes_the_peak_between_zero_velocity_keys()
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        graph.Second.Easing = new SplineEasing(1f / 3, 0, 2f / 3, 1);
        graph.Model.IsSpeedGraph.Value = true;
        HeadlessTestHelpers.Render(3);
        graph.View.Focus();
        graph.Window.KeyPress(Key.F, RawInputModifiers.Shift, PhysicalKey.None, "f");
        graph.Window.KeyRelease(Key.F, RawInputModifiers.Shift, PhysicalKey.None, "f");
        HeadlessTestHelpers.Render(3);
        var scroll = graph.View.FindControl<ScrollViewer>("scroll")!;
        double peakY = graph.Model.Baseline.Value - 600 * graph.Model.ScaleY.Value - scroll.Offset.Y;
        Assert.That(peakY, Is.InRange(0, scroll.Viewport.Height));
        Assert.That(graph.Model.Baseline.Value - scroll.Offset.Y, Is.InRange(0, scroll.Viewport.Height));
    }

}
