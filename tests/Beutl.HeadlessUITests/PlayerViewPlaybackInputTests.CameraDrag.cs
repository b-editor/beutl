using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;

using Beutl.Editor.Services;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Graphics3D;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;

using Microsoft.Extensions.DependencyInjection;

using Vector3 = System.Numerics.Vector3;

namespace Beutl.HeadlessUITests;

public partial class PlayerViewPlaybackInputTests
{
    [AvaloniaTest]
    public async Task Camera_drag_presents_a_frame_when_more_input_arrives_during_rendering()
    {
        EditViewModel editor = await OpenEditor();
        Scene3D scene = await Add3DScene(editor);
        var camera = scene.Camera.Value!;
        editor.GetRequiredService<IEditorSelection>().Select(editor.Scene.Children.Single());
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        var renderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseRender = new ManualResetEventSlim();
        int blockNextRender = 0;
        int framesPresented = 0;
        Point point = default;

        scene.AmbientIntensity.Expression = new PreviewRenderProbe(scene.AmbientIntensity.Value, () =>
        {
            if (Interlocked.Exchange(ref blockNextRender, 0) == 1)
            {
                renderStarted.TrySetResult();
                releaseRender.Wait();
            }
        });

        void OnPreviewInvalidated(object? sender, EventArgs e) => Interlocked.Increment(ref framesPresented);

        try
        {
            await PreparePreview(view, window);
            editor.Player.SelectPreviewTool(PreviewTool.Camera3D);
            point = PreviewCenter(view, window);
            window.MouseDown(point, MouseButton.Right);
            RenderThreadDrain.Drain(TimeSpan.FromSeconds(5));
            editor.Player.PreviewInvalidated += OnPreviewInvalidated;

            Vector3 originalTarget = camera.Target.Value;
            Volatile.Write(ref blockNextRender, 1);
            window.MouseMove(point + new Vector(16, 0), RawInputModifiers.RightMouseButton);
            await renderStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Vector3 firstTarget = camera.Target.Value;
            Assert.That(firstTarget, Is.Not.EqualTo(originalTarget));

            // This job runs after the in-flight render and ahead of any render for the following moves.
            Task<int> framesAfterFirstRender = RenderThread.Dispatcher.InvokeAsync(() => Volatile.Read(ref framesPresented));
            window.MouseMove(point + new Vector(30, 6), RawInputModifiers.RightMouseButton);
            window.MouseMove(point + new Vector(46, 12), RawInputModifiers.RightMouseButton);
            Assert.That(camera.Target.Value, Is.Not.EqualTo(firstTarget));

            releaseRender.Set();
            Assert.That(await framesAfterFirstRender.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(1),
                "An edit arriving mid-render must not discard the frame being drawn.");
            await HeadlessTestHelpers.WaitUntilAsync(() => Volatile.Read(ref framesPresented) >= 2,
                message: "The last camera update must reach the preview while the button is still held.");
            HeadlessTestHelpers.Render();
            Capture(window, "3d-camera-drag");
        }
        finally
        {
            editor.Player.PreviewInvalidated -= OnPreviewInvalidated;
            releaseRender.Set();
            window.MouseUp(point, MouseButton.Right);
            await RenderThread.Dispatcher.InvokeAsync(static () => { }).WaitAsync(TimeSpan.FromSeconds(5));
            window.Close();
        }
    }

    // Render pixels the cube moves along world X and Y per frame pixel dragged right; one is 1080 / 480 world units.
    [AvaloniaTest]
    [TestCase(50f, 0f, 2f, 0f)]
    [TestCase(100f, 90f, 0f, -1f)]
    public async Task A_3D_drag_keeps_the_object_under_the_pointer_in_a_scaled_or_turned_scene(
        float scale, float rotation, float expectedX, float expectedY)
    {
        EditViewModel editor = await OpenEditor();
        Scene3D scene = await Add3DScene(editor);
        Object3D cube = scene.Objects.Single();
        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(100, 100, scale));
        transform.Children.Add(new RotationTransform(rotation));
        scene.Transform.Value = transform;
        editor.GetRequiredService<IEditorSelection>().Select(editor.Scene.Children.Single());
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        Point point = default;
        try
        {
            await PreparePreview(view, window);
            editor.Player.IsMoveMode.Value = false;
            editor.Player.IsCameraMode.Value = true;
            point = PreviewCenter(view, window);
            Vector3 start = cube.Position.Value;

            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(point + new Vector(40, 0), RawInputModifiers.LeftMouseButton);
            window.MouseUp(point + new Vector(40, 0), MouseButton.Left);

            double oneToOneMove = 40 * 640 / view.image.Bounds.Width * (1080.0 / 480.0);
            Vector3 moved = cube.Position.Value - start;
            Assert.Multiple(() =>
            {
                Assert.That(moved.X, Is.EqualTo(expectedX * oneToOneMove).Within(0.5));
                Assert.That(moved.Y, Is.EqualTo(expectedY * oneToOneMove).Within(0.5));
            });
        }
        finally
        {
            window.MouseUp(point, MouseButton.Left);
            window.Close();
        }
    }

    private sealed class PreviewRenderProbe(float value, Action onRender) : IExpression<float>
    {
        public string ExpressionString => "Preview render probe";

        public float Evaluate(ExpressionContext context)
        {
            if (RenderThread.Dispatcher.CheckAccess()) onRender();
            return value;
        }

        public bool Validate(out string? error)
        {
            error = null;
            return true;
        }
    }
}
