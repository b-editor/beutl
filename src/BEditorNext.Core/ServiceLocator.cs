namespace BEditorNext;

// https://github.com/AvaloniaUI/Avalonia/blob/c4bbe3ac5e0becdd7d7ff25eaebb2429b9bba0f3/src/Avalonia.Base/AvaloniaLocator.cs

public sealed class ServiceLocator : IServiceProvider
{
    private readonly Dictionary<Type, Func<object>> _registry = new();
    private readonly ServiceLocator? _parentScope;

    static ServiceLocator()
    {
        Current = CurrentMutable = new ServiceLocator();
    }

    public ServiceLocator()
    {

    }

    public ServiceLocator(ServiceLocator parentScope)
    {
        _parentScope = parentScope;
    }

    public static ServiceLocator Current { get; set; }

    public static ServiceLocator CurrentMutable { get; set; }

    public object? GetService(Type t)
    {
        return _registry.TryGetValue(t, out Func<object>? rv) ? rv() : _parentScope?.GetService(t);
    }

    public RegistrationHelper<T> Bind<T>()
        where T : notnull
    {
        return new(this);
    }

    public ServiceLocator BindToSelf<T>(T constant)
        where T : notnull
    {
        return Bind<T>().ToConstant(constant);
    }

    public ServiceLocator BindToSelfSingleton<T>() where T : class, new()
    {
        return Bind<T>().ToSingleton<T>();
    }

    public static IDisposable EnterScope()
    {
        var d = new ResolverDisposable(Current, CurrentMutable);
        Current = CurrentMutable = new ServiceLocator(Current);
        return d;
    }

    public class RegistrationHelper<TService>
        where TService : notnull
    {
        private readonly ServiceLocator _locator;

        public RegistrationHelper(ServiceLocator locator)
        {
            _locator = locator;
        }

        public ServiceLocator ToConstant<TImpl>(TImpl constant) where TImpl : TService
        {
            _locator._registry[typeof(TService)] = () => constant;
            return _locator;
        }

        public ServiceLocator ToFunc<TImlp>(Func<TImlp> func) where TImlp : TService
        {
            _locator._registry[typeof(TService)] = () => func();
            return _locator;
        }

        public ServiceLocator ToLazy<TImlp>(Func<TImlp> func) where TImlp : TService
        {
            bool constructed = false;
            TImlp? instance = default;
            _locator._registry[typeof(TService)] = () =>
            {
                if (!constructed)
                {
                    instance = func();
                    constructed = true;
                }

                return instance ??= func();
            };
            return _locator;
        }

        public ServiceLocator ToSingleton<TImpl>() where TImpl : class, TService, new()
        {
            TImpl? instance = null;
            return ToFunc(() => (instance ??= new TImpl()));
        }

        public ServiceLocator ToTransient<TImpl>() where TImpl : class, TService, new()
        {
            return ToFunc(() => new TImpl());
        }
    }

    private sealed class ResolverDisposable : IDisposable
    {
        private readonly ServiceLocator _resolver;
        private readonly ServiceLocator _mutable;

        public ResolverDisposable(ServiceLocator resolver, ServiceLocator mutable)
        {
            _resolver = resolver;
            _mutable = mutable;
        }

        public void Dispose()
        {
            Current = _resolver;
            CurrentMutable = _mutable;
        }
    }
}
