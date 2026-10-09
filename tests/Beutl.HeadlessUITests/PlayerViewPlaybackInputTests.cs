using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;

using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Gizmo;
using Beutl.Graphics3D.Primitives;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;

using Microsoft.Extensions.DependencyInjection;

using Vector3 = System.Numerics.Vector3;

namespace Beutl.HeadlessUITests;

[NonParallelizable]
[TestFixture]
public class PlayerViewPlaybackInputTests
{
    [AvaloniaTest]
    [TestCase(MouseButton.Left, false)]
    [TestCase(MouseButton.Left, true)]
    [TestCase(MouseButton.Middle, false)]
    public async Task Preview_input_during_playback_does_not_wait_for_the_render_thread(MouseButton button, bool handMode)
    {
        EditViewModel editor = await OpenEditor();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        using var release = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? render = null;
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            await RenderThread.Dispatcher.InvokeAsync(static () => { });
            HeadlessTestHelpers.Render();
            Assert.That(view.image.Bounds.Width, Is.GreaterThan(0), "The click must reach the preview hit-test path.");

            editor.Player.IsMoveMode.Value = !handMode;
            editor.Player.IsHandMode.Value = handMode;
            editor.Player.IsPlaying.Value = true;
            HeadlessTestHelpers.Render();
            var selection = editor.GetRequiredService<IEditorSelection>();
            object? selected = selection.SelectedObject.Value;
            var startMatrix = editor.Player.FrameMatrix.Value;
            Point point = view.image.TranslatePoint(new Point(view.image.Bounds.Width / 2, view.image.Bounds.Height / 2), window)!.Value;

            // Playback occupies the render dispatcher until its producer exits. Keep it busy in the
            // same way, with an independent deadline so a synchronous hit-test fails without hanging.
            render = RenderThread.Dispatcher.InvokeAsync(() =>
            {
                entered.TrySetResult();
                release.Token.WaitHandle.WaitOne();
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            release.CancelAfter(TimeSpan.FromSeconds(2));

            window.MouseDown(point, button);
            bool returnedWhileRendering = !release.IsCancellationRequested;
            if (handMode || button == MouseButton.Middle)
                window.MouseMove(point + new Vector(20, 10), button == MouseButton.Middle
                    ? RawInputModifiers.MiddleMouseButton : RawInputModifiers.LeftMouseButton);
            window.MouseUp(point + new Vector(20, 10), button);

            Assert.Multiple(() =>
            {
                Assert.That(returnedWhileRendering, Is.True, "Preview input must return while the playback producer still owns the render thread.");
                Assert.That(editor.Player.IsPlaying.Value, Is.True);
                Assert.That(selection.SelectedObject.Value, Is.SameAs(selected));
                if (handMode || button == MouseButton.Middle)
                    Assert.That(editor.Player.FrameMatrix.Value, Is.Not.EqualTo(startMatrix), "Panning must remain available during playback.");
            });
            Capture(window, $"playing-{button}-{handMode}");
        }
        finally
        {
            release.Cancel();
            if (render is not null)
                await render.WaitAsync(TimeSpan.FromSeconds(5));
            await editor.Player.Pause();
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(MouseButton.Left, false)]
    [TestCase(MouseButton.Right, false)]
    [TestCase(MouseButton.Left, true)]
    [TestCase(MouseButton.Right, true)]
    public async Task Camera_input_during_playback_does_not_wait_for_the_render_thread(MouseButton button, bool selectedScene)
    {
        EditViewModel editor = await OpenEditor();
        Scene3D? scene = selectedScene ? await Add3DScene(editor) : null;
        var selection = editor.GetRequiredService<IEditorSelection>();
        selection.SelectedObject.Value = selectedScene ? editor.Scene.Children.Single() : null;
        if (scene != null)
        {
            scene.GizmoTarget.CurrentValue = scene.Objects.Single().Id;
            scene.GizmoMode.CurrentValue = GizmoMode.Translate;
        }
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        using var release = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? render = null;
        try
        {
            await PreparePreview(view, window);
            editor.Player.IsMoveMode.Value = false;
            editor.Player.IsCameraMode.Value = true;
            editor.Player.IsPlaying.Value = true;
            HeadlessTestHelpers.Render();
            object? selected = selection.SelectedObject.Value;
            Vector3? target = scene?.Camera.CurrentValue?.Target.CurrentValue;
            Vector3? position = scene?.Objects.Single().Position.CurrentValue;
            Point point = PreviewCenter(view, window);

            render = RenderThread.Dispatcher.InvokeAsync(() =>
            {
                entered.TrySetResult();
                release.Token.WaitHandle.WaitOne();
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            release.CancelAfter(TimeSpan.FromSeconds(2));

            window.MouseDown(point, button);
            bool returnedWhileRendering = !release.IsCancellationRequested;
            window.MouseMove(point + new Vector(20, 10), button == MouseButton.Left
                ? RawInputModifiers.LeftMouseButton : RawInputModifiers.RightMouseButton);
            window.MouseUp(point, button);

            Assert.Multiple(() =>
            {
                Assert.That(returnedWhileRendering, Is.True, "Camera input must not wait for playback to release the render thread.");
                Assert.That(selection.SelectedObject.Value, Is.SameAs(selected));
                Assert.That(editor.Player.IsPlaying.Value, Is.True);
                if (scene != null)
                {
                    Assert.That(scene.Camera.CurrentValue!.Target.CurrentValue, Is.EqualTo(target!.Value));
                    Assert.That(scene.Objects.Single().Position.CurrentValue, Is.EqualTo(position!.Value));
                }
            });
            Capture(window, $"camera-playing-{button}-{selectedScene}");
        }
        finally
        {
            release.Cancel();
            if (render is not null) await render.WaitAsync(TimeSpan.FromSeconds(5));
            await editor.Player.Pause();
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(MouseButton.Left)]
    [TestCase(MouseButton.Right)]
    public async Task Playback_start_finishes_an_active_camera_drag(MouseButton button)
    {
        EditViewModel editor = await OpenEditor();
        Scene3D scene = await Add3DScene(editor);
        Object3D cube = scene.Objects.Single();
        var camera = scene.Camera.CurrentValue!;
        editor.GetRequiredService<IEditorSelection>().SelectedObject.Value = editor.Scene.Children.Single();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        using var release = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? render = null;
        Point point = default;
        RawInputModifiers modifiers = button == MouseButton.Left ? RawInputModifiers.LeftMouseButton : RawInputModifiers.RightMouseButton;
        try
        {
            await PreparePreview(view, window);
            editor.Player.IsMoveMode.Value = false;
            editor.Player.IsCameraMode.Value = true;
            point = PreviewCenter(view, window);
            Vector3 originalPosition = cube.Position.CurrentValue;
            Vector3 originalTarget = camera.Target.CurrentValue;

            window.MouseDown(point, button);
            window.MouseMove(point + new Vector(12, 8), modifiers);
            Vector3 position = cube.Position.CurrentValue;
            Vector3 target = camera.Target.CurrentValue;
            Assert.That(button == MouseButton.Left ? position != originalPosition : target != originalTarget,
                Is.True, "The drag must actually edit the object or camera before playback starts.");

            editor.Player.IsPlaying.Value = true;
            HeadlessTestHelpers.Settle();
            render = RenderThread.Dispatcher.InvokeAsync(() =>
            {
                entered.TrySetResult();
                release.Token.WaitHandle.WaitOne();
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            release.CancelAfter(TimeSpan.FromSeconds(2));

            window.MouseMove(point + new Vector(40, 25), modifiers);
            bool returnedWhileRendering = !release.IsCancellationRequested;
            Assert.Multiple(() =>
            {
                Assert.That(returnedWhileRendering, Is.True, "An existing drag must not query the renderer during playback.");
                Assert.That(cube.Position.CurrentValue, Is.EqualTo(position));
                Assert.That(camera.Target.CurrentValue, Is.EqualTo(target));
            });

            release.Cancel();
            await render.WaitAsync(TimeSpan.FromSeconds(5));
            editor.Player.IsPlaying.Value = false;
            HeadlessTestHelpers.Settle();
            window.MouseMove(point + new Vector(60, 35), modifiers);
            window.MouseUp(point, button);
            Assert.Multiple(() =>
            {
                Assert.That(cube.Position.CurrentValue, Is.EqualTo(position), "The old drag must not resume after playback stops.");
                Assert.That(camera.Target.CurrentValue, Is.EqualTo(target));
            });

            editor.HistoryManager.Undo();
            Assert.Multiple(() =>
            {
                Assert.That(cube.Position.CurrentValue, Is.EqualTo(originalPosition));
                Assert.That(camera.Target.CurrentValue, Is.EqualTo(originalTarget), "Playback must finish the edit as one undoable gesture.");
            });
        }
        finally
        {
            release.Cancel();
            if (render is not null) await render.WaitAsync(TimeSpan.FromSeconds(5));
            window.MouseUp(point, button);
            await editor.Player.Pause();
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Playback_start_stops_camera_key_movement()
    {
        EditViewModel editor = await OpenEditor();
        Scene3D scene = await Add3DScene(editor);
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
            Vector3 originalPosition = camera.Position.CurrentValue;
            window.MouseDown(point, MouseButton.Right);
            window.KeyPress(Key.W, RawInputModifiers.None, PhysicalKey.W, "w");
            for (int i = 0; i < 40 && camera.Position.CurrentValue == originalPosition; i++)
            {
                await Task.Delay(25);
                HeadlessTestHelpers.Settle();
            }
            Assert.That(camera.Position.CurrentValue, Is.Not.EqualTo(originalPosition), "The movement timer must be running before playback.");

            editor.Player.IsPlaying.Value = true;
            HeadlessTestHelpers.Settle();
            Vector3 position = camera.Position.CurrentValue;
            await Task.Delay(80);
            HeadlessTestHelpers.Settle();
            Assert.That(camera.Position.CurrentValue, Is.EqualTo(position));

            editor.Player.IsPlaying.Value = false;
            HeadlessTestHelpers.Settle();
            await Task.Delay(80);
            HeadlessTestHelpers.Settle();
            Assert.That(camera.Position.CurrentValue, Is.EqualTo(position), "Held movement keys must not restart the old session after playback stops.");
        }
        finally
        {
            window.KeyRelease(Key.W, RawInputModifiers.None, PhysicalKey.W, "w");
            window.MouseUp(point, MouseButton.Right);
            await editor.Player.Pause();
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(MouseButton.Left)]
    [TestCase(MouseButton.Right)]
    public async Task Camera_drag_is_available_after_playback_stops(MouseButton button)
    {
        EditViewModel editor = await OpenEditor();
        Scene3D scene = await Add3DScene(editor);
        Object3D cube = scene.Objects.Single();
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
            editor.Player.IsPlaying.Value = true;
            HeadlessTestHelpers.Settle();
            editor.Player.IsPlaying.Value = false;
            HeadlessTestHelpers.Settle();
            point = PreviewCenter(view, window);
            Vector3 position = cube.Position.CurrentValue;
            Vector3 target = camera.Target.CurrentValue;
            window.MouseDown(point, button);
            window.MouseMove(point + new Vector(20, 10), button == MouseButton.Left
                ? RawInputModifiers.LeftMouseButton : RawInputModifiers.RightMouseButton);
            window.MouseUp(point, button);

            Assert.That(button == MouseButton.Left ? cube.Position.CurrentValue != position : camera.Target.CurrentValue != target,
                Is.True, "Object selection and camera control must be available while paused.");
            Capture(window, $"camera-stopped-{button}");
        }
        finally
        {
            window.MouseUp(point, button);
            window.Close();
        }
    }

    private static async Task<Scene3D> Add3DScene(EditViewModel editor)
    {
        GpuTestGate.EnsureAvailable();
        var scene = new Scene3D();
        scene.RenderWidth.CurrentValue = 640;
        scene.RenderHeight.CurrentValue = 480;
        scene.Objects.Add(new Cube3D());
        await editor.GetRequiredService<IElementAdder>().AddAsync([new ElementDescription(
            TimeSpan.Zero, TimeSpan.FromSeconds(4), 0, new ElementSource.EngineObject(() => scene))], CancellationToken.None);
        return scene;
    }

    private static async Task PreparePreview(PlayerView view, Window window)
    {
        window.Show();
        HeadlessTestHelpers.Render();
        await RenderThread.Dispatcher.InvokeAsync(static () => { });
        HeadlessTestHelpers.Render();
        Assert.That(view.image.Bounds.Width, Is.GreaterThan(0));
    }

    private static Point PreviewCenter(PlayerView view, Window window)
        => view.image.TranslatePoint(new Point(view.image.Bounds.Width / 2, view.image.Bounds.Height / 2), window)!.Value;

    [AvaloniaTest]
    public async Task Preview_selection_is_available_after_playback_stops()
    {
        EditViewModel editor = await OpenEditor();
        await editor.GetRequiredService<IElementAdder>().AddAsync([new ElementDescription(
            Start: TimeSpan.Zero,
            Length: TimeSpan.FromSeconds(4),
            Layer: 0,
            Source: new ElementSource.EngineObject(() => new RectShape()))], CancellationToken.None);
        var selection = editor.GetRequiredService<IEditorSelection>();
        selection.SelectedObject.Value = null;
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            await RenderThread.Dispatcher.InvokeAsync(static () => { });
            HeadlessTestHelpers.Render();
            editor.Player.IsPlaying.Value = true;
            HeadlessTestHelpers.Settle();
            editor.Player.IsPlaying.Value = false;
            HeadlessTestHelpers.Settle();
            Point point = view.image.TranslatePoint(new Point(view.image.Bounds.Width / 2, view.image.Bounds.Height / 2), window)!.Value;

            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            HeadlessTestHelpers.Render();

            Assert.That(selection.SelectedObject.Value, Is.SameAs(editor.Scene.Children.Single()));
            Capture(window, "stopped-selection");
        }
        finally { window.Close(); }
    }

    private static async Task<EditViewModel> OpenEditor()
    {
        await TestReset.ResetShellAsync();
        string name = $"preview-playback-input-{Guid.NewGuid():N}";
        string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(directory);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, name, directory))!;
        TestShell.Editor.ActivateTabItem(project.Items.OfType<Scene>().First());
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("BEUTL_PLAYER_INPUT_CAPTURE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame();
        image?.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
    }
}
