using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

// feature 003: the ImmediateCanvas "density-aware logical surface" contract. The canvas bakes a base CTM
// CreateScale(SurfaceDensity) at construction (a no-op at density 1), so logical geometry maps to the
// ceil(logical × density) device buffer automatically; device-space code opts out via PushDeviceSpace().
// RenderTarget.Create needs Vulkan, so these skip when it is unavailable.
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

            // density 1 = byte-identity anchor: matrix untouched, density layers agree, logical viewport
            // defaults to the device size (logical == device).
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
            // A fractional logical viewport must survive verbatim, not round-tripped through device px: it
            // feeds Drawable layout, where a 200.5 -> 200 truncation would shift placement.
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
                // A logical rect; the baked base CTM CreateScale(2) maps it to device (20,20)-(60,60).
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
                // Absolute device space: CTM identity, current density 1, but surface density unchanged so a
                // Snapshot taken here still tags the whole surface at its real density.
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
                    // The ambient translate AND the base scale are both cancelled — device space is absolute.
                    Assert.That(canvas.Transform, Is.EqualTo(Matrix.Identity));
                }

                // Disposing PushDeviceSpace restores the ambient (base · translate), not the bare base.
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
                Matrix enclosing = canvas.Transform; // device space + a device translate
                using (canvas.PushDeviceSpace())
                {
                    Assert.That(canvas.Transform, Is.EqualTo(Matrix.Identity));
                }

                // Inner dispose returns to the ENCLOSING state, not the canvas base.
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
                // Set is base-aware: it does NOT clobber the surface density to a bare matrix.
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
                // Pop re-syncs from the SKCanvas matrix, which still carries the pinned base below it.
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

            // Open at density 2 (takes a base Save + SetMatrix), then dispose (must restore the save stack).
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
                    // Inside device space the Set-base is identity, so Set yields the bare matrix (no surface
                    // density re-injected): an absolute device-px Set, as device code expects.
                    Assert.That(canvas.Transform, Is.EqualTo(m));
                }
            }
        });
    }

    // feature 003: the DrawDrawable latent-bug fix — the nested build context must get the logical viewport,
    // not the device buffer size. With the old bug (device size as viewport) a centred shape would centre
    // against the 2x-larger viewport and miss the device centre.
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
        // Regression (CI host crash, feature 003): the canvas bakes a base Save() at construction (density != 1)
        // and undoes it in DisposeCore via Canvas.RestoreToCount(_baseSaveCount). The SKCanvas is cached from the
        // SKSurface at construction (owns: false, a child wrapper of the surface); disposing the surface zeroes
        // that wrapper's Handle. RestoreToCount on a zero-Handle wrapper passes a null SkCanvas* into native Skia
        // and SIGSEGVs the render thread, an uncatchable fault that neither the try/catch in DisposeCore nor a
        // non-null managed-wrapper check covers. DisposeCore must skip the restore when the wrapper is gone
        // (guarded by Canvas.Handle). We drive it deterministically: dispose the backing target first (zeroing
        // the cached canvas Handle), then the canvas, which must skip the restore rather than crash.
        //
        // The pre-fix failure is a native SIGSEGV, not a managed exception, so Assert.DoesNotThrow cannot observe
        // it — a regression hard-crashes the whole NUnit run instead of failing here. The real post-condition is
        // the IsDisposed == true check below, proving DisposeCore ran past the skipped RestoreToCount. DoesNotThrow
        // stays only to catch an adjacent managed-exception regression (e.g. a future guard that throws
        // ObjectDisposedException instead of skipping). A try/catch around RestoreToCount would not suffice, since
        // the fault is native — do not weaken the production guard on that belief.
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var target = RenderTarget.Create(200, 100)!;
            var canvas = new ImmediateCanvas(target, 2f, logicalSize: new Size(100, 50));

            target.Dispose(); // frees the SKSurface and zeroes the cached SKCanvas wrapper's Handle
            Assert.That(target.IsDisposed, Is.True);

            // Pin that the guarded skip branch is actually exercised: disposing the surface must have zeroed the
            // cached wrapper's Handle. A future SkiaSharp that left it non-zero would route through the normal
            // restore path, a false-green that stops covering the guard.
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
