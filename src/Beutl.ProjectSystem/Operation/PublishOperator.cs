using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Extensibility;
using Beutl.Graphics;
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

public abstract class PublishOperator<T> : SourceOperator, IPublishOperator
    where T : Renderable, new()
{
    public static readonly CoreProperty<T> ValueProperty;
    private T _value = null!;
    private readonly PropertyWithDefaultValue[] _properties;
    private bool _deserializing;

    private readonly EvaluationTarget _evaluationTarget =
        typeof(T).IsAssignableTo(typeof(Drawable)) ? EvaluationTarget.Graphics
        : typeof(T).IsAssignableTo(typeof(Audio.Audio)) ? EvaluationTarget.Audio : EvaluationTarget.Unknown;

    static PublishOperator()
    {
        ValueProperty = ConfigureProperty<T, PublishOperator<T>>(nameof(Value))
            .Accessor(o => o.Value, (o, v) => o.Value = v)
            .Register();

        Hierarchy<PublishOperator<T>>(ValueProperty);
    }

    protected PublishOperator(PropertyWithDefaultValue[] properties)
    {
        _properties = properties;
        Value = new T();
    }

    public T Value
    {
        get => _value;
        set => SetAndRaise(ValueProperty, ref _value, value);
    }

    Renderable IPublishOperator.Value => Value;

    public override EvaluationTarget GetEvaluationTarget() => _evaluationTarget;

    public override void Evaluate(OperatorEvaluationContext context)
    {
        context.AddFlowRenderable(Value);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        _deserializing = true;
        try
        {
            base.Deserialize(context);
            Debug.Assert(Value != null);
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            Value ??= new T();
            // 互換性処理
            if (!context.Contains(nameof(Value)))
            {
                foreach (var prop in _properties)
                {
                    string name = prop.Property.Name;
                    if (!context.Contains(name)) continue;

                    JsonNode? propNode = context.GetValue<JsonNode?>(name);
                    (CoreProperty? _, Optional<object?> value, IAnimation? animation) =
                        PropertyEntrySerializer.ToTuple(propNode, name, typeof(T), context);

                    if (value.HasValue)
                    {
                        Value.SetValue(prop.Property, value.Value);
                    }

                    if (animation == null) continue;

                    var oldAnm = Value.Animations.FirstOrDefault(i => i.Property.Id == prop.Property.Id);
                    if (oldAnm == null) continue;

                    Value.Animations.Remove(oldAnm);
                    Value.Animations.Add(animation);
                }
            }
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
            if (e.NewValue == null)
            {
                return;
            }

            foreach (var property in _properties)
            {
                var propertyType = property.Property.PropertyType;
                var adapterType = typeof(AnimatablePropertyAdapter<>).MakeGenericType(propertyType);
                var adapter = (IPropertyAdapter)Activator.CreateInstance(adapterType, property.Property, Value)!;
                Properties.Add(adapter);
                if (!_deserializing)
                {
                    var obj = property.Factory();
                    if (obj.HasValue)
                    {
                        object? value = obj.Value;
                        if (!propertyType.IsValueType && value == null)
                        {
                            Value.SetValue(property.Property, null);
                        }
                        else
                        {
                            if (value == null)
                            {
                                value = property.Property.GetMetadata<T, CorePropertyMetadata>().GetDefaultValue();
                            }
                            else if (!propertyType.IsInstanceOfType(value))
                            {
                                try
                                {
                                    value = TypeDescriptor.GetConverter(propertyType).ConvertFrom(value);
                                }
                                catch
                                {
                                    value = TypeDescriptor.GetConverter(value!.GetType())
                                        .ConvertTo(value, propertyType);
                                }
                            }

                            Value.SetValue(property.Property, value);
                        }
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
