using Avalonia.Headless.NUnit;
using Beutl.Composition;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[NonParallelizable]
[TestFixture]
public class SelectedDrawableRenderTests
{
    // A 48x32 red rectangle with its top-left corner moved to (x, y).
    private static RectShape CreateRedRect(float x, float y)
        => new()
        {
            Width = { Value = 48 },
            Height = { Value = 32 },
            AlignmentX = { Value = AlignmentX.Left },
            AlignmentY = { Value = AlignmentY.Top },
            Transform = { Value = new TranslateTransform(x, y) },
            Fill = { Value = new SolidColorBrush(Colors.Red) },
        };

    [AvaloniaTest]
    public async Task Shifted_selected_drawable_measure_matches_rasterization_and_caller_owns_result()
    {
        TestGpu.RequireAnyDevice();
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("selected-drawable-shifted", 320, 240);
        var drawable = CreateRedRect(37, 29);

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
                bool contentMatches = playerBitmap.GetPixelSpan<ushort>()
                    .SequenceEqual(ownedBitmap.GetPixelSpan<ushort>());
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
                    Assert.That(contentMatches, Is.True,
                        "PlayerViewModel must return the same rendered pixels as direct rasterization.");
                    Assert.That(playerBitmap.IsDisposed, Is.False,
                        "PlayerViewModel must return a clone that survives disposal of its rasterization.");
                });
                AssertOpaqueRedCenter(playerBitmap, "PlayerViewModel bitmap");
                AssertOpaqueRedCenter(ownedBitmap, "direct rasterization bitmap");

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
    public async Task Selected_drawable_outside_the_frame_is_still_exported_whole()
    {
        TestGpu.RequireAnyDevice();
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("selected-drawable-off-frame", 320, 240);
        // The project frame is 320x240, so this sits entirely beyond its bottom-right corner.
        var drawable = CreateRedRect(400, 300);

        PixelSize measuredSize = await editor.Player.MeasureSelectedDrawable(drawable);
        Bitmap bitmap = await editor.Player.DrawSelectedDrawable(drawable);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(measuredSize, Is.EqualTo(new PixelSize(48, 32)));
                Assert.That(bitmap.Width, Is.EqualTo(48));
                Assert.That(bitmap.Height, Is.EqualTo(32));
            });
            AssertOpaqueRedCenter(bitmap, "off-frame export");
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task Selected_drawable_straddling_the_frame_edge_keeps_its_hidden_half()
    {
        TestGpu.RequireAnyDevice();
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("selected-drawable-straddling", 320, 240);
        // Half of its width hangs past the frame's right edge at x = 320.
        var drawable = CreateRedRect(296, 100);

        PixelSize measuredSize = await editor.Player.MeasureSelectedDrawable(drawable);

        Assert.That(measuredSize, Is.EqualTo(new PixelSize(48, 32)));
    }

    private static void AssertOpaqueRedCenter(Bitmap bitmap, string label)
    {
        Assert.That(bitmap.ColorType, Is.EqualTo(BitmapColorType.RgbaF16), label);
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        int offset = (((bitmap.Height / 2) * bitmap.Width) + (bitmap.Width / 2)) * 4;
        float red = (float)BitConverter.UInt16BitsToHalf(pixels[offset]);
        float green = (float)BitConverter.UInt16BitsToHalf(pixels[offset + 1]);
        float blue = (float)BitConverter.UInt16BitsToHalf(pixels[offset + 2]);
        float alpha = (float)BitConverter.UInt16BitsToHalf(pixels[offset + 3]);

        Assert.Multiple(() =>
        {
            Assert.That(red, Is.EqualTo(1).Within(0.001), $"{label} center red");
            Assert.That(green, Is.EqualTo(0).Within(0.001), $"{label} center green");
            Assert.That(blue, Is.EqualTo(0).Within(0.001), $"{label} center blue");
            Assert.That(alpha, Is.EqualTo(1).Within(0.001), $"{label} center alpha");
        });
    }

    [AvaloniaTest]
    public async Task Empty_selected_drawable_measure_matches_empty_rasterization()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("selected-drawable-empty", 320, 240);
        var drawable = new RectShape
        {
            Width = { Value = 0 },
            Height = { Value = 32 },
            AlignmentX = { Value = AlignmentX.Left },
            AlignmentY = { Value = AlignmentY.Top },
            Transform = { Value = new TranslateTransform(37, 29) },
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

            // Keep the rasterization wait bounded so a stalled UI operation fails the test.
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

    [AvaloniaTest]
    public async Task Group_uses_its_content_extent_for_measurement_and_rasterization()
    {
        TestGpu.RequireAnyDevice();
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("selected-drawable-full-target-group", 320, 240);
        var group = new DrawableGroup();
        group.Children.Add(CreateRedRect(37, 29));

        PixelSize measuredSize = await editor.Player.MeasureSelectedDrawable(group);
        using Bitmap bitmap = await editor.Player.DrawSelectedDrawable(group);
        (RenderNodeMeasurement measurement, RenderNodeRasterization rasterization) =
            RenderSelectedDrawable(group, editor.Renderer.Value.FrameSize);
        using (rasterization)
        {
            Assert.Multiple(() =>
            {
                Assert.That(measurement.OutputBounds, Is.EqualTo(new Rect(37, 29, 48, 32)));
                Assert.That(measurement.QueryBounds, Is.EqualTo(new Rect(37, 29, 48, 32)));
                Assert.That(rasterization.Bounds, Is.EqualTo(measurement.OutputBounds));
                Assert.That(measuredSize, Is.EqualTo(new PixelSize(48, 32)));
                Assert.That(bitmap.Width, Is.EqualTo(48));
                Assert.That(bitmap.Height, Is.EqualTo(32));
            });
        }
    }

    [AvaloniaTest]
    public async Task Full_target_only_drawable_renders_when_query_bounds_are_empty()
    {
        TestGpu.RequireAnyDevice();
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("selected-drawable-full-target-only", 320, 240);
        var drawable = new SelectedDrawableFullTargetDrawable();

        PixelSize measuredSize = await editor.Player.MeasureSelectedDrawable(drawable);
        using Bitmap bitmap = await editor.Player.DrawSelectedDrawable(drawable);
        (RenderNodeMeasurement measurement, RenderNodeRasterization rasterization) =
            RenderSelectedDrawable(drawable, editor.Renderer.Value.FrameSize);
        using (rasterization)
        {
            Assert.Multiple(() =>
            {
                Assert.That(measurement.OutputBounds, Is.EqualTo(new Rect(0, 0, 320, 240)));
                Assert.That(measurement.QueryBounds, Is.EqualTo(Rect.Empty));
                Assert.That(rasterization.Bounds, Is.EqualTo(measurement.OutputBounds));
                Assert.That(measuredSize, Is.EqualTo(new PixelSize(320, 240)));
                Assert.That(bitmap.Width, Is.EqualTo(320));
                Assert.That(bitmap.Height, Is.EqualTo(240));
            });
        }
    }

    [AvaloniaTest]
    public async Task Full_target_drawable_grouped_with_off_frame_content_keeps_that_content()
    {
        TestGpu.RequireAnyDevice();
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("selected-drawable-full-target-off-frame", 320, 240);
        var group = new DrawableGroup();
        group.Children.Add(new SelectedDrawableFullTargetDrawable());
        group.Children.Add(CreateRedRect(400, 300));

        PixelSize measuredSize = await editor.Player.MeasureSelectedDrawable(group);
        using Bitmap bitmap = await editor.Player.DrawSelectedDrawable(group);

        Assert.Multiple(() =>
        {
            Assert.That(measuredSize, Is.EqualTo(new PixelSize(448, 332)));
            Assert.That(bitmap.Width, Is.EqualTo(448));
            Assert.That(bitmap.Height, Is.EqualTo(332));
        });
    }

    [AvaloniaTest]
    public async Task Nested_scene_selected_drawable_records_the_requested_output_scale()
    {
        TestGpu.RequireAnyDevice();
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("selected-drawable-nested-scale", 320, 240);
        string location = TestShell.NewWorkspace("selected-drawable-nested-scale-source");
        var innerScene = new Scene(64, 48, string.Empty)
        {
            Uri = new Uri(Path.Combine(location, "inner.scene"))
        };
        var capture = new SelectedDrawableScaleCaptureDrawable();
        var element = new Element
        {
            Start = { Value = MediaTime.Zero },
            Length = { Value = MediaTime.FromSeconds(1) },
            IsEnabled = { Value = true },
            Uri = new Uri(Path.Combine(location, "nested.layer"))
        };
        element.AddObject(capture);
        element.AddObject(new RectShape
        {
            Width = { Value = 64 },
            Height = { Value = 48 },
        });
        innerScene.Children.Add(element);
        var drawable = new SceneDrawable();
        var owner = new Scene();
        owner.Children.Add(new Element { Objects = { drawable } });
        _ = new BeutlApplication { Project = { Value = new Project { Items = { owner, innerScene } } } };
        drawable.ReferencedScene.Expression = new ReferenceExpression<Scene?>(innerScene.Id.Value);

        using Bitmap bitmap = await editor.Player.DrawSelectedDrawable(drawable, outputScale: 2);

        Assert.Multiple(() =>
        {
            Assert.That(capture.ObservedOutputScales, Is.EqualTo(new[] { 2f }));
            Assert.That(bitmap.Width, Is.EqualTo(128));
            Assert.That(bitmap.Height, Is.EqualTo(96));
        });
    }

    [AvaloniaTest]
    public async Task Selected_drawable_records_the_sanitized_output_scale_for_an_invalid_request()
    {
        TestGpu.RequireAnyDevice();
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("selected-drawable-invalid-scale", 320, 240);

        foreach (float requested in new[] { float.NaN, 0f, -2f })
        {
            var capture = new SelectedDrawableScaleCaptureFullTargetDrawable();

            using Bitmap bitmap = await editor.Player.DrawSelectedDrawable(capture, requested);

            Assert.Multiple(() =>
            {
                Assert.That(
                    capture.ObservedOutputScales,
                    Is.EqualTo(new[] { 1f }),
                    $"recording context for outputScale {requested}");
                Assert.That(bitmap.Width, Is.EqualTo(320), $"width for outputScale {requested}");
                Assert.That(bitmap.Height, Is.EqualTo(240), $"height for outputScale {requested}");
            });
        }
    }

    private static (RenderNodeMeasurement, RenderNodeRasterization) RenderSelectedDrawable(
        Drawable drawable,
        PixelSize frameSize)
    {
        return RenderThread.Dispatcher.Invoke(() =>
        {
            using var resource = drawable.ToResource(new CompositionContext(MediaTime.Zero));
            using var root = new DrawableRenderNode(resource);
            using (var context = new GraphicsContext2D(root, frameSize.ToSize(1)))
            {
                resource.Render(context);
            }

            var request = new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Delivery,
                TargetDomain = new Rect(default, frameSize.ToSize(1)),
                OutputScale = 1,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            };
            using var renderer = new RenderNodeRenderer(root, request);
            RenderNodeMeasurement measurement = renderer.Measure();
            RenderNodeRasterization rasterization = renderer.Rasterize(request with
            {
                RequestedRegion = measurement.OutputBounds,
            });
            return (measurement, rasterization);
        });
    }
}

internal sealed partial class SelectedDrawableFullTargetDrawable : Drawable
{
    public new partial class Resource
    {
        protected override Size MeasureCore(Size availableSize)
            => Size.Empty;

        protected override void OnDraw(GraphicsContext2D context)
            => context.Clear(Colors.CornflowerBlue);
    }
}

internal sealed partial class SelectedDrawableScaleCaptureDrawable : Drawable
{
    public List<float> ObservedOutputScales { get; } = [];

    public new partial class Resource
    {
        protected override void RenderCore(GraphicsContext2D context)
        {
            RequireOriginal().ObservedOutputScales.Add(context.OutputScale);
        }

        protected override Size MeasureCore(Size availableSize)
            => Size.Empty;

        protected override void OnDraw(GraphicsContext2D context)
        {
        }
    }
}

internal sealed partial class SelectedDrawableScaleCaptureFullTargetDrawable : Drawable
{
    public List<float> ObservedOutputScales { get; } = [];

    public new partial class Resource
    {
        protected override void RenderCore(GraphicsContext2D context)
        {
            RequireOriginal().ObservedOutputScales.Add(context.OutputScale);
            base.RenderCore(context);
        }

        protected override Size MeasureCore(Size availableSize)
            => Size.Empty;

        protected override void OnDraw(GraphicsContext2D context)
            => context.Clear(Colors.CornflowerBlue);
    }
}
