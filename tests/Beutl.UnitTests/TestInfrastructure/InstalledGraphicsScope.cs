using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.TestInfrastructure;

/// <summary>Installs a graphics state for one test and puts the previous one back when disposed.</summary>
internal sealed class InstalledGraphicsScope : IDisposable
{
    private readonly InstalledGraphics _previous;
    private readonly bool _releaseDiscarded;
    private readonly bool _drainOnDispose;

    private InstalledGraphicsScope(InstalledGraphics graphics, bool releaseDiscarded, bool drainOnDispose)
    {
        _releaseDiscarded = releaseDiscarded;
        _drainOnDispose = drainOnDispose;
        _previous = GraphicsContextFactory.ExchangeInstalledGraphics(graphics);
    }

    /// <summary>Installs <paramref name="graphics"/>, leaving what the test installs after it to the test.</summary>
    public static InstalledGraphicsScope Install(InstalledGraphics graphics) => new(graphics, false, false);

    /// <summary>Installs <paramref name="context"/> as the shared context of a state that initialized.</summary>
    public static InstalledGraphicsScope Install(IGraphicsContext? context)
        => Install(new InstalledGraphics(context, null, null, FailedToInitialize: false));

    /// <summary>
    /// Installs <paramref name="graphics"/> and, on dispose, releases the context and the instance the test left
    /// installed. The instance that goes back is never released.
    /// </summary>
    public static InstalledGraphicsScope InstallReleasing(InstalledGraphics graphics) => new(graphics, true, false);

    /// <summary>
    /// Stands <paramref name="context"/> in for the shared context of a test that shuts it down, and releases what the
    /// test left installed on dispose.
    /// </summary>
    /// <remarks>
    /// Flushes what the live context still owes first: a resource left queued would be flushed against the stand-in
    /// instead, on a call it never declared. With <paramref name="drainOnDispose"/>, dispose drains the queue before
    /// putting the live state back. That is a no-op once the teardown discharged the queue, kept so a regression that
    /// skips the discharge cannot leak the stand-in resource into the rest of the run.
    /// </remarks>
    public static InstalledGraphicsScope StandIn(IGraphicsContext context, bool drainOnDispose = false)
    {
        GpuResourceReclaimQueue.FlushAndDrain();
        return new(new InstalledGraphics(context, null, null, FailedToInitialize: false), true, drainOnDispose);
    }

    public void Dispose()
    {
        if (_drainOnDispose)
            GpuResourceReclaimQueue.DrainAfterContextSync();

        InstalledGraphics discarded = GraphicsContextFactory.ExchangeInstalledGraphics(_previous);
        if (!_releaseDiscarded)
            return;

        discarded.SharedContext?.Dispose();
        if (!ReferenceEquals(discarded.VulkanInstance, _previous.VulkanInstance))
            discarded.VulkanInstance?.Dispose();
    }
}
