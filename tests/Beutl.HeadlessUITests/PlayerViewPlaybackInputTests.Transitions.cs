using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Microsoft.Extensions.DependencyInjection;
using Pointer = Avalonia.Input.Pointer;

namespace Beutl.HeadlessUITests;

public partial class PlayerViewPlaybackInputTests
{
    [AvaloniaTest]
    [TestCase(MouseButton.Left)]
    [TestCase(MouseButton.Right)]
    public async Task Queued_start_and_stop_finish_camera_edit_once(MouseButton button)
    {
        EditViewModel editor = await OpenEditor();
        var scene = await Add3DScene(editor);
        var cube = scene.Objects.Single();
        var camera = scene.Camera.CurrentValue!;
        editor.GetRequiredService<IEditorSelection>().SelectedObject.Value = editor.Scene.Children.Single();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        Point point = default;
        try
        {
            await PreparePreview(view, window);
            editor.Player.IsMoveMode.Value = false;
            editor.Player.IsCameraMode.Value = true;
            point = PreviewCenter(view, window);
            window.MouseDown(point, button);
            window.MouseMove(point + new Vector(12, 8), HeldButton(button));
            if (button == MouseButton.Right)
                window.KeyPress(Key.W, RawInputModifiers.None, PhysicalKey.W, "w");
            object handler = MouseHandler(view)!;
            var position = cube.Position.CurrentValue;
            var target = camera.Target.CurrentValue;
            var cameraPosition = camera.Position.CurrentValue;
            int index = editor.HistoryManager.CurrentIndex;

            PublishPlayingOffUI(editor.Player, stop: true);
            Assert.Multiple(() =>
            {
                Assert.That(editor.Player.IsPlaying.Value, Is.False);
                Assert.That(MouseHandler(view), Is.SameAs(handler), "The queued notifications must still be pending.");
            });
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(MouseHandler(view), Is.Null, "The emitted start transition must finish the edit even if playback already stopped.");
                Assert.That(editor.HistoryManager.CurrentIndex, Is.EqualTo(index + 1));
            });
            window.MouseMove(point + new Vector(40, 25), HeldButton(button));
            window.MouseUp(point, button);
            await Task.Delay(50);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(cube.Position.CurrentValue, Is.EqualTo(position));
                Assert.That(camera.Target.CurrentValue, Is.EqualTo(target));
                Assert.That(camera.Position.CurrentValue, Is.EqualTo(cameraPosition));
                Assert.That(editor.HistoryManager.CurrentIndex, Is.EqualTo(index + 1));
            });
        }
        finally
        {
            window.KeyRelease(Key.W, RawInputModifiers.None, PhysicalKey.W, "w");
            window.MouseUp(point, button);
            await editor.Player.Pause();
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase("Pointer")]
    [TestCase("Keyboard")]
    [TestCase("Timer")]
    public async Task Camera_callbacks_finish_edit_before_playback_notification(string callback)
    {
        EditViewModel editor = await OpenEditor();
        var scene = await Add3DScene(editor);
        var cube = scene.Objects.Single();
        var camera = scene.Camera.CurrentValue!;
        editor.GetRequiredService<IEditorSelection>().SelectedObject.Value = editor.Scene.Children.Single();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        MouseButton button = callback == "Pointer" ? MouseButton.Left : MouseButton.Right;
        Point point = default;
        using var release = new CancellationTokenSource();
        Task? render = null;
        try
        {
            await PreparePreview(view, window);
            editor.Player.IsMoveMode.Value = false;
            editor.Player.IsCameraMode.Value = true;
            point = PreviewCenter(view, window);
            window.MouseDown(point, button);
            window.MouseMove(point + new Vector(12, 8), HeldButton(button));
            if (callback == "Timer")
                window.KeyPress(Key.W, RawInputModifiers.None, PhysicalKey.W, "w");
            object handler = MouseHandler(view)!;
            int index = editor.HistoryManager.CurrentIndex;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            render = RenderThread.Dispatcher.InvokeAsync(() =>
            {
                entered.TrySetResult();
                release.Token.WaitHandle.WaitOne();
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            PublishPlayingOffUI(editor.Player);
            var position = cube.Position.CurrentValue;
            var target = camera.Target.CurrentValue;
            var cameraPosition = camera.Position.CurrentValue;
            Assert.That(MouseHandler(view), Is.SameAs(handler));
            release.CancelAfter(TimeSpan.FromSeconds(2));

            // Window input helpers flush dispatcher jobs; route the actual callback without pumping
            // so only its independent guard can stop this interaction before the queued notification.
            if (callback == "Pointer")
                RaiseMove(view, window, point + new Vector(40, 25), button);
            else if (callback == "Keyboard")
                view.FindControl<Panel>("framePanel")!.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.W,
                    PhysicalKey = PhysicalKey.W,
                    KeySymbol = "w"
                });
            else
                handler.GetType().GetMethod("OnMovementTimerTick", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(handler, [null, EventArgs.Empty]);

            Assert.Multiple(() =>
            {
                Assert.That(release.IsCancellationRequested, Is.False, "Early input must not wait for the render dispatcher.");
                Assert.That(MouseHandler(view), Is.SameAs(handler), "The subscription must still be pending when the guard is tested.");
                Assert.That(editor.HistoryManager.CurrentIndex, Is.EqualTo(index + 1), "The callback must finish the gesture itself.");
                Assert.That(cube.Position.CurrentValue, Is.EqualTo(position));
                Assert.That(camera.Target.CurrentValue, Is.EqualTo(target));
                Assert.That(camera.Position.CurrentValue, Is.EqualTo(cameraPosition));
            });
            release.Cancel();
            await render.WaitAsync(TimeSpan.FromSeconds(5));
            HeadlessTestHelpers.Settle();
            Assert.That(editor.HistoryManager.CurrentIndex, Is.EqualTo(index + 1), "The later notification must not commit twice.");
        }
        finally
        {
            release.Cancel();
            if (render is not null) await render.WaitAsync(TimeSpan.FromSeconds(5));
            window.KeyRelease(Key.W, RawInputModifiers.None, PhysicalKey.W, "w");
            window.MouseUp(point, button);
            await editor.Player.Pause();
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task Playback_finishes_active_move_transform(bool handle, bool queuedStop)
    {
        EditViewModel editor = await OpenEditor();
        var shape = new RectShape();
        await editor.GetRequiredService<IElementAdder>().AddAsync([new ElementDescription(
            TimeSpan.Zero, TimeSpan.FromSeconds(4), 0, new ElementSource.EngineObject(() => shape))], CancellationToken.None);
        editor.GetRequiredService<IEditorSelection>().SelectedObject.Value = editor.Scene.Children.Single();
        var originalTransform = shape.Transform.CurrentValue;
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        Point point = default;
        IPointer? captured = null;
        try
        {
            await PreparePreview(view, window);
            var panel = view.FindControl<Panel>("framePanel")!;
            panel.AddHandler(InputElement.PointerPressedEvent, (_, e) => captured = e.Pointer,
                RoutingStrategies.Bubble, handledEventsToo: true);
            point = PreviewCenter(view, window);
            if (handle)
                point += new Vector(50 * view.image.Bounds.Width / editor.Scene.FrameSize.Width, 0);
            else
                point += new Vector(20, 15) * (view.image.Bounds.Width / editor.Scene.FrameSize.Width);
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(point + new Vector(20, 10), RawInputModifiers.LeftMouseButton);
            var pose = TransformPose(shape);
            object handler = MouseHandler(view)!;
            int index = editor.HistoryManager.CurrentIndex;
            Assert.Multiple(() =>
            {
                Assert.That(pose, Is.Not.EqualTo((0f, 0f, 100f, 100f)));
                Assert.That(captured!.Captured, Is.SameAs(panel));
            });

            PublishPlayingOffUI(editor.Player, stop: queuedStop);
            Assert.That(MouseHandler(view), Is.SameAs(handler));
            if (queuedStop) HeadlessTestHelpers.Settle();
            else RaiseMove(view, window, point + new Vector(45, 25), MouseButton.Left);
            Assert.Multiple(() =>
            {
                Assert.That(TransformPose(shape), Is.EqualTo(pose));
                Assert.That(captured!.Captured, Is.Null, "Finishing the edit must release capture without rolling it back.");
                Assert.That(editor.HistoryManager.CurrentIndex, Is.EqualTo(index + 1));
            });

            editor.Player.IsPlaying.Value = false;
            HeadlessTestHelpers.Settle();
            window.MouseMove(point + new Vector(60, 35), RawInputModifiers.LeftMouseButton);
            window.MouseUp(point, MouseButton.Left);
            Assert.Multiple(() =>
            {
                Assert.That(TransformPose(shape), Is.EqualTo(pose));
                Assert.That(editor.HistoryManager.CurrentIndex, Is.EqualTo(index + 1));
                Assert.That(MouseHandler(view), Is.Null);
            });
            editor.HistoryManager.Undo();
            Assert.That(shape.Transform.CurrentValue, Is.SameAs(originalTransform));
            Capture(window, $"move-transition-{handle}-{queuedStop}");
        }
        finally
        {
            window.MouseUp(point, MouseButton.Left);
            await editor.Player.Pause();
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(false, MouseButton.Left)]
    [TestCase(true, MouseButton.Left)]
    [TestCase(true, MouseButton.Right)]
    public async Task Loop_boundary_input_does_not_wait_for_rearming_session(bool camera, MouseButton button)
    {
        EditViewModel editor = await OpenEditor();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        FieldInfo field = typeof(PlayerViewModel).GetField("_playbackTask", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object? original = field.GetValue(editor.Player);
        var session = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new CancellationTokenSource();
        Task? render = null;
        try
        {
            await PreparePreview(view, window);
            editor.Player.IsMoveMode.Value = !camera;
            editor.Player.IsCameraMode.Value = camera;
            editor.Player.IsLoopEnabled.Value = true;
            field.SetValue(editor.Player, session.Task);
            Assert.That(editor.Player.IsPlaying.Value, Is.False, "This is the inter-iteration window, not an ordinary playing tick.");
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            render = RenderThread.Dispatcher.InvokeAsync(() =>
            {
                entered.TrySetResult();
                release.Token.WaitHandle.WaitOne();
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            release.CancelAfter(TimeSpan.FromSeconds(2));
            Point point = PreviewCenter(view, window);
            window.MouseDown(point, button);
            bool returnedWhileRendering = !release.IsCancellationRequested;
            window.MouseUp(point, button);
            Assert.Multiple(() =>
            {
                Assert.That(returnedWhileRendering, Is.True);
                Assert.That(MouseHandler(view), Is.Null);
                Assert.That(session.Task.IsCompleted, Is.False);
            });
        }
        finally
        {
            release.Cancel();
            if (render is not null) await render.WaitAsync(TimeSpan.FromSeconds(5));
            session.TrySetResult();
            field.SetValue(editor.Player, original);
            await editor.Player.Pause();
            window.Close();
        }
    }

    private static object? MouseHandler(PlayerView view)
        => typeof(PlayerView).GetField("_mouseState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view);

    private static RawInputModifiers HeldButton(MouseButton button)
        => button == MouseButton.Left ? RawInputModifiers.LeftMouseButton : RawInputModifiers.RightMouseButton;

    private static void PublishPlayingOffUI(PlayerViewModel player, bool stop = false)
    {
        // Keep the UI turn occupied until both off-thread emissions are queued.
        Task publication = Task.Run(() =>
        {
            player.IsPlaying.Value = true;
            if (stop) player.IsPlaying.Value = false;
        });
        Assert.That(publication.Wait(TimeSpan.FromSeconds(5)), Is.True, "Publishing state must not wait for the UI dispatcher.");
    }

    private static void RaiseMove(PlayerView view, Window window, Point point, MouseButton button)
    {
        using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var panel = view.FindControl<Panel>("framePanel")!;
        var properties = new PointerPointProperties(HeldButton(button), PointerUpdateKind.Other);
        panel.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, panel, pointer, window,
            point, 1, properties, KeyModifiers.None));
    }

    private static (float X, float Y, float ScaleX, float ScaleY) TransformPose(RectShape shape)
    {
        var group = (TransformGroup)shape.Transform.CurrentValue!;
        var translate = group.Children.OfType<TranslateTransform>().Single();
        var scale = group.Children.OfType<ScaleTransform>().Single();
        return (translate.X.CurrentValue, translate.Y.CurrentValue, scale.ScaleX.CurrentValue, scale.ScaleY.CurrentValue);
    }
}
