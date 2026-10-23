using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
[NonParallelizable]
public sealed class RenderTargetSnapshotReadbackFailureTests
{
    private const int Width = 48;
    private const int Height = 32;

    private const string ReadbackFailureMessage =
        "Failed to read the render target surface into the destination bitmap.";

    [Test]
    public void Snapshot_ReportsTheReadbackFailure_WhenTheSurfaceCannotBeRead()
    {
        using RenderTarget target = RenderTarget.CreateNull(Width, Height);

        Assert.That(
            () => target.Snapshot().Dispose(),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(ReadbackFailureMessage));
    }

    [Test]
    public void SnapshotAlpha_ReportsTheReadbackFailure_WhenTheSurfaceCannotBeRead()
    {
        using RenderTarget target = RenderTarget.CreateNull(Width, Height);

        Assert.That(
            () => target.SnapshotAlpha().Dispose(),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(ReadbackFailureMessage));
    }

    [Test]
    public void Snapshot_ReturnsALiveBitmap_WhenTheReadbackSucceeds()
    {
        using RenderTarget target = new CpuRenderTarget(Width, Height);

        using Bitmap snapshot = target.Snapshot();
        using Bitmap alpha = target.SnapshotAlpha();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.IsDisposed, Is.False);
            Assert.That(snapshot.SKBitmap.Handle, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(snapshot.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
            Assert.That(alpha.IsDisposed, Is.False);
            Assert.That(alpha.SKBitmap.Handle, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(alpha.ColorType, Is.EqualTo(BitmapColorType.Alpha8));
        });
    }

    [Test]
    public void SnapshotInto_KeepsTheCallersBitmap_WhenTheReadbackFails()
    {
        using RenderTarget target = RenderTarget.CreateNull(Width, Height);
        using Bitmap destination = target.CreateSnapshotBitmap();

        Assert.That(
            () => target.SnapshotInto(destination),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo(ReadbackFailureMessage));

        Assert.Multiple(() =>
        {
            Assert.That(
                destination.IsDisposed,
                Is.False,
                "the destination belongs to the caller, who keeps it whether the readback succeeded or not");
            Assert.That(destination.SKBitmap.Handle, Is.Not.EqualTo(IntPtr.Zero));
        });
    }
}
