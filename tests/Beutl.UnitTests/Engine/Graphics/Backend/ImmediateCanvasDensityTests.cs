using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

// ImmediateCanvas density-aware surface contract. The canvas bakes a base CTM of CreateScale(density)
// at construction. RenderTarget.Create needs Vulkan.
[NonParallelizable]
[TestFixture]
public class ImmediateCanvasDensityTests
{
    [Test]
    public void Density1_Construction_IsTrueNoOp()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(64, 48)!;
            using var canvas = new ImmediateCanvas(target, 1f);

            // density 1: matrix untouched, logical == device.
            Assert.That(canvas.Transform, Is.EqualTo(Matrix.Identity));
            Assert.That(canvas.Density, Is.EqualTo(1f));
            Assert.That(canvas.SurfaceDensity, Is.EqualTo(1f));
            Assert.That(canvas.DeviceSize, Is.EqualTo(new PixelSize(64, 48)));
            Assert.That(canvas.LogicalSize, Is.EqualTo(new Size(64, 48)));
        });
    }

    [Test]
    public void Density2_Construction_BakesBaseScale()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(200, 100)!;
            using var canvas = new ImmediateCanvas(target, 2f, logicalSize: new Size(100, 50));

            Assert.That(canvas.Transform, Is.EqualTo(Matrix.CreateScale(2f, 2f)));
            Assert.That(canvas.Density, Is.EqualTo(2f));
            Assert.That(canvas.SurfaceDensity, Is.EqualTo(2f));
            Assert.That(canvas.DeviceSize, Is.EqualTo(new PixelSize(200, 100)));
            Assert.That(canvas.LogicalSize, Is.EqualTo(new Size(100, 50)));
        });
    }

    [Test]
    public void LogicalSize_Fractional_PreservedExactly()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(401, 201)!;
            // Fractional logical viewport must survive verbatim, not truncated through device px.
            using var canvas = new ImmediateCanvas(target, 2f, logicalSize: new Size(200.5f, 100.25f));

            Assert.That(canvas.LogicalSize, Is.EqualTo(new Size(200.5f, 100.25f)));
            Assert.That(canvas.DeviceSize, Is.EqualTo(new PixelSize(401, 201)));
        });
    }

    [Test]
    public void Density2_DrawRectangle_MapsLogicalToDevice()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(100, 100)!;
            using (var canvas = new ImmediateCanvas(target, 2f, logicalSize: new Size(50, 50)))
            {
                canvas.Clear(Colors.Black);
                // Logical rect; base CTM maps it to device (20,20)-(60,60).
                canvas.DrawRectangle(new Rect(10, 10, 20, 20), Brushes.Resource.White, null);
            }

            using Bitmap snap = target.Snapshot();
            Assert.That(IsWhite(snap, 40, 40), Is.True, "logical rect centre should be white at device (40,40)");
            Assert.That(IsWhite(snap, 8, 8), Is.False, "device (8,8) = logical (4,4) is outside the rect");
            Assert.That(IsWhite(snap, 70, 70), Is.False, "device (70,70) = logical (35,35) is outside the rect");
        });
    }

    [Test]
    public void PushDeviceSpace_FlipsToDeviceDensity_RestoredOnDispose()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(200, 100)!;
            using var canvas = new ImmediateCanvas(target, 2f, logicalSize: new Size(100, 50));

            using (canvas.PushDeviceSpace())
            {
                // Absolute device space: CTM identity, density 1, surface density unchanged.
                Assert.That(canvas.Transform, Is.EqualTo(Matrix.Identity));
                Assert.That(canvas.Density, Is.EqualTo(1f));
                Assert.That(canvas.SurfaceDensity, Is.EqualTo(2f));
            }

            Assert.That(canvas.Transform, Is.EqualTo(Matrix.CreateScale(2f, 2f)));
            Assert.That(canvas.Density, Is.EqualTo(2f));
        });
    }

    [Test]
    public void PushDeviceSpace_UnderAmbientTransform_IsAbsoluteDevice()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(200, 100)!;
            using var canvas = new ImmediateCanvas(target, 2f, logicalSize: new Size(100, 50));

            using (canvas.PushTransform(Matrix.CreateTranslation(17, 23)))
            {
                using (canvas.PushDeviceSpace())
                {
                    // Both ambient translate and base scale are cancelled.
                    Assert.That(canvas.Transform, Is.EqualTo(Matrix.Identity));
                }

                // Restores the ambient (base * translate), not the bare base.
                Assert.That(canvas.Transform, Is.EqualTo(Matrix.CreateScale(2f, 2f).Prepend(Matrix.CreateTranslation(17, 23))));
            }
        });
    }

    [Test]
    public void NestedPushDeviceSpace_RestoresToEnclosingState_NotBase()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(200, 100)!;
            using var canvas = new ImmediateCanvas(target, 2f, logicalSize: new Size(100, 50));

            using (canvas.PushDeviceSpace())
            using (canvas.PushTransform(Matrix.CreateTranslation(5, 5)))
            {
                Matrix enclosing = canvas.Transform;
                using (canvas.PushDeviceSpace())
                {
                    Assert.That(canvas.Transform, Is.EqualTo(Matrix.Identity));
                }

                // Inner dispose returns to the enclosing state, not the canvas base.
                Assert.That(canvas.Transform, Is.EqualTo(enclosing));
                Assert.That(canvas.Density, Is.EqualTo(1f));
            }
        });
    }

    [Test]
    public void PushTransform_Set_ReAppliesBase()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(200, 100)!;
            using var canvas = new ImmediateCanvas(target, 2f, logicalSize: new Size(100, 50));

            var m = Matrix.CreateTranslation(7, 9);
            using (canvas.PushTransform(m, TransformOperator.Set))
            {
                // Set is base-aware: does not clobber the surface density.
                Assert.That(canvas.Transform, Is.EqualTo(Matrix.CreateScale(2f, 2f).Prepend(m)));
            }

            Assert.That(canvas.Transform, Is.EqualTo(Matrix.CreateScale(2f, 2f)));
        });
    }

    [Test]
    public void PushPop_PreservesBaseTransform()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(200, 100)!;
            using var canvas = new ImmediateCanvas(target, 2f, logicalSize: new Size(100, 50));

            using (canvas.PushTransform(Matrix.CreateTranslation(11, 13)))
            {
                // Pop re-syncs from the SKCanvas matrix, which still carries the pinned base.
            }

            Assert.That(canvas.Transform, Is.EqualTo(Matrix.CreateScale(2f, 2f)),
                "the pinned base CTM must survive a Push/Pop cycle");
        });
    }

    [Test]
    public void SameRenderTarget_ReOpenedAtDifferentDensity_NoBaseLeak()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(200, 100)!;

            // Open at density 2, then dispose (must restore the save stack).
            using (var canvas = new ImmediateCanvas(target, 2f, logicalSize: new Size(100, 50)))
            {
                Assert.That(canvas.Transform, Is.EqualTo(Matrix.CreateScale(2f, 2f)));
            }

            // Re-open the SAME target at density 1: it must NOT inherit the previous base matrix / save depth.
            using (var canvas2 = new ImmediateCanvas(target, 1f))
            {
                Assert.That(canvas2.Transform, Is.EqualTo(Matrix.Identity),
                    "a reused RenderTarget must not carry the prior canvas's base CTM");
            }
        });
    }

    [Test]
    public void PushDeviceSpace_ThenSet_IsAbsoluteDevice()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(200, 100)!;
            using var canvas = new ImmediateCanvas(target, 2f, logicalSize: new Size(100, 50));

            using (canvas.PushDeviceSpace())
            {
                var m = Matrix.CreateTranslation(7, 9);
                using (canvas.PushTransform(m, TransformOperator.Set))
                {
                    // Inside device space the Set-base is identity, so Set yields the bare matrix.
                    Assert.That(canvas.Transform, Is.EqualTo(m));
                }
            }
        });
    }

    // DrawDrawable must pass the logical viewport, not the device buffer size, to the build context.
    [Test]
    public void DrawDrawable_CentresAgainstLogicalViewport_NotDeviceSize()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var shape = new RectShape();
            shape.Width.CurrentValue = 10;
            shape.Height.CurrentValue = 10;
            shape.AlignmentX.CurrentValue = AlignmentX.Center;
            shape.AlignmentY.CurrentValue = AlignmentY.Center;
            shape.Fill.CurrentValue = Brushes.White;
            using Drawable.Resource resource = shape.ToResource(CompositionContext.Default);

            using var target = RenderTarget.Create(100, 100)!; // device = ceil(50 logical x 2)
            using (var canvas = new ImmediateCanvas(target, 2f, logicalSize: new Size(50, 50)))
            {
                canvas.Clear(Colors.Black);
                canvas.DrawDrawable(resource);
            }

            using Bitmap snap = target.Snapshot();
            // logical centre (25,25) -> device (50,50); the 10-logical shape occupies device (40,40)-(60,60).
            Assert.That(IsWhite(snap, 50, 50), Is.True,
                "the shape should centre against the LOGICAL 50x50 viewport (device centre)");
            Assert.That(IsWhite(snap, 95, 95), Is.False,
                "if DrawDrawable used the device size as the viewport, the shape would drift toward the corner");
        });
    }

    [Test]
    public void Dispose_AfterBackingRenderTargetDisposed_SkipsBaseRestoreInsteadOfCrashing()
    {
        // Regression: disposing the backing surface zeroes the cached SKCanvas Handle; RestoreToCount on
        // a zero-Handle SIGSEGVs the render thread. DisposeCore must skip the restore when Handle is zero.
        // A regression hard-crashes the NUnit run (native fault, not a managed exception).
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var target = RenderTarget.Create(200, 100)!;
            var canvas = new ImmediateCanvas(target, 2f, logicalSize: new Size(100, 50));

            target.Dispose();
            Assert.That(target.IsDisposed, Is.True);

            // Verify the precondition: disposing the surface must have zeroed the cached Handle.
            Assert.That(canvas.Canvas.Handle, Is.EqualTo(IntPtr.Zero),
                "precondition: disposing the backing surface must zero the cached SKCanvas wrapper's Handle");

            Assert.DoesNotThrow(() => canvas.Dispose(),
                "DisposeCore must skip the base RestoreToCount once the cached SKCanvas wrapper is disposed");
            Assert.That(canvas.IsDisposed, Is.True);
        });
    }

    private static bool IsWhite(Bitmap bmp, int x, int y)
    {
        var p = bmp.SKBitmap.GetPixel(x, y);
        return p.Red > 150 && p.Green > 150 && p.Blue > 150;
    }
}
