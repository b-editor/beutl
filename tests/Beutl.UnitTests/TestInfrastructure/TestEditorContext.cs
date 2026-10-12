using Beutl.Extensibility;

using Reactive.Bindings;

namespace Beutl.UnitTests.TestInfrastructure;

public sealed class TestEditorContext(CoreObject obj) : IEditorContext
{
    private readonly Dictionary<Type, object> _services = [];

    public CoreObject Object { get; } = obj;

    public EditorExtension Extension => null!;

    public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

    public void AddService<T>(T service)
        where T : notnull
    {
        _services[typeof(T)] = service;
    }

    public object? GetService(Type serviceType)
    {
        return _services.GetValueOrDefault(serviceType);
    }

    public T? FindToolTab<T>(Func<T, bool> condition)
        where T : IToolContext
    {
        return default;
    }

    public T? FindToolTab<T>()
        where T : IToolContext
    {
        return default;
    }

    public bool OpenToolTab(IToolContext item)
    {
        return false;
    }

    public void CloseToolTab(IToolContext item)
    {
    }
}
