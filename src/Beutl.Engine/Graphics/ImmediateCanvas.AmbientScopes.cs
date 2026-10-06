using Beutl.Graphics.Rendering.Requests;

namespace Beutl.Graphics;

public partial class ImmediateCanvas
{
    internal IDisposable PushRenderTargetLeaseSession(RenderTargetLeaseSession? leaseSession)
    {
        VerifyAccess();
        var scope = new RenderTargetLeaseSessionScope(
            this,
            parent: s_renderTargetLeaseSession.Value,
            leaseSession: leaseSession,
            previous: RenderTargetLeaseSession);
        RenderTargetLeaseSession = leaseSession;
        s_renderTargetLeaseSession.Value = scope;
        return scope;
    }

    /// <summary>
    /// Installs <paramref name="materializer"/> for the returned scope's lifetime, restoring the previous hook
    /// on dispose.
    /// </summary>
    /// <remarks>
    /// Scopes nest, and the returned scope must be closed before any scope pushed after it — a
    /// <see langword="using"/> declaration or block gives that ordering for free. Closing one early would
    /// re-install a hook whose scope has ended over the one still in force, and because a canvas seeds itself
    /// from the ambient hook at construction, every canvas built afterwards would inherit it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown by the returned scope's <see cref="IDisposable.Dispose"/> when a scope pushed after it is still
    /// open. Closing an already-closed scope stays a no-op.
    /// </exception>
    public IDisposable PushDrawableBrushMaterializer(DrawableBrushMaterializer? materializer)
    {
        VerifyAccess();
        var scope = new DrawableBrushMaterializerScope(
            this,
            parent: s_drawableBrushMaterializer.Value,
            materializer: materializer,
            previous: DrawableBrushMaterializer);
        DrawableBrushMaterializer = materializer;
        s_drawableBrushMaterializer.Value = scope;
        return scope;
    }

    internal static IDisposable ObserveFlushes(Action<ImmediateCanvasFlushKind> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var scope = new FlushObserverScope(s_flushObserver.Value, observer);
        s_flushObserver.Value = scope;
        return scope;
    }

    internal static void RecordFlush(ImmediateCanvasFlushKind kind)
    {
        for (FlushObserverScope? scope = s_flushObserver.Value; scope is not null; scope = scope.Parent)
        {
            try
            {
                scope.Observer(kind);
            }
            catch
            {
                // Test observation must never affect rendering or cleanup.
            }
        }
    }

    private sealed class FlushObserverScope(
        FlushObserverScope? parent,
        Action<ImmediateCanvasFlushKind> observer) : IDisposable
    {
        private bool _disposed;

        public FlushObserverScope? Parent { get; } = parent;

        public Action<ImmediateCanvasFlushKind> Observer { get; } = observer;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (!ReferenceEquals(s_flushObserver.Value, this))
                throw new InvalidOperationException("Immediate-canvas flush observers must be closed in LIFO order.");
            s_flushObserver.Value = Parent;
        }
    }

    private sealed class RenderTargetLeaseSessionScope(
        ImmediateCanvas canvas,
        RenderTargetLeaseSessionScope? parent,
        RenderTargetLeaseSession? leaseSession,
        RenderTargetLeaseSession? previous) : IDisposable
    {
        private ImmediateCanvas? _canvas = canvas;

        public RenderTargetLeaseSessionScope? Parent { get; } = parent;

        public RenderTargetLeaseSession? LeaseSession { get; } = leaseSession;

        public void Dispose()
        {
            ImmediateCanvas? owner = Interlocked.Exchange(ref _canvas, null);
            if (owner is null)
                return;
            if (!ReferenceEquals(s_renderTargetLeaseSession.Value, this))
            {
                throw new InvalidOperationException(
                    "Immediate-canvas render-target lease sessions must be closed in LIFO order.");
            }

            s_renderTargetLeaseSession.Value = Parent;
            if (!owner.IsDisposed)
                owner.RenderTargetLeaseSession = previous;
        }
    }

    private sealed class DrawableBrushMaterializerScope(
        ImmediateCanvas canvas,
        DrawableBrushMaterializerScope? parent,
        DrawableBrushMaterializer? materializer,
        DrawableBrushMaterializer? previous) : IDisposable
    {
        private ImmediateCanvas? _canvas = canvas;

        public DrawableBrushMaterializerScope? Parent { get; } = parent;

        public DrawableBrushMaterializer? Materializer { get; } = materializer;

        public void Dispose()
        {
            ImmediateCanvas? owner = Interlocked.Exchange(ref _canvas, null);
            if (owner is null)
                return;
            if (!ReferenceEquals(s_drawableBrushMaterializer.Value, this))
            {
                throw new InvalidOperationException(
                    "Immediate-canvas drawable-brush materializers must be closed in LIFO order.");
            }

            s_drawableBrushMaterializer.Value = Parent;
            if (!owner.IsDisposed)
                owner.DrawableBrushMaterializer = previous;
        }
    }
}
