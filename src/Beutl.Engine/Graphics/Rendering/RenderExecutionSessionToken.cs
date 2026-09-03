using System.Runtime.ExceptionServices;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

internal sealed class RenderExecutionSessionToken
{
    private readonly Dictionary<object, int> _authorizedResources = new(ReferenceEqualityComparer.Instance);
    private readonly DrawableBrushMaterializer? _drawableBrushMaterializer;
    private RenderExecutionCallbackGuard.Scope _callbackGuard = RenderExecutionCallbackGuard.Enter();
    private bool _active = true;
    private ImmediateCanvas? _activeCanvas;
    private RenderCallbackCanvas? _activeFacade;
    private DrawableBrushMaterializer? _previousDrawableBrushMaterializer;

    public RenderExecutionSessionToken(DrawableBrushMaterializer? drawableBrushMaterializer = null)
    {
        _drawableBrushMaterializer = drawableBrushMaterializer;
    }

    public void ThrowIfInactive()
    {
        if (!_active)
            throw new InvalidOperationException("The render execution callback has completed.");
    }

    public void Complete()
    {
        ThrowIfInactive();
        bool hasActiveCanvas = _activeCanvas is not null;
        RestoreDrawableBrushMaterializer();
        _active = false;
        _activeCanvas = null;
        _activeFacade = null;
        _authorizedResources.Clear();
        _callbackGuard.Dispose();
        if (hasActiveCanvas)
            throw new InvalidOperationException("An execution canvas is still active.");
    }

    public void RunAndComplete(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        RunAndComplete(
            () =>
            {
                action();
                return true;
            });
    }

    public T RunAndComplete<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ExceptionDispatchInfo? primaryFailure = null;
        T result = default!;
        try
        {
            result = action();
        }
        catch (Exception ex)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(ex);
        }
        finally
        {
            try
            {
                Complete();
            }
            catch when (primaryFailure is not null)
            {
                // The callback failure remains primary; session cleanup is best-effort on this path.
            }
        }

        primaryFailure?.Throw();
        return result;
    }

    public void EnterCanvas(ImmediateCanvas canvas, RenderCallbackCanvas? facade)
    {
        ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(canvas);
        if (_activeCanvas is not null)
            throw new InvalidOperationException("Only one callback canvas may be active in an execution session.");

        _activeCanvas = canvas;
        _activeFacade = facade;
        _previousDrawableBrushMaterializer = canvas.DrawableBrushMaterializer;
        if (_drawableBrushMaterializer is not null)
            canvas.DrawableBrushMaterializer = _drawableBrushMaterializer;
    }

    public void UseRawCanvas(ImmediateCanvas canvas, Action<ImmediateCanvas> use)
    {
        ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(use);
        EnterCanvas(canvas, facade: null);
        try
        {
            canvas.ConfigureRawExecutionCallback(this);
            use(canvas);
        }
        finally
        {
            try
            {
                canvas.CloseWithoutFlush();
            }
            finally
            {
                ExitCanvas(canvas);
            }
        }
    }

    public void ExitCanvas(ImmediateCanvas canvas)
    {
        if (!ReferenceEquals(_activeCanvas, canvas))
            throw new InvalidOperationException("The supplied canvas is not the active execution canvas.");

        RestoreDrawableBrushMaterializer();
        _activeCanvas = null;
        _activeFacade = null;
    }

    public bool IsActiveCanvas(ImmediateCanvas canvas)
        => _active && ReferenceEquals(_activeCanvas, canvas);

    public ImmediateCanvas GetActiveCanvas(RenderCallbackCanvas facade)
    {
        ThrowIfInactive();
        if (_activeCanvas is null || !ReferenceEquals(_activeFacade, facade))
        {
            throw new InvalidOperationException(
                "The operation must run while this callback canvas facade is active.");
        }

        return _activeCanvas;
    }

    public void VerifyActiveCanvas(ImmediateCanvas canvas)
    {
        ThrowIfInactive();
        if (!ReferenceEquals(_activeCanvas, canvas) || _activeFacade is null)
        {
            throw new InvalidOperationException(
                "An execution input may be drawn only on the active same-session callback canvas.");
        }
    }

    public PixelPoint GetActiveCanvasDeviceOrigin(ImmediateCanvas canvas)
    {
        VerifyActiveCanvas(canvas);
        return _activeFacade!.DeviceOriginUnchecked;
    }

    public void AuthorizeResource(object resource, Action use)
    {
        ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(use);

        _authorizedResources.TryGetValue(resource, out int count);
        _authorizedResources[resource] = count + 1;
        try
        {
            use();
        }
        finally
        {
            if (count == 0)
                _authorizedResources.Remove(resource);
            else
                _authorizedResources[resource] = count;
        }
    }

    public void UseResource<T>(
        RenderResource<T> resource,
        IReadOnlyList<RenderResourceBinding> declaredResources,
        Action<T> use)
        where T : class
    {
        ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(declaredResources);
        ArgumentNullException.ThrowIfNull(use);
        for (int index = 0; index < declaredResources.Count; index++)
        {
            if (ReferenceEquals(declaredResources[index].Resource.SlotIdentity, resource.SlotIdentity))
            {
                UseResourceCore(resource, use);
                return;
            }
        }

        throw new InvalidOperationException("The render resource was not declared by this operation.");
    }

    public void UseResources(
        IReadOnlyList<RenderResource> resources,
        Action use)
    {
        ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(use);
        UseResourceAt(0);

        void UseResourceAt(int index)
        {
            if (index == resources.Count)
            {
                use();
                return;
            }

            RenderResource resource = resources[index];
            resource.Registry.UseUntyped(
                resource,
                value =>
                {
                    AuthorizeResource(value, () => UseResourceAt(index + 1));
                    return true;
                });
        }
    }

    public void UseResource<T>(
        RenderResourceSlot<T> slot,
        IReadOnlyList<RenderResourceBinding> declaredResources,
        Action<T> use)
        where T : class
    {
        ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(declaredResources);
        ArgumentNullException.ThrowIfNull(use);
        for (int index = 0; index < declaredResources.Count; index++)
        {
            RenderResourceBinding binding = declaredResources[index];
            if (!ReferenceEquals(binding.Slot, slot))
                continue;

            if (binding.Resource is not RenderResource<T> resource)
            {
                throw new InvalidOperationException(
                    "The resource bound to the requested slot does not match the slot's declared type.");
            }

            UseResourceCore(resource, use);
            return;
        }

        throw new KeyNotFoundException(
            "No resource was bound to the requested slot for this execution callback.");
    }

    public bool IsResourceAuthorized(object resource)
        => _active && _authorizedResources.ContainsKey(resource);

    private void UseResourceCore<T>(RenderResource<T> resource, Action<T> use)
        where T : class
    {
        resource.Registry.Use(
            resource,
            value =>
            {
                AuthorizeResource(value, () => use(value));
                return true;
            });
    }

    private void RestoreDrawableBrushMaterializer()
    {
        if (_activeCanvas is { IsDisposed: false } canvas)
            canvas.DrawableBrushMaterializer = _previousDrawableBrushMaterializer;
        _previousDrawableBrushMaterializer = null;
    }

}
