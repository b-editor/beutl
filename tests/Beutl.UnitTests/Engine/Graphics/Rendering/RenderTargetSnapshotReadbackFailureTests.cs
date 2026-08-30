using System.Runtime.CompilerServices;

using Beutl.Graphics.Rendering;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// Pins that <see cref="RenderTarget.Snapshot"/> and <see cref="RenderTarget.SnapshotAlpha"/> release the
/// bitmap they allocated when the surface readback fails.
/// </summary>
/// <remarks>
/// <para>
/// A failed readback is a device-loss symptom, and the callers that meet it snapshot once per frame, so they
/// ask again on the next one. Propagating without releasing leaves one full-frame native bitmap behind per
/// attempt for a finalizer to find, which is the worst moment to be holding them.
/// </para>
/// <para>
/// The bitmap never escapes the failing call, so no test can hold it and read <see cref="Bitmap.IsDisposed"/>.
/// What the release does leave observable is finalization: <see cref="Bitmap.Dispose"/> ends in
/// <see cref="GC.SuppressFinalize"/>, so a released bitmap never reaches the finalization queue while a
/// stranded one — and the <c>SKBitmap</c> it owns — does. Every measurement is calibrated against a
/// deliberate leak of the very bitmaps the snapshot would allocate, so a harness that stopped seeing leaks
/// fails loudly instead of passing vacuously.
/// </para>
/// <para>
/// A null Skia surface stands in for the lost device: it declines every CPU readback, which is the one
/// observable <see cref="RenderTarget"/> branches on. <see cref="RenderTarget.SnapshotInto(Bitmap)"/> is
/// deliberately outside the release — its destination belongs to the caller, who keeps it either way — and
/// the last test pins that carve-out.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class RenderTargetSnapshotReadbackFailureTests
{
    private const int Width = 48;
    private const int Height = 32;

    /// <summary>
    /// How many failed snapshots one measurement takes. Large enough that the objects a stranded bitmap
    /// contributes cannot be confused with the handful the surrounding harness allocates.
    /// </summary>
    private const int Attempts = 64;

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
    public void Snapshot_ReleasesTheBitmapItAllocated_WhenTheReadbackFails()
    {
        AssertFailedSnapshotStrandsNothing(
            static target => target.Snapshot(),
            BitmapColorType.RgbaF16);
    }

    [Test]
    public void SnapshotAlpha_ReleasesTheBitmapItAllocated_WhenTheReadbackFails()
    {
        AssertFailedSnapshotStrandsNothing(
            static target => target.SnapshotAlpha(),
            BitmapColorType.Alpha8);
    }

    /// <remarks>
    /// The negative control for the releases above: a surface that can serve the readback must still hand
    /// back a live bitmap. Without it a fixture that stopped reaching the readback at all — or one whose
    /// null surface failed something earlier — would satisfy every assertion above by doing nothing.
    /// </remarks>
    [Test]
    public void Snapshot_ReturnsALiveBitmap_WhenTheReadbackSucceeds()
    {
        using RenderTarget target = new RasterRenderTarget(Width, Height);

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

    /// <remarks>
    /// The carve-out: <see cref="RenderTarget.SnapshotInto(Bitmap)"/> fills a destination the caller owns and
    /// keeps whether or not the readback succeeds, so the release must not reach it.
    /// </remarks>
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

    /// <summary>
    /// Measures the finalizable garbage <paramref name="snapshot"/> leaves behind over <see cref="Attempts"/>
    /// failed readbacks, against the same count of deliberately stranded bitmaps of
    /// <paramref name="colorType"/>.
    /// </summary>
    private static void AssertFailedSnapshotStrandsNothing(
        Func<RenderTarget, Bitmap> snapshot,
        BitmapColorType colorType)
    {
        using RenderTarget target = RenderTarget.CreateNull(Width, Height);
        int failures = 0;

        long stranded = MeasureFinalizableGarbage(() => StrandBitmaps(Attempts, colorType));
        long observed = MeasureFinalizableGarbage(() => failures = FailSnapshots(target, snapshot, Attempts));

        Assert.Multiple(() =>
        {
            Assert.That(
                stranded,
                Is.GreaterThanOrEqualTo(Attempts),
                "the calibration leak has to register, or the measurement below cannot tell a leak from a release");
            Assert.That(
                failures,
                Is.EqualTo(Attempts),
                "every attempt has to reach the failing readback, or the test proves nothing");
            Assert.That(
                observed,
                Is.LessThan(Attempts),
                "a failed readback must release the bitmap it allocated instead of leaving it for a finalizer");
        });
    }

    /// <summary>
    /// Runs <paramref name="body"/> and reports how many objects it left waiting on the finalization queue.
    /// </summary>
    /// <remarks>
    /// Both halves of a released snapshot bitmap suppress their own finalizer — <see cref="Bitmap.Dispose"/>
    /// and SkiaSharp's own — so only a stranded one is counted here.
    /// </remarks>
    private static long MeasureFinalizableGarbage(Action body)
    {
        Quiesce();
        long before = GC.GetGCMemoryInfo().FinalizationPendingCount;
        body();

        // Promotes whatever the body abandoned onto the finalization queue without draining it, which is
        // what makes the count readable.
        GC.Collect();
        return GC.GetGCMemoryInfo().FinalizationPendingCount - before;
    }

    /// <summary>Drains the queue so the previous measurement's leak cannot be charged to the next one.</summary>
    private static void Quiesce()
    {
        for (int i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        GC.Collect();
    }

    /// <remarks>
    /// Not inlined, so no caller frame can keep the abandoned bitmaps reachable past the measurement's
    /// collection.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int FailSnapshots(RenderTarget target, Func<RenderTarget, Bitmap> snapshot, int attempts)
    {
        int failures = 0;
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                snapshot(target).Dispose();
            }
            catch (InvalidOperationException)
            {
                failures++;
            }
        }

        return failures;
    }

    /// <summary>
    /// Allocates the bitmaps a snapshot would and abandons them undisposed — the shape a failed readback
    /// left behind before it released them.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StrandBitmaps(int count, BitmapColorType colorType)
    {
        for (int i = 0; i < count; i++)
        {
            _ = new Bitmap(Width, Height, colorType, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);
        }
    }

    /// <summary>
    /// A CPU-raster target, whose surface serves a readback. The control for the null-surface fixture.
    /// </summary>
    private sealed class RasterRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);
}
