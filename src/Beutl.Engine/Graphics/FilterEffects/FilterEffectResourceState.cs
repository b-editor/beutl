using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Effects;

internal sealed class FilterEffectResourceState
{
    private readonly RenderNodeContext? _renderContext;
    private RenderRequestResourceRegistry? _standaloneRegistry;
    private List<RenderResource>? _resources;
    private int _references = 1;
    private bool _transferred;

    public FilterEffectResourceState(RenderNodeContext? renderContext)
    {
        _renderContext = renderContext;
    }

    public int Count => _resources?.Count ?? 0;

    public FilterEffectResourceState AddReference()
    {
        if (_references <= 0)
            throw new ObjectDisposedException(nameof(FilterEffectResourceState));
        _references++;
        return this;
    }

    public RenderResource<T> Own<T>(T resource)
        where T : class, IDisposable
    {
        ThrowIfTransferred();
        RenderResource<T> token = _renderContext is not null
            ? _renderContext.Own(resource)
            : (_standaloneRegistry ??= new RenderRequestResourceRegistry()).RegisterOwned(resource);
        (_resources ??= []).Add(token);
        return token;
    }

    public RenderResource<T> Borrow<T>(T resource)
        where T : class
    {
        ThrowIfTransferred();
        RenderResource<T> token = _renderContext is not null
            ? _renderContext.Borrow(resource)
            : (_standaloneRegistry ??= new RenderRequestResourceRegistry()).RegisterBorrowed(resource);
        (_resources ??= []).Add(token);
        return token;
    }

    /// <remarks>
    /// Shader and geometry stages declare their bindings as different types, so the resource is read through a
    /// selector rather than by projecting each list into a common one.
    /// </remarks>
    public void ValidateResources<TBinding>(
        IReadOnlyList<TBinding> bindings,
        Func<TBinding, RenderResource> selectResource,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(selectResource);
        for (int index = 0; index < bindings.Count; index++)
        {
            RenderResource resource = selectResource(bindings[index]);
            if (!IsRegistered(resource)
                || resource.RegistrationState == RenderResourceRegistrationState.Released)
            {
                throw new ArgumentException(
                    "Every declared resource must be registered by this FilterEffectContext family.",
                    parameterName);
            }
        }
    }

    private bool IsRegistered(RenderResource resource)
    {
        if (_resources is null)
            return false;
        for (int index = 0; index < _resources.Count; index++)
        {
            if (ReferenceEquals(_resources[index], resource))
                return true;
        }

        return false;
    }

    public void RollbackTo(int count, Exception? primaryFailure = null)
    {
        int resourceCount = Count;
        if (count < 0 || count > resourceCount)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count == resourceCount)
            return;

        List<RenderResource> resources = _resources!;
        RenderResource[] removed = resources.Skip(count).ToArray();
        resources.RemoveRange(count, resourceCount - count);
        Rollback(removed, primaryFailure);
    }

    private void Rollback(RenderResource[] removed, Exception? primaryFailure)
    {
        if (_renderContext is not null)
        {
            if (primaryFailure is null)
                _renderContext.RollbackResources(removed);
            else
            {
                Exception? cleanupFailure =
                    _renderContext.RollbackResourcesAndCapture(removed, primaryFailure);
                if (cleanupFailure is not null)
                    throw cleanupFailure;
            }

            return;
        }

        List<Exception>? failures = null;
        for (int index = removed.Length - 1; index >= 0; index--)
        {
            try
            {
                _standaloneRegistry!.Rollback(removed[index]);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is not null)
            throw new AggregateException("Filter-effect resource rollback failed.", failures);
    }

    public void Transfer()
    {
        ThrowIfTransferred();
        _transferred = true;
    }

    public void CommitStandaloneResources()
    {
        if (_standaloneRegistry is null || _resources is null)
            return;

        foreach (RenderResource resource in _resources)
        {
            if (resource.RegistrationState == RenderResourceRegistrationState.Pending)
                _standaloneRegistry.Commit(resource);
        }
    }

    public void ReleaseReference()
    {
        if (_references <= 0)
            return;
        _references--;
        if (_references != 0)
            return;

        if (_standaloneRegistry is not null)
        {
            _standaloneRegistry.Dispose();
            return;
        }

        if (!_transferred)
            RollbackTo(0);
    }

    private void ThrowIfTransferred()
    {
        if (_transferred)
            throw new InvalidOperationException("Filter-effect resources were already transferred to the render request.");
    }
}
