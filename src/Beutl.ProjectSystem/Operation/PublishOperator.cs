using System.ComponentModel;
using Beutl.Extensibility;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Operation;

public record struct PropertyWithDefaultValue(CoreProperty Property, Func<Optional<object?>> Factory)
{
    public static implicit operator PropertyWithDefaultValue(
        (CoreProperty Property, Optional<object?> DefaultValue) tuple)
    {
        return new PropertyWithDefaultValue(tuple.Property, () => tuple.DefaultValue);
    }

    public static implicit operator PropertyWithDefaultValue(
        (CoreProperty Property, Func<Optional<object?>> Factory) tuple)
    {
        return new PropertyWithDefaultValue(tuple.Property, tuple.Factory);
    }

    public static implicit operator PropertyWithDefaultValue(CoreProperty property)
    {
        return (property, () => Optional<object?>.Empty);
    }
}

public abstract class PublishOperator<T> : SourceOperator
    where T : Renderable, new()
{
    public static readonly CoreProperty<T?> ValueProperty;
    private T? _value;
    private readonly PropertyWithDefaultValue[] _properties;
    private bool _deserializing;

    static PublishOperator()
    {
        ValueProperty = ConfigureProperty<T?, PublishOperator<T>>(nameof(Value))
            .Accessor(o => o.Value, (o, v) => o.Value = v)
            .Register();

        Hierarchy<PublishOperator<T>>(ValueProperty);
    }

    protected PublishOperator(PropertyWithDefaultValue[] properties)
    {
        _properties = properties;
        Value = new T();
    }

    public T? Value
    {
        get => _value;
        set => SetAndRaise(ValueProperty, ref _value, value);
    }

    public override void Evaluate(OperatorEvaluationContext context)
    {
        if (Value != null)
        {
            context.AddFlowRenderable(Value);
        }
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        _deserializing = true;
        try
        {
            base.Deserialize(context);
        }
        finally
        {
            _deserializing = false;
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args is CorePropertyChangedEventArgs<T?> e
            && e.Property.Id == ValueProperty.Id)
        {
            RaiseInvalidated(new RenderInvalidatedEventArgs(this, nameof(Value)));
            if (e.OldValue is IAffectsRender oldValue)
            {
                oldValue.Invalidated -= OnValueInvalidated;
            }

            if (e.NewValue is IAffectsRender newValue)
            {
                newValue.Invalidated += OnValueInvalidated;
            }

            Properties.Clear();
            foreach (var property in _properties)
            {
                var adapterType = typeof(AnimatablePropertyAdapter<>).MakeGenericType(property.Property.PropertyType);
                var adapter = (IPropertyAdapter)Activator.CreateInstance(adapterType, property.Property, Value)!;
                Properties.Add(adapter);
                if (!_deserializing)
                {
                    var obj = property.Factory();
                    if (obj.HasValue)
                    {
                        Value?.SetValue(property.Property, obj.Value);
                    }
                }
            }
        }
    }

    private void OnValueInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        RaiseInvalidated(e);
    }
}
