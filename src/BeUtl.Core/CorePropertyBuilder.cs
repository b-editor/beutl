using System.Linq.Expressions;
using System.Reflection;

namespace BeUtl;

public interface ICorePropertyBuilder<T>
{
    void OverrideMetadata(CorePropertyMetadata<T> metadata);
}

public sealed class CorePropertyBuilder<T, TOwner> : ICorePropertyBuilder<T>
{
    private readonly string _name;
    private Func<TOwner, T>? _getter;
    private Action<TOwner, T>? _setter;
    private CorePropertyMetadata<T> _metadata = new();

    public CorePropertyBuilder(string name)
    {
        _name = name;
    }

    public CorePropertyBuilder(CorePropertyBuilder<T, TOwner> baseObject)
    {
        _name = baseObject._name;
        //_defaultValue = baseObject._defaultValue;
        //_observability = baseObject._observability;
        //_serializeName = baseObject._serializeName;
        //_propertyFlags = baseObject._propertyFlags;
    }

    public CoreProperty<T> Register()
    {
        CoreProperty<T>? property = null;

        if (_getter != null)
        {
            property = new StaticProperty<TOwner, T>(_name, _getter, _setter, _metadata);
        }
        else
        {
            property = new CoreProperty<T>(_name, typeof(TOwner), _metadata);
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
        _metadata = _metadata with
        {
            DefaultValue = value
        };
        return this;
    }

    public CorePropertyBuilder<T, TOwner> Observability(PropertyObservability value)
    {
        _metadata = _metadata with
        {
            Observability = value
        };
        return this;
    }

    public CorePropertyBuilder<T, TOwner> SerializeName(string? value)
    {
        _metadata = _metadata with
        {
            SerializeName = value
        };
        return this;
    }

    public CorePropertyBuilder<T, TOwner> PropertyFlags(PropertyFlags value)
    {
        _metadata = _metadata with
        {
            PropertyFlags = value
        };
        return this;
    }

    public CorePropertyBuilder<T, TOwner> OverrideMetadata(CorePropertyMetadata<T> metadata)
    {
        if (_metadata != null)
        {
            metadata.Merge(_metadata, null);
        }
        _metadata = metadata;
        return this;
    }

    void ICorePropertyBuilder<T>.OverrideMetadata(CorePropertyMetadata<T> metadata)
    {
        OverrideMetadata(metadata);
    }
}
