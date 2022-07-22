using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Serialization;

using BeUtl.Validation;

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

    public CorePropertyBuilder<T, TOwner> Range<TValidator>(T min, T max, bool merge = false)
        where TValidator : RangeValidator<T>, new()
    {
        IValidator<T> validator = new TValidator
        {
            Maximum = max,
            Minimum = min
        };

        if (merge && _metadata.Validator != null)
        {
            validator = new LinkedValidator(_metadata.Validator, validator);
        }
        _metadata = _metadata with
        {
            Validator = validator
        };
        return this;
    }

    public CorePropertyBuilder<T, TOwner> Range(T min, T max, bool merge = false)
    {
        if (Activator.CreateInstance(RangeValidationService.Instance.Get<T>()) is RangeValidator<T> validator1)
        {
            validator1.Minimum = min;
            validator1.Maximum = max;

            IValidator<T> validator2 = validator1;
            if (merge && _metadata.Validator != null)
            {
                validator2 = new LinkedValidator(_metadata.Validator, validator1);
            }
            _metadata = _metadata with
            {
                Validator = validator2
            };
        }

        return this;
    }

    public CorePropertyBuilder<T, TOwner> Validator(IValidator<T> validator, bool merge = false)
    {
        if (merge && _metadata.Validator != null)
        {
            validator = new LinkedValidator(_metadata.Validator, validator);
        }
        _metadata = _metadata with
        {
            Validator = validator
        };

        return this;
    }

    public CorePropertyBuilder<T, TOwner> Validator(
        Func<ICoreObject, T, bool>? validate = null,
        Func<ICoreObject, T, T>? coerce = null,
        bool merge = false)
    {
        IValidator<T> validator1 = new FuncValidator
        {
            ValidateFunc = validate,
            CoerceFunc = coerce,
        };
        if (merge && _metadata.Validator != null)
        {
            validator1 = new LinkedValidator(_metadata.Validator, validator1);
        }
        _metadata = _metadata with
        {
            Validator = validator1
        };

        return this;
    }

    public CorePropertyBuilder<T, TOwner> JsonConverter(JsonConverter<T> jsonConverter)
    {
        _metadata = _metadata with
        {
            JsonConverter = jsonConverter
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

    private sealed class FuncValidator : IValidator<T>
    {
        public Func<ICoreObject, T, T>? CoerceFunc { get; set; }

        public Func<ICoreObject, T, bool>? ValidateFunc { get; set; }

        public T Coerce(ICoreObject obj, T value)
        {
            if (CoerceFunc != null)
            {
                return CoerceFunc(obj, value);
            }
            else
            {
                return value;
            }
        }

        public bool Validate(ICoreObject obj, T value)
        {
            return ValidateFunc?.Invoke(obj, value) ?? true;
        }
    }

    private sealed class LinkedValidator : IValidator<T>
    {
        public LinkedValidator(IValidator<T> first, IValidator<T> second)
        {
            First = first;
            Second = second;
        }

        public IValidator<T> First { get; }

        public IValidator<T> Second { get; }

        public T Coerce(ICoreObject obj, T value)
        {
            return Second.Coerce(obj, First.Coerce(obj, value));
        }

        public bool Validate(ICoreObject obj, T value)
        {
            return First.Validate(obj, value) && Second.Validate(obj, value);
        }
    }
}
