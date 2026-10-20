namespace Beutl.Editor.Services;

// An editor's services, found by their exact type. Lookups are read on each request; owned services are
// created on first request and disposed with the registry.
internal sealed class EditorServiceRegistry : IDisposable
{
    private readonly Dictionary<Type, Func<object?>> _services = [];
    private readonly List<Lazy<object>> _owned = [];

    public IReadOnlyCollection<Type> Types => _services.Keys;

    public EditorServiceRegistry Add<T>(Func<T?> lookup)
        where T : class
    {
        _services.Add(typeof(T), lookup);
        return this;
    }

    public EditorServiceRegistry AddOwned<T>(Func<T> create)
        where T : class
    {
        var service = new Lazy<object>(create);
        _owned.Add(service);
        _services.Add(typeof(T), () => service.Value);
        return this;
    }

    public object? GetService(Type serviceType)
        => _services.TryGetValue(serviceType, out Func<object?>? lookup) ? lookup() : null;

    public void Dispose()
    {
        foreach (Lazy<object> service in _owned)
        {
            if (service.IsValueCreated && service.Value is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
