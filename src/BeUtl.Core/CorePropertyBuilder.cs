using System.Linq.Expressions;
using System.Reflection;

namespace BeUtl;

public sealed class CorePropertyBuilder<T, TOwner>
{
    private readonly string _name;
    private T? _defaultValue;
    private PropertyObservability _observability;
    private string? _serializeName;
    private PropertyFlags _propertyFlags;
    private Func<TOwner, T>? _getter;
    private Action<TOwner, T>? _setter;

    public CorePropertyBuilder(string name)
    {
        _name = name;
    }

    public CorePropertyBuilder(CorePropertyBuilder<T, TOwner> baseObject)
    {
        _name = baseObject._name;
        _defaultValue = baseObject._defaultValue;
        _observability = baseObject._observability;
        _serializeName = baseObject._serializeName;
        _propertyFlags = baseObject._propertyFlags;
    }

    public CoreProperty<T> Register()
    {
        var metadata = new CorePropertyMetadata<T>
        {
            DefaultValue = _defaultValue,
            DesignerFlags = _propertyFlags,
            Observability = _observability,
            SerializeName = _serializeName,
        };
        CoreProperty<T>? property = null;

        if (_getter != null)
        {
            property = new StaticProperty<TOwner, T>(_name, _getter, _setter, metadata);
        }
        else
        {
            property = new CoreProperty<T>(_name, typeof(TOwner), metadata);
        }
        PropertyRegistry.Register(typeof(TOwner), property);

        return property;
    }

    public StaticProperty<TOwner, T> RegisterStatic()
    {
        return (StaticProperty<TOwner, T>)Register();
    }

    public CorePropertyBuilder<T, TOwner> Accessor(Func<TOwner, T> getter, Action<TOwner, T>? setter)
    {
        _getter = getter;
        _setter = setter;
        return this;
    }
    
    public CorePropertyBuilder<T, TOwner> Accessor(Expression<Func<TOwner, T>> exp)
    {
        _getter = exp.Compile();
        if (exp.Body is MemberExpression memberExp &&
            memberExp.Member is PropertyInfo propInfo &&
            propInfo.SetMethod != null)
        {
            ParameterExpression ownerParam = Expression.Parameter(typeof(TOwner), "o");
            ParameterExpression valueParam = Expression.Parameter(typeof(T), "v");
            MemberExpression? memberAccess = Expression.MakeMemberAccess(ownerParam, propInfo);
            BinaryExpression? assign = Expression.Assign(memberAccess, valueParam);
            Expression<Action<TOwner, T>> lambda1 = Expression.Lambda<Action<TOwner, T>>(assign, new[] { ownerParam, valueParam });
            _setter = lambda1.Compile();
        }

        return this;
    }

    public CorePropertyBuilder<T, TOwner> DefaultValue(T? value)
    {
        _defaultValue = value;
        return this;
    }

    public CorePropertyBuilder<T, TOwner> Observability(PropertyObservability value)
    {
        _observability = value;
        return this;
    }

    public CorePropertyBuilder<T, TOwner> SerializeName(string? value)
    {
        _serializeName = value;
        return this;
    }

    public CorePropertyBuilder<T, TOwner> PropertyFlags(PropertyFlags value)
    {
        _propertyFlags = value;
        return this;
    }
}
