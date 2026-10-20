using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;

using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Gizmo;
using Beutl.Graphics3D.Primitives;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;

using Microsoft.Extensions.DependencyInjection;

using Vector3 = System.Numerics.Vector3;

namespace Beutl.HeadlessUITests;

[NonParallelizable]
[TestFixture]
public partial class PlayerViewPlaybackInputTests
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
        Task? render = null;
        try
        {
            await PreparePreview(view, window);

            editor.Player.IsMoveMode.Value = !handMode;
            editor.Player.IsHandMode.Value = handMode;
            editor.Player.IsPlaying.Value = true;
            HeadlessTestHelpers.Render();
            var selection = editor.GetRequiredService<IEditorSelection>();
            object? selected = selection.SelectedObject.Value;
            var startMatrix = editor.Player.FrameMatrix.Value;
            Point point = PreviewCenter(view, window);

            // Playback occupies the render dispatcher until its producer exits. Keep it busy in the
            // same way, with an independent deadline so a synchronous hit-test fails without hanging.
            render = await BlockRenderThreadAsync(release);
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

            render = await BlockRenderThreadAsync(release);
            release.CancelAfter(TimeSpan.FromSeconds(2));

            window.MouseDown(point, button);
            bool returnedWhileRendering = !release.IsCancellationRequested;
            window.MouseMove(point + new Vector(20, 10), HeldButton(button));
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
        Task? render = null;
        Point point = default;
        RawInputModifiers modifiers = HeldButton(button);
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
            render = await BlockRenderThreadAsync(release);
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
            window.MouseMove(point + new Vector(20, 10), HeldButton(button));
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

    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public async Task The_wheel_dollies_the_camera(bool selectedScene)
    {
        EditViewModel editor = await OpenEditor();
        Scene3D scene = await Add3DScene(editor);
        var camera = scene.Camera.CurrentValue!;
        editor.GetRequiredService<IEditorSelection>().SelectedObject.Value =
            selectedScene ? editor.Scene.Children.Single() : null;
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            await PreparePreview(view, window);
            editor.Player.IsMoveMode.Value = false;
            editor.Player.IsCameraMode.Value = true;
            Vector3 position = camera.Position.CurrentValue;
            Vector3 target = camera.Target.CurrentValue;

            window.MouseWheel(PreviewCenter(view, window), new Vector(0, 1));

            Vector3 movement = camera.Position.CurrentValue - position;
            Assert.Multiple(() =>
            {
                Assert.That(movement, Is.Not.EqualTo(Vector3.Zero), "The wheel must move the camera.");
                Assert.That(Vector3.Distance(camera.Target.CurrentValue - target, movement), Is.LessThan(1e-3f),
                    "The target moves with the camera.");
            });

            editor.HistoryManager.Undo();
            Assert.Multiple(() =>
            {
                Assert.That(camera.Position.CurrentValue, Is.EqualTo(position));
                Assert.That(camera.Target.CurrentValue, Is.EqualTo(target));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_wheel_leaves_the_camera_alone_while_a_button_is_held()
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
            Vector3 position = camera.Position.CurrentValue;

            window.MouseDown(point, MouseButton.Right);
            window.MouseMove(point + new Vector(20, 10), RawInputModifiers.RightMouseButton);
            window.MouseWheel(point + new Vector(20, 10), new Vector(0, 1), RawInputModifiers.RightMouseButton);

            Assert.That(camera.Position.CurrentValue, Is.EqualTo(position),
                "A wheel step during an orbit would be committed together with the unfinished orbit.");

            window.MouseUp(point + new Vector(20, 10), MouseButton.Right);
            window.MouseWheel(point, new Vector(0, 1));
            Assert.That(camera.Position.CurrentValue, Is.Not.EqualTo(position), "After the drag the wheel dollies again.");
        }
        finally
        {
            window.MouseUp(point, MouseButton.Right);
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task A_3D_move_is_skipped_while_the_render_thread_does_not_answer()
    {
        EditViewModel editor = await OpenEditor();
        Scene3D scene = await Add3DScene(editor);
        Object3D cube = scene.Objects.Single();
        editor.GetRequiredService<IEditorSelection>().SelectedObject.Value = editor.Scene.Children.Single();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        using var release = new CancellationTokenSource();
        Task? render = null;
        TimeSpan wait = PlayerView.RenderThreadWait;
        Point point = default;
        try
        {
            await PreparePreview(view, window);
            editor.Player.IsMoveMode.Value = false;
            editor.Player.IsCameraMode.Value = true;
            point = PreviewCenter(view, window);
            window.MouseDown(point, MouseButton.Left);
            Assert.That(scene.GizmoTarget.CurrentValue, Is.EqualTo(cube.Id), "The press must select the cube.");
            Vector3 position = cube.Position.CurrentValue;
            PlayerView.RenderThreadWait = TimeSpan.FromMilliseconds(50);
            render = await BlockRenderThreadAsync(release);

            window.MouseMove(point + new Vector(30, 10), RawInputModifiers.LeftMouseButton);
            Assert.That(cube.Position.CurrentValue, Is.EqualTo(position));

            release.Cancel();
            await render.WaitAsync(TimeSpan.FromMinutes(1));
            PlayerView.RenderThreadWait = wait;
            window.MouseMove(point + new Vector(60, 20), RawInputModifiers.LeftMouseButton);
            Assert.That(cube.Position.CurrentValue, Is.Not.EqualTo(position), "A move the render thread answers goes through.");
        }
        finally
        {
            PlayerView.RenderThreadWait = wait;
            release.Cancel();
            if (render is not null) await render.WaitAsync(TimeSpan.FromSeconds(5));
            window.MouseUp(point, MouseButton.Left);
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Cancelling_a_move_keeps_a_nudge_made_just_before_it()
    {
        EditViewModel editor = await OpenEditor();
        var shape = new RectShape();
        await editor.GetRequiredService<IElementAdder>().AddAsync([new ElementDescription(
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), 0, new ElementSource.EngineObject(() => shape))], CancellationToken.None);
        Element element = editor.Scene.Children.Single();
        editor.GetRequiredService<IEditorSelection>().SelectedObject.Value = element;
        editor.GetRequiredService<IEditorClock>().CurrentTime.Value = TimeSpan.FromSeconds(2);
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        IPointer? captured = null;
        Point point = default;
        try
        {
            await PreparePreview(view, window);
            view.FindControl<Panel>("framePanel")!.AddHandler(InputElement.PointerPressedEvent,
                (_, e) => captured = e.Pointer, RoutingStrategies.Bubble, handledEventsToo: true);
            int index = editor.HistoryManager.CurrentIndex;
            point = PreviewCenter(view, window);

            editor.GetRequiredService<IElementNudgeService>().Nudge(editor.Scene, [element], 1);
            TimeSpan nudged = element.Start;
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(point + new Vector(20, 10), RawInputModifiers.LeftMouseButton);
            Assert.That(editor.HistoryManager.CurrentIndex, Is.EqualTo(index + 1), "The move commits the nudge before it writes.");
            Assert.That(editor.HistoryManager.HasPendingOperations, Is.True, "The move must have changed the shape.");
            captured!.Capture(null);
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(element.Start, Is.EqualTo(nudged));
                Assert.That(editor.HistoryManager.CurrentIndex, Is.EqualTo(index + 1));
                Assert.That(editor.HistoryManager.PeekUndo()!.Name, Is.EqualTo("CommandNames.MoveElement"));
                Assert.That(editor.HistoryManager.HasPendingOperations, Is.False);
            });
        }
        finally
        {
            window.MouseUp(point, MouseButton.Left);
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task A_3D_press_keeps_the_selection_while_the_render_thread_does_not_answer()
    {
        EditViewModel editor = await OpenEditor();
        Scene3D scene = await Add3DScene(editor);
        Object3D cube = scene.Objects.Single();
        editor.GetRequiredService<IEditorSelection>().SelectedObject.Value = editor.Scene.Children.Single();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        using var release = new CancellationTokenSource();
        Task? render = null;
        TimeSpan wait = PlayerView.RenderThreadWait;
        Point point = default;
        try
        {
            await PreparePreview(view, window);
            editor.Player.IsMoveMode.Value = false;
            editor.Player.IsCameraMode.Value = true;
            point = PreviewCenter(view, window);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.That(scene.GizmoTarget.CurrentValue, Is.EqualTo(cube.Id), "The first press must select the cube.");
            PlayerView.RenderThreadWait = TimeSpan.FromMilliseconds(50);
            render = await BlockRenderThreadAsync(release);

            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);

            Assert.That(scene.GizmoTarget.CurrentValue, Is.EqualTo(cube.Id));
        }
        finally
        {
            PlayerView.RenderThreadWait = wait;
            release.Cancel();
            if (render is not null) await render.WaitAsync(TimeSpan.FromSeconds(5));
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task A_press_keeps_the_handles_on_their_drawable_while_the_render_thread_does_not_answer()
    {
        EditViewModel editor = await OpenEditor();
        var aside = new RectShape();
        aside.Width.CurrentValue = 200;
        aside.Height.CurrentValue = 200;
        aside.Transform.CurrentValue = new TranslateTransform(-600, 0);
        var clicked = new RectShape();
        clicked.Width.CurrentValue = 400;
        clicked.Height.CurrentValue = 300;
        await editor.GetRequiredService<IElementAdder>().AddAsync([new ElementDescription(
            TimeSpan.Zero, TimeSpan.FromSeconds(4), 0, new ElementSource.EngineObject(() => aside))], CancellationToken.None);
        Element element = editor.Scene.Children.Single();
        element.Objects.Add(clicked);
        var selection = editor.GetRequiredService<IEditorSelection>();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        using var release = new CancellationTokenSource();
        Task? render = null;
        TimeSpan wait = PlayerView.RenderThreadWait;
        Point point = default;
        try
        {
            await PreparePreview(view, window);
            editor.Player.IsMoveMode.Value = true;
            point = PreviewCenter(view, window) + new Vector(30, 20);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            UpdateHandles();
            Assert.That(view.transformHandlesOverlay.Drawable, Is.SameAs(clicked), "The first press must put the handles on the drawable it hit.");
            PlayerView.RenderThreadWait = TimeSpan.FromMilliseconds(50);
            render = await BlockRenderThreadAsync(release);

            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            release.Cancel();
            await render.WaitAsync(TimeSpan.FromMinutes(1));
            PlayerView.RenderThreadWait = wait;
            Assert.That(selection.SelectedObject.Value, Is.SameAs(element));
            UpdateHandles();

            Assert.That(view.transformHandlesOverlay.Drawable, Is.SameAs(clicked));
        }
        finally
        {
            PlayerView.RenderThreadWait = wait;
            release.Cancel();
            if (render is not null) await render.WaitAsync(TimeSpan.FromSeconds(5));
            window.Close();
        }

        // Selecting the element again places the handles on the drawable the last press hit.
        void UpdateHandles()
        {
            selection.SelectedObject.Value = null;
            HeadlessTestHelpers.Render();
            selection.SelectedObject.Value = element;
            HeadlessTestHelpers.Render();
        }
    }

    [AvaloniaTest]
    public async Task A_renderer_replaced_while_a_query_runs_is_disposed_after_the_query()
    {
        EditViewModel editor = await OpenEditor();
        SceneRenderer replaced = editor.Renderer.Value;
        using var release = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool disposedUnderTheQuery = true;
        Task query = RenderThread.Dispatcher.InvokeAsync(() =>
        {
            entered.TrySetResult();
            release.Token.WaitHandle.WaitOne();
            disposedUnderTheQuery = replaced.IsDisposed;
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // The new renderer's constructor holds the UI thread until the query ends, so another thread ends it.
            release.CancelAfter(TimeSpan.FromMilliseconds(200));
            editor.PreviewScale.Value = RenderScale.Half;
            await query.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(editor.Renderer.Value, Is.Not.SameAs(replaced));
                Assert.That(disposedUnderTheQuery, Is.False);
                Assert.That(replaced.IsDisposed, Is.True);
            });
        }
        finally
        {
            release.Cancel();
            await query.WaitAsync(TimeSpan.FromSeconds(5));
            editor.PreviewScale.Value = RenderScale.Full;
        }
    }

    private static async Task<Scene3D> Add3DScene(EditViewModel editor)
    {
        TestGpu.RequireAnyDevice();
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
        Assert.That(view.image.Bounds.Width, Is.GreaterThan(0), "The click must reach the preview hit-test path.");
    }

    private static Point PreviewCenter(PlayerView view, Window window)
        => view.image.TranslatePoint(new Point(view.image.Bounds.Width / 2, view.image.Bounds.Height / 2), window)!.Value;

    // Returns once a job holds the render thread. The job keeps it until release is canceled.
    private static async Task<Task> BlockRenderThreadAsync(CancellationTokenSource release)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task render = RenderThread.Dispatcher.InvokeAsync(() =>
        {
            entered.TrySetResult();
            release.Token.WaitHandle.WaitOne();
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        return render;
    }

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
            await PreparePreview(view, window);
            editor.Player.IsPlaying.Value = true;
            HeadlessTestHelpers.Settle();
            editor.Player.IsPlaying.Value = false;
            HeadlessTestHelpers.Settle();
            Point point = PreviewCenter(view, window);

            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            HeadlessTestHelpers.Render();

            Assert.That(selection.SelectedObject.Value, Is.SameAs(editor.Scene.Children.Single()));
            Capture(window, "stopped-selection");
        }
        finally { window.Close(); }
    }

    private static async Task<EditViewModel> OpenEditor()
        => (await TestShell.OpenNewSceneAsync("preview-playback-input")).Editor;

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("BEUTL_PLAYER_INPUT_CAPTURE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame();
        image?.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
    }
}
