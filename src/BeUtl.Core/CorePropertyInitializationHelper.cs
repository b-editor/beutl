namespace BeUtl;

public interface IPropertyInitializationHelper
{
    void SetValue(string key, object value);
}

public class CorePropertyInitializationHelper<T, TOwner> : IPropertyInitializationHelper
{
    protected Dictionary<string, object> _options = new();
    protected T? _defaultValue;
    protected PropertyObservability _observability;
    protected readonly string _name;

    public CorePropertyInitializationHelper(string name)
    {
        _name = name;
    }

    public CorePropertyInitializationHelper(CorePropertyInitializationHelper<T, TOwner> baseObject)
    {
        _name = baseObject._name;
        _options = baseObject._options;
        _defaultValue = baseObject._defaultValue;
    }

    public CorePropertyInitializationHelper<T, TOwner> DefaultValue(T? defaultValue)
    {
        _defaultValue = defaultValue;
        return this;
    }

    public CorePropertyInitializationHelper<T, TOwner> Observability(PropertyObservability observability)
    {
        _observability = observability;
        return this;
    }

    public CorePropertyInitializationHelper<T, TOwner> SetValue(string key, object value)
    {
        _options[key] = value;
        return this;
    }

    public StaticPropertyInitializationHelper<T, TOwner> Accessor(Func<TOwner, T> getter, Action<TOwner, T>? setter = null)
    {
        return new StaticPropertyInitializationHelper<T, TOwner>(this, getter, setter);
    }

    public CoreProperty<T> Register()
    {
        return OnRegister();
    }

    protected virtual CoreProperty<T> OnRegister()
    {
        var metadata = new CorePropertyMetadata(_defaultValue, _observability, _options);
        var property = new CoreProperty<T>(_name, typeof(TOwner), metadata);
        PropertyRegistry.Register(typeof(TOwner), property);

        return property;
    }

    void IPropertyInitializationHelper.SetValue(string key, object value)
    {
        _options[key] = value;
    }
}

public class StaticPropertyInitializationHelper<T, TOwner> : CorePropertyInitializationHelper<T, TOwner>
{
    protected Func<TOwner, T> _getter;
    protected Action<TOwner, T>? _setter;

    public StaticPropertyInitializationHelper(string name, Func<TOwner, T> getter, Action<TOwner, T>? setter = null)
        : base(name)
    {
        _getter = getter;
        _setter = setter;
    }

    public StaticPropertyInitializationHelper(CorePropertyInitializationHelper<T, TOwner> baseObject, Func<TOwner, T> getter, Action<TOwner, T>? setter = null)
        : base(baseObject)
    {
        _getter = getter;
        _setter = setter;
    }

    public new StaticProperty<TOwner, T> Register()
    {
        return (StaticProperty<TOwner, T>)OnRegister();
    }

    protected override CoreProperty<T> OnRegister()
    {
        var metadata = new CorePropertyMetadata(_defaultValue, _observability, _options);
        var property = new StaticProperty<TOwner, T>(_name, _getter, _setter, metadata);
        PropertyRegistry.Register(typeof(TOwner), property);

        return property;
    }
}
