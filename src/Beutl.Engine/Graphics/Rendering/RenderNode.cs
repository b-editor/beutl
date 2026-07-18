using System.Runtime.ExceptionServices;
using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics.Rendering;

public abstract class RenderNode : IDisposable
{
    private const int ActiveDisposeState = 0;
    private const int DisposingState = 1;
    private const int DisposedState = 2;

    private int _disposeState;

    protected RenderNode()
    {
        Cache = new RenderNodeCache(this);
    }

    ~RenderNode()
    {
        if (!TryBeginDispose())
            return;

        try
        {
            OnDispose(false);
        }
        catch
        {
            // Finalizers must never allow cleanup failures to escape onto the finalizer thread.
        }
        finally
        {
            CompleteDispose();
        }
    }

    public bool IsDisposed => Volatile.Read(ref _disposeState) == DisposedState;

    public bool HasChanges { get; set; }

    public RenderNodeCache Cache { get; }

    public abstract RenderNodeOperation[] Process(RenderNodeContext context);

    public void Dispose()
    {
        if (!TryBeginDispose())
            return;

        Exception? failure = null;
        try
        {
            OnDispose(true);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        try
        {
            Cache.Dispose();
        }
        catch (Exception ex)
        {
            failure ??= ex;
        }
        finally
        {
            CompleteDispose();
            GC.SuppressFinalize(this);
        }

        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private bool TryBeginDispose()
        => Interlocked.CompareExchange(
            ref _disposeState,
            DisposingState,
            ActiveDisposeState) == ActiveDisposeState;

    private void CompleteDispose()
    {
        Volatile.Write(ref _disposeState, DisposedState);
    }

    protected virtual void OnDispose(bool disposing)
    {
    }

    /// <summary>
    /// Called when this node's output will be served from a render-node cache — its own or an ancestor's — so
    /// <see cref="Process"/> will not run on subsequent frames. Overriders must release any cross-frame resources
    /// they hold outside that node cache (e.g. a retained pooled lease) so it is not stranded until node dispose.
    /// The default is a no-op; a later cache invalidation re-runs <see cref="Process"/>, which re-acquires as needed.
    /// </summary>
    protected internal virtual void OnServedFromCache()
    {
    }
}
