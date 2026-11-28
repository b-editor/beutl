namespace Beutl.Editor.Components.AiWorkspaceTab.ViewModels;

/// <summary>
/// An AI page that keeps a resource for exactly as long as its own work runs: until its disposal has drained every
/// operation the page started, whatever its flags still read by then.
/// </summary>
internal interface IAiPageLifetime
{
    /// <summary>
    /// Disposes <paramref name="resource"/> once the page's disposal has drained its operations, or at once if it
    /// already has.
    /// </summary>
    void ReleaseWhenDrained(IDisposable resource);
}

/// <summary>The resources an <see cref="IAiPageLifetime"/> releases once its operations have drained.</summary>
internal sealed class AiPageDrainedResources : IDisposable
{
    private readonly object _sync = new();
    private List<IDisposable>? _resources = [];

    public void Add(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_sync)
        {
            if (_resources is not null)
            {
                _resources.Add(resource);
                return;
            }
        }

        resource.Dispose();
    }

    public void Dispose()
    {
        List<IDisposable>? resources;
        lock (_sync)
        {
            resources = _resources;
            _resources = null;
        }

        if (resources is null)
            return;

        List<Exception>? failures = null;
        foreach (IDisposable resource in resources)
        {
            try
            {
                resource.Dispose();
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is not null)
            throw new AggregateException(failures);
    }
}
