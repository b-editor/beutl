using Beutl.Graphics.Rendering.Requests;

namespace Beutl.Graphics;

public partial class ImmediateCanvas
{
    internal IDisposable PushRenderTargetLeaseSession(RenderTargetLeaseSession? leaseSession)
    {
        return PushAmbientScope(
            s_renderTargetLeaseSession,
            leaseSession,
            RenderTargetLeaseSession,
            static (canvas, value) => canvas.RenderTargetLeaseSession = value,
            "Immediate-canvas render-target lease sessions must be closed in LIFO order.");
    }

    /// <summary>
    /// Installs <paramref name="materializer"/> for the returned scope's lifetime, restoring the previous hook
    /// on dispose.
    /// </summary>
    /// <remarks>
    /// Close scopes in LIFO order, as <see langword="using"/> does. Closing one while a later scope is open throws
    /// <see cref="InvalidOperationException"/>, since every canvas built afterwards would inherit its hook.
    /// </remarks>
    public IDisposable PushDrawableBrushMaterializer(DrawableBrushMaterializer? materializer)
    {
        return PushAmbientScope(
            s_drawableBrushMaterializer,
            materializer,
            DrawableBrushMaterializer,
            static (canvas, value) => canvas.DrawableBrushMaterializer = value,
            "Immediate-canvas drawable-brush materializers must be closed in LIFO order.");
    }

    private AmbientCanvasScope<T> PushAmbientScope<T>(
        AsyncLocal<AmbientCanvasScope<T>?> slot,
        T? value,
        T? previous,
        Action<ImmediateCanvas, T?> set,
        string orderViolationMessage)
        where T : class
    {
        VerifyAccess();
        var scope = new AmbientCanvasScope<T>(this, slot, slot.Value, value, previous, set, orderViolationMessage);
        set(this, value);
        slot.Value = scope;
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

    /// <summary>A value installed on a canvas, and seeding the canvases built, while the scope is open.</summary>
    private sealed class AmbientCanvasScope<T>(
        ImmediateCanvas canvas,
        AsyncLocal<AmbientCanvasScope<T>?> slot,
        AmbientCanvasScope<T>? parent,
        T? value,
        T? previous,
        Action<ImmediateCanvas, T?> restore,
        string orderViolationMessage) : IDisposable
        where T : class
    {
        private ImmediateCanvas? _canvas = canvas;

        public AmbientCanvasScope<T>? Parent { get; } = parent;

        public T? Value { get; } = value;

        public void Dispose()
        {
            ImmediateCanvas? owner = Interlocked.Exchange(ref _canvas, null);
            if (owner is null)
                return;
            if (!ReferenceEquals(slot.Value, this))
                throw new InvalidOperationException(orderViolationMessage);

            slot.Value = Parent;
            if (!owner.IsDisposed)
                restore(owner, previous);
        }
    }
}
