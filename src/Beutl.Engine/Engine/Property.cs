using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Beutl.Animation;
using Beutl.Media;
using Beutl.Validation;

namespace Beutl.Engine;

public static class Property
{
    public static IProperty<T> CreateAnimatable<T>(
        T defaultValue = default(T)!,
        IValidator<T>? validator = null)
    {
        var property = new AnimatableProperty<T>(defaultValue, validator);

        return property;
    }

    public static IProperty<T> CreateAnimatable<T>(
        T defaultValue,
        params ValidationAttribute[] validationAttributes)
    {
        var validator = validationAttributes.Length > 0
            ? new MultipleValidator<T>(validationAttributes
                .Select(CorePropertyMetadata<T>.ConvertValidator)
                .ToArray())
            : null;

        return CreateAnimatable(defaultValue, validator);
    }

    public static IProperty<T> Create<T>(
        T defaultValue = default(T)!,
        IValidator<T>? validator = null)
    {
        var property = new SimpleProperty<T>(defaultValue, validator);

        return property;
    }

    public static IProperty<T> Create<T>(
        T defaultValue,
        params ValidationAttribute[] validationAttributes)
    {
        var validator = validationAttributes.Length > 0
            ? new MultipleValidator<T>(validationAttributes
                .Select(CorePropertyMetadata<T>.ConvertValidator)
                .ToArray())
            : null;

        return Create(defaultValue, validator);
    }

    public static IProperty<TOutput> Converted<TInput, TOutput>(
        this IProperty<TInput> source,
        Func<TInput, TOutput> convert,
        Func<TOutput, TInput>? convertBack = null)
    {
        return new ConvertedProperty<TInput, TOutput>(source, convert, convertBack);
    }

    private sealed class ConvertedProperty<TInput, TOutput>(
        IProperty<TInput> source,
        Func<TInput, TOutput> convert,
        Func<TOutput, TInput>? convertBack) : IProperty<TOutput>
    {
        private ConvertedAnimation<TInput, TOutput>? _convertedAnimation;

        private EventHandler<PropertyValueChangedEventArgs<TOutput>>? _valueChanged;

        public string Name => source.Name;

        public Type ValueType => typeof(TOutput);

        public bool IsAnimatable => source.IsAnimatable;

        public bool HasLocalValue => source.HasLocalValue;

        public TOutput DefaultValue => convert(source.DefaultValue);

        public TOutput CurrentValue
        {
            get => convert(source.CurrentValue);
            set
            {
                if (convertBack == null)
                {
                    throw new NotSupportedException("Setting value is not supported in this converted property.");
                }

                source.CurrentValue = convertBack(value);
            }
        }

        public IAnimation<TOutput>? Animation
        {
            get
            {
                if (source.Animation == null)
                {
                    _convertedAnimation = null;
                    return null;
                }

                if (_convertedAnimation == null)
                {
                    _convertedAnimation = new ConvertedAnimation<TInput, TOutput>(source.Animation, convert);
                }

                return _convertedAnimation;
            }
            set => throw new NotSupportedException("Setting animation is not supported in converted property.");
        }

        public event EventHandler<PropertyValueChangedEventArgs<TOutput>>? ValueChanged
        {
            add
            {
                if (_valueChanged == null)
                {
                    source.ValueChanged += OnValueChanged;
                }

                _valueChanged += value;
            }
            remove
            {
                _valueChanged -= value;
                if (_valueChanged == null)
                {
                    source.ValueChanged -= OnValueChanged;
                }
            }
        }

        public void operator <<= (TOutput value) => CurrentValue = value;

        public TOutput GetValue(IClock clock)
        {
            return convert(source.GetValue(clock));
        }

        public object? GetValueAsObject(IClock clock)
        {
            return GetValue(clock);
        }

        public void SetValueAsObject(object? value)
        {
            if (convertBack == null)
            {
                throw new NotSupportedException("Setting value is not supported in this converted property.");
            }

            if (value is TOutput output)
            {
                source.SetValueAsObject(convertBack(output));
            }
            else
            {
                throw new ArgumentException($"Invalid value type. Expected {typeof(TOutput)}, got {value?.GetType()}");
            }
        }

        public object? GetDefaultValueAsObject()
        {
            return DefaultValue;
        }

        public void SetPropertyInfo(PropertyInfo propertyInfo)
        {
            source.SetPropertyInfo(propertyInfo);
        }

        public PropertyInfo? GetPropertyInfo()
        {
            return source.GetPropertyInfo();
        }

        private void OnValueChanged(object? sender, PropertyValueChangedEventArgs<TInput> e)
        {
            var newArgs = new PropertyValueChangedEventArgs<TOutput>(
                this,
                convert(e.OldValue),
                convert(e.NewValue));
            _valueChanged?.Invoke(this, newArgs);
        }
    }

    private sealed class ConvertedAnimation<TInput, TOutput>(
        IAnimation<TInput> source,
        Func<TInput, TOutput> convert) : Hierarchical, IAnimation<TOutput>
    {
        public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

        public IValidator<TOutput>? Validator { get; set; }

        public TOutput? GetAnimatedValue(IClock clock)
        {
            TInput? inputValue = source.GetAnimatedValue(clock);
            if (inputValue is not null)
            {
                return convert(inputValue);
            }
            else
            {
                return default;
            }
        }

        public TOutput? Interpolate(TimeSpan timeSpan)
        {
            TInput? inputValue = source.Interpolate(timeSpan);
            if (inputValue is not null)
            {
                return convert(inputValue);
            }
            else
            {
                return default;
            }
        }

        public TimeSpan Duration => source.Duration;

        public bool UseGlobalClock => source.UseGlobalClock;
    }
}
