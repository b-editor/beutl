using Avalonia.Headless.NUnit;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[NonParallelizable]
[TestFixture]
public class SelectedDrawableRenderTests
{
    private static Task ResetProjectAsync() => TestReset.ResetShellAsync();

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<EditViewModel> OpenEditor(string name)
    {
        Project project = (await TestShell.Project.CreateProject(
            320, 240, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    [AvaloniaTest]
    public async Task Shifted_selected_drawable_measure_matches_rasterization_and_caller_owns_result()
    {
        GpuTestGate.EnsureAvailable();
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditor("selected-drawable-shifted");
        var drawable = new RectShape
        {
            Width = { CurrentValue = 48 },
            Height = { CurrentValue = 32 },
            AlignmentX = { CurrentValue = AlignmentX.Left },
            AlignmentY = { CurrentValue = AlignmentY.Top },
            Transform = { CurrentValue = new TranslateTransform(37, 29) },
            Fill = { CurrentValue = new SolidColorBrush(Colors.Red) },
        };

        PixelSize measuredSize = await editor.Player.MeasureSelectedDrawable(drawable);
        Bitmap playerBitmap = await editor.Player.DrawSelectedDrawable(drawable);
        try
        {
            (RenderNodeMeasurement measurement, RenderNodeRasterization rasterization) =
                RenderSelectedDrawable(drawable, editor.Renderer.Value.FrameSize);
            try
            {
                Bitmap ownedBitmap = rasterization.Bitmap
                    ?? throw new AssertionException("The shifted non-empty drawable produced no bitmap.");
                Assert.Multiple(() =>
                {
                    Assert.That(measurement.OutputBounds, Is.EqualTo(new Rect(37, 29, 48, 32)));
                    Assert.That(rasterization.Bounds, Is.EqualTo(measurement.OutputBounds));
                    Assert.That(rasterization.IsEmpty, Is.False);
                    Assert.That(rasterization.IsDisposed, Is.False);
                    Assert.That(ownedBitmap.IsDisposed, Is.False,
                        "Disposing the renderer must not dispose its returned rasterization.");
                    Assert.That(measuredSize, Is.EqualTo(PixelRect.FromRect(measurement.OutputBounds).Size));
                    Assert.That(playerBitmap.Width, Is.EqualTo(ownedBitmap.Width));
                    Assert.That(playerBitmap.Height, Is.EqualTo(ownedBitmap.Height));
                    Assert.That(playerBitmap.IsDisposed, Is.False,
                        "PlayerViewModel must return a clone that survives disposal of its rasterization.");
                });

                rasterization.Dispose();
                Assert.Multiple(() =>
                {
                    Assert.That(rasterization.IsDisposed, Is.True);
                    Assert.That(ownedBitmap.IsDisposed, Is.True,
                        "The caller-owned rasterization must dispose its bitmap.");
                    Assert.That(
                        () => _ = rasterization.Bitmap,
                        Throws.TypeOf<ObjectDisposedException>());
                });

                rasterization.Dispose();
            }
            finally
            {
                rasterization.Dispose();
            }
        }
        finally
        {
            playerBitmap.Dispose();
        }

        Assert.That(playerBitmap.IsDisposed, Is.True);
    }

    [AvaloniaTest]
    public async Task Empty_selected_drawable_measure_matches_empty_rasterization()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditor("selected-drawable-empty");
        var drawable = new RectShape
        {
            Width = { CurrentValue = 0 },
            Height = { CurrentValue = 32 },
            AlignmentX = { CurrentValue = AlignmentX.Left },
            AlignmentY = { CurrentValue = AlignmentY.Top },
            Transform = { CurrentValue = new TranslateTransform(37, 29) },
        };

        PixelSize measuredSize = await editor.Player.MeasureSelectedDrawable(drawable);
        (RenderNodeMeasurement measurement, RenderNodeRasterization rasterization) =
            RenderSelectedDrawable(drawable, editor.Renderer.Value.FrameSize);

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(measuredSize, Is.EqualTo(PixelSize.Empty));
                Assert.That(measurement.OutputBounds, Is.EqualTo(Rect.Empty));
                Assert.That(rasterization.Bounds, Is.EqualTo(measurement.OutputBounds));
                Assert.That(rasterization.IsEmpty, Is.True);
                Assert.That(rasterization.Bitmap, Is.Null);
            });

            // Assert.ThrowsAsync blocks the Avalonia UI thread and deadlocks the headless dispatcher,
            // so await the empty-result failure inline with a bounded timeout.
            InvalidOperationException? exception = null;
            try
            {
                using Bitmap unexpected = await editor.Player.DrawSelectedDrawable(drawable)
                    .WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Fail("An empty selected drawable must not produce a bitmap.");
            }
            catch (InvalidOperationException ex)
            {
                exception = ex;
            }

            Assert.That(exception!.Message, Does.Contain("produced no raster output"));

            rasterization.Dispose();
            Assert.Multiple(() =>
            {
                Assert.That(rasterization.IsDisposed, Is.True);
                Assert.That(
                    () => _ = rasterization.Bitmap,
                    Throws.TypeOf<ObjectDisposedException>());
            });
        }
        finally
        {
            rasterization.Dispose();
        }
    }

    private static (RenderNodeMeasurement, RenderNodeRasterization) RenderSelectedDrawable(
        Drawable drawable,
        PixelSize frameSize)
    {
        return RenderThread.Dispatcher.Invoke(() =>
        {
            using var resource = drawable.ToResource(new CompositionContext(TimeSpan.Zero));
            using var root = new DrawableRenderNode(resource);
            using (var context = new GraphicsContext2D(root, frameSize.ToSize(1)))
            {
                drawable.Render(context, resource);
            }

            using var renderer = new RenderNodeRenderer(
                root,
                new RenderNodeRendererOptions
                {
                    Intent = RenderIntent.Delivery,
                    TargetDomain = new Rect(default, frameSize.ToSize(1)),
                    OutputScale = 1,
                    UseRenderCache = false,
                });
            RenderNodeMeasurement measurement = renderer.Measure();
            RenderNodeRasterization rasterization = renderer.Rasterize();
            return (measurement, rasterization);
        });
    }
}
