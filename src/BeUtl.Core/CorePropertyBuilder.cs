using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Serialization;

using BeUtl.Validation;

namespace BeUtl;

public sealed class CorePropertyBuilder<T, TOwner>
{
    private readonly string _name;
    private Func<TOwner, T>? _getter;
    private Action<TOwner, T>? _setter;
    private string? _serializeName;
    private Optional<PropertyFlags> _propertyFlags;
    private Optional<T> _defaultValue;
    private IValidator<T>? _validator;
    private JsonConverter<T>? _jsonConverter;

    public CorePropertyBuilder(string name)
    {
        _name = name;
        Raw = new RawBuilder(this);
    }

    public RawBuilder Raw { get; }

    public CoreProperty<T> Register()
    {
        CoreProperty<T>? property = null;

        var metadata = new CorePropertyMetadata<T>(_serializeName, _propertyFlags, _defaultValue, _validator, _jsonConverter);
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

    public CorePropertyBuilder<T, TOwner> DefaultValue(T value)
    {
        _defaultValue = new Optional<T>(value);
        return this;
    }

    public CorePropertyBuilder<T, TOwner> SerializeName(string? value)
    {
        _serializeName = value;
        return this;
    }

    private static IValidator<T> MergeValidator(IValidator<T> oldValidator, IValidator<T> newValidator)
    {
        if (oldValidator is TuppleValidator<T> tupple)
        {
            newValidator = new MultipleValidator<T>(new IValidator<T>[]
            {
                tupple.First,
                tupple.Second,
                newValidator
            });
        }
        else if (oldValidator is MultipleValidator<T> multiple)
        {
            int length = multiple.Items.Length;
            var array = new IValidator<T>[length + 1];
            multiple.Items.AsSpan().CopyTo(array.AsSpan().Slice(0, length));

            array[^1] = newValidator;

            newValidator = new MultipleValidator<T>(array);
        }
        else
        {
            newValidator = new TuppleValidator<T>(oldValidator, newValidator);
        }

        return newValidator;
    }

    public CorePropertyBuilder<T, TOwner> Range<TValidator>(T min, T max, bool merge = false)
        where TValidator : RangeValidator<T>, new()
    {
        IValidator<T> validator = new TValidator
        {
            Maximum = max,
            Minimum = min
        };

        if (merge && _validator != null)
        {
            validator = MergeValidator(_validator, validator);
        }

        _validator = validator;
        return this;
    }

    public CorePropertyBuilder<T, TOwner> Range(T min, T max, bool merge = false)
    {
        if (Activator.CreateInstance(RangeValidationService.Instance.Get<T>()) is RangeValidator<T> validator1)
        {
            validator1.Minimum = min;
            validator1.Maximum = max;

            IValidator<T> validator2 = validator1;
            if (merge && _validator != null)
            {
                validator2 = MergeValidator(_validator, validator1);
            }

            _validator = validator2;
        }

        return this;
    }

    public CorePropertyBuilder<T, TOwner> Minimum<TValidator>(T min, bool merge = false)
        where TValidator : RangeValidator<T>, new()
    {
        IValidator<T> validator = new TValidator
        {
            Minimum = min
        };

        if (merge && _validator != null)
        {
            validator = MergeValidator(_validator, validator);
        }
        _validator = validator;
        return this;
    }

    public CorePropertyBuilder<T, TOwner> Minimum(T min, bool merge = false)
    {
        if (Activator.CreateInstance(RangeValidationService.Instance.Get<T>()) is RangeValidator<T> validator1)
        {
            validator1.Minimum = min;

            IValidator<T> validator2 = validator1;
            if (merge && _validator != null)
            {
                validator2 = MergeValidator(_validator, validator1);
            }
            _validator = validator2;
        }

        return this;
    }

    public CorePropertyBuilder<T, TOwner> Maximum<TValidator>(T max, bool merge = false)
        where TValidator : RangeValidator<T>, new()
    {
        IValidator<T> validator = new TValidator
        {
            Maximum = max
        };

        if (merge && _validator != null)
        {
            validator = MergeValidator(_validator, validator);
        }
        _validator = validator;
        return this;
    }

    public CorePropertyBuilder<T, TOwner> Maximum(T max, bool merge = false)
    {
        if (Activator.CreateInstance(RangeValidationService.Instance.Get<T>()) is RangeValidator<T> validator1)
        {
            validator1.Maximum = max;

            IValidator<T> validator2 = validator1;
            if (merge && _validator != null)
            {
                validator2 = MergeValidator(_validator, validator1);
            }
            _validator = validator2;
        }

        return this;
    }

    public CorePropertyBuilder<T, TOwner> Validator(IValidator<T> validator, bool merge = false)
    {
        if (merge && _validator != null)
        {
            validator = MergeValidator(_validator, validator);
        }
        _validator = validator;

        return this;
    }

    public CorePropertyBuilder<T, TOwner> Validator(
        Func<ICoreObject?, T?, bool>? validate = null,
        Func<ICoreObject?, T?, T?>? coerce = null,
        bool merge = false)
    {
        IValidator<T> validator1 = new FuncValidator<T>
        {
            ValidateFunc = validate,
            CoerceFunc = coerce,
        };
        if (merge && _validator != null)
        {
            validator1 = MergeValidator(_validator, validator1);
        }
        _validator = validator1;

        return this;
    }

    public CorePropertyBuilder<T, TOwner> JsonConverter(JsonConverter<T> jsonConverter)
    {
        _jsonConverter = jsonConverter;
        return this;
    }

    public CorePropertyBuilder<T, TOwner> PropertyFlags(PropertyFlags value, bool merge = false)
    {
        if (merge)
        {
            _propertyFlags = _propertyFlags.GetValueOrDefault() | value;
        }
        else
        {
            _propertyFlags = value;
        }

        return this;
    }

    public sealed class RawBuilder
    {
        private readonly CorePropertyBuilder<T, TOwner> _builder;

        internal RawBuilder(CorePropertyBuilder<T, TOwner> builder)
        {
            _builder = builder;
        }

        public CorePropertyBuilder<T, TOwner> DefaultValue(Optional<T> value)
        {
            _builder._defaultValue = value;
            return _builder;
        }

        public CorePropertyBuilder<T, TOwner> PropertyFlags(Optional<PropertyFlags> value)
        {
            _builder._propertyFlags = value;
            return _builder;
        }
    }
}
