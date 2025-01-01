using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.Reactive;
using Beutl.Serialization;
using Beutl.Validation;
using Reactive.Bindings;

namespace Beutl.NodeTree;

public sealed class NodePropertyAdapter<T> : IAnimatablePropertyAdapter<T>
{
    private readonly CorePropertyMetadata<T> _metadata;
    private readonly ReactivePropertySlim<T?> _rxProperty = new();
    private IAnimation<T>? _animation;

    public NodePropertyAdapter(T? value, CoreProperty<T> property, IAnimation<T>? animation, Type implementedType)
    {
        Property = property;
        Animation = animation;
        ImplementedType = implementedType;
        ObserveAnimation = new AnimationObservable(this);
        _metadata = property.GetMetadata<CorePropertyMetadata<T>>(implementedType);
        _rxProperty.Value = value;
    }

    public NodePropertyAdapter(CoreProperty<T> property, Type implementedType)
    {
        Property = property;
        ImplementedType = implementedType;
        ObserveAnimation = new AnimationObservable(this);
        _metadata = property.GetMetadata<CorePropertyMetadata<T>>(implementedType);
        _rxProperty.Value = _metadata.HasDefaultValue ? _metadata.DefaultValue : default;
    }

    private sealed class AnimationObservable(NodePropertyAdapter<T> adapter) : LightweightObservableBase<IAnimation<T>?>
    {
        private IAnimation<T>? _prevAnimation = adapter.Animation;

        protected override void Subscribed(IObserver<IAnimation<T>?> observer, bool first)
        {
            base.Subscribed(observer, first);
            observer.OnNext(adapter.Animation);
        }

        protected override void Deinitialize()
        {
            adapter.Invalidated -= Setter_Invalidated;
        }

        protected override void Initialize()
        {
            adapter.Invalidated += Setter_Invalidated;
        }

        private void Setter_Invalidated(object? sender, EventArgs e)
        {
            if (_prevAnimation != adapter.Animation)
            {
                PublishNext(adapter.Animation);
                _prevAnimation = adapter.Animation;
            }
        }
    }

    public CoreProperty<T> Property { get; }

    public IAnimation<T>? Animation
    {
        get => _animation;
        set
        {
            if (_animation != value)
            {
                if (_animation != null)
                {
                    _animation.Invalidated -= Animation_Invalidated;
                }

                _animation = value;

                if (value != null)
                {
                    value.Invalidated += Animation_Invalidated;
                }

                Invalidated?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public IObservable<IAnimation<T>?> ObserveAnimation { get; }

    public Type ImplementedType { get; }

    public Type PropertyType => Property.PropertyType;

    public string DisplayName
    {
        get
        {
            CorePropertyMetadata metadata = Property.GetMetadata<CorePropertyMetadata>(ImplementedType);
            return metadata.DisplayAttribute?.GetName() ?? Property.Name;
        }
    }

    public string? Description
    {
        get
        {
            CorePropertyMetadata metadata = Property.GetMetadata<CorePropertyMetadata>(ImplementedType);
            return metadata.DisplayAttribute?.GetDescription();
        }
    }

    public bool IsReadOnly => false;

    CoreProperty IPropertyAdapter.GetCoreProperty() => Property;

    public event EventHandler? Invalidated;

    private void Animation_Invalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    private void Value_Invalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    public IObservable<T?> GetObservable()
    {
        return _rxProperty;
    }

    public T? GetValue()
    {
        return _rxProperty.Value;
    }

    public void SetValue(T? value)
    {
        if (_metadata.Validator is { } validator)
        {
            validator.TryCoerce(new ValidationContext(null, Property), ref value);
        }

        if (!EqualityComparer<T>.Default.Equals(_rxProperty.Value, value))
        {
            if (_rxProperty.Value is IAffectsRender oldValue)
            {
                oldValue.Invalidated -= Value_Invalidated;
            }

            _rxProperty.Value = value;

            Invalidated?.Invoke(this, EventArgs.Empty);
            if (value is IAffectsRender newValue)
            {
                newValue.Invalidated += Value_Invalidated;
            }
        }
    }

    public object? GetDefaultValue()
    {
        return Property.GetMetadata<ICorePropertyMetadata>(ImplementedType).GetDefaultValue();
    }

    public void Serialize(ICoreSerializationContext context)
    {
        context.SetValue(nameof(Property), Property.Name);
        context.SetValue("Target", TypeFormat.ToString(ImplementedType));

        context.SetValue("Setter", PropertyEntrySerializer.ToJson(Property, _rxProperty.Value, Animation, ImplementedType, context).Item2);
    }

    public void Deserialize(ICoreSerializationContext context)
    {
        if (context.GetValue<JsonNode>("Setter") is not { } setterNode)
            return;

        (CoreProperty? prop, Optional<object?> value, IAnimation? animation) =
            PropertyEntrySerializer.ToTuple(setterNode, Property.Name, ImplementedType, context);
        if (prop is null) return;

        if (animation != null)
        {
            Animation = animation as IAnimation<T>;
        }

        if (value is { HasValue: true, Value: T typedValue })
        {
            SetValue(typedValue);
        }
    }
}
