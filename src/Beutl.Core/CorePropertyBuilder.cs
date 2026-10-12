using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Beutl.Serialization;

namespace Beutl;

public sealed class CorePropertyBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] T, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TOwner>
{
    private readonly string _name;
    private Func<TOwner, T>? _getter;
    private Action<TOwner, T>? _setter;
    private Optional<T> _defaultValue;
    private readonly PropertyInfo? _propertyInfo;
    private Attribute[] _attributes;
    private List<string>? _formerNames;

    public CorePropertyBuilder(string name, bool isAttached = false)
    {
        _name = name;

        if (!isAttached)
        {
            _propertyInfo = typeof(TOwner).GetProperty(name) ?? throw new InvalidOperationException();
            _attributes = _propertyInfo.GetCustomAttributes().ToArray();
        }
        else
        {
            _attributes = [];
        }
    }

    public CorePropertyBuilder(Expression<Func<TOwner, T>> exp)
    {
        _getter = exp.Compile();
        if (exp.Body is MemberExpression memberExp &&
            memberExp.Member is PropertyInfo propInfo &&
            propInfo.SetMethod != null)
        {
            _propertyInfo = propInfo;
            _name = propInfo.Name;

            ParameterExpression ownerParam = Expression.Parameter(typeof(TOwner), "o");
            ParameterExpression valueParam = Expression.Parameter(typeof(T), "v");
            MemberExpression? memberAccess = Expression.MakeMemberAccess(ownerParam, propInfo);
            BinaryExpression? assign = Expression.Assign(memberAccess, valueParam);
            Expression<Action<TOwner, T>> lambda1 = Expression.Lambda<Action<TOwner, T>>(assign, [ownerParam, valueParam]);
            _setter = lambda1.Compile();

            _attributes = _propertyInfo.GetCustomAttributes().ToArray();
        }
        else
        {
            throw new InvalidOperationException();
        }
    }

    public CoreProperty<T> Register()
    {
        CoreProperty<T>? property = null;

        // Kept apart until now so that SetAttribute, which replaces the attributes, does not drop them.
        Attribute[] attributes = _formerNames == null
            ? _attributes
            : [.. _attributes, .. _formerNames.Select(name => new FormerPersistedNameAttribute(name))];
        var metadata = new CorePropertyMetadata<T>(_defaultValue, _getter == null || _setter != null, attributes);
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

    public CorePropertyBuilder<T, TOwner> Accessor(Func<TOwner, T> getter, Action<TOwner, T>? setter = null)
    {
        _getter = getter;
        _setter = setter;
        return this;
    }

    public CorePropertyBuilder<T, TOwner> DefaultValue(T value)
    {
        _defaultValue = new Optional<T>(value);
        return this;
    }

    public CorePropertyBuilder<T, TOwner> SetAttribute(params Attribute[] attributes)
    {
        _attributes = attributes;
        return this;
    }

    /// <summary>
    /// Adds a member key the property was saved under before it was renamed. A value found only under that key
    /// loads into the property; the property is always written under its current name.
    /// </summary>
    /// <remarks>
    /// This has the effect of a <see cref="FormerPersistedNameAttribute"/> on the property, and can be called once
    /// for each former name. Writing a renamed member is a breaking change of the format, because a build older
    /// than the rename does not load it.
    /// </remarks>
    /// <param name="name">The former member key.</param>
    public CorePropertyBuilder<T, TOwner> FormerPersistedName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        (_formerNames ??= []).Add(name);
        return this;
    }
}
