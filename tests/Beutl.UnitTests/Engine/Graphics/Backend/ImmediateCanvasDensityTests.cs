using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

// feature 003: the ImmediateCanvas "density-aware logical surface" contract. The canvas BAKES a base CTM
// CreateScale(SurfaceDensity) at construction (a TRUE no-op at density 1), so logical geometry maps to the
// ceil(logical × density) device buffer automatically; device-space code opts out via PushDeviceSpace().
// These pin the load-bearing invariants of that contract (RenderTarget.Create needs Vulkan, so they skip
// when it is unavailable).
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

            // density 1 = byte-identity anchor: the matrix is never touched, density layers agree, and the
            // logical viewport defaults to the device size (logical == device).
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
            // A fractional logical viewport must survive verbatim (NOT round-tripped through device px) — it
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
                // A LOGICAL rect — the baked base CTM CreateScale(2) maps it to device (20,20)-(60,60).
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
                // Absolute device space: CTM identity, current density 1, but the surface density is unchanged
                // (a Snapshot taken here must still tag the whole surface at its real density).
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

    private static bool IsWhite(Bitmap bmp, int x, int y)
    {
        var p = bmp.SKBitmap.GetPixel(x, y);
        return p.Red > 150 && p.Green > 150 && p.Blue > 150;
    }
}
