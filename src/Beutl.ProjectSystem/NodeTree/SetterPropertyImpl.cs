using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Extensibility;
using Beutl.Reactive;
using Beutl.Serialization;
using Beutl.Styling;

namespace Beutl.NodeTree;

public sealed class SetterPropertyImpl<T>(Setter<T> setter, Type implementedType) : IAbstractAnimatableProperty<T>
{
    private sealed class AnimationObservable : LightweightObservableBase<IAnimation<T>?>
    {
        private readonly Setter<T> _setter;
        private IAnimation<T>? _prevAnimation;

        public AnimationObservable(Setter<T> setter)
        {
            _setter = setter;
            _prevAnimation = setter.Animation;
        }

        protected override void Subscribed(IObserver<IAnimation<T>?> observer, bool first)
        {
            base.Subscribed(observer, first);
            observer.OnNext(_setter.Animation);
        }

        protected override void Deinitialize()
        {
            _setter.Invalidated -= Setter_Invalidated;
        }

        protected override void Initialize()
        {
            _setter.Invalidated += Setter_Invalidated;
        }

        private void Setter_Invalidated(object? sender, EventArgs e)
        {
            if (_prevAnimation != _setter.Animation)
            {
                PublishNext(_setter.Animation);
                _prevAnimation = _setter.Animation;
            }
        }
    }

    public CoreProperty<T> Property { get; } = setter.Property;

    public Setter<T> Setter { get; } = setter;

    public IAnimation<T>? Animation
    {
        get => Setter.Animation;
        set => Setter.Animation = value;
    }

    public IObservable<IAnimation<T>?> ObserveAnimation { get; } = new AnimationObservable(setter);

    public Type ImplementedType { get; } = implementedType;

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

    CoreProperty? IAbstractProperty.GetCoreProperty() => Property;

    public IObservable<T?> GetObservable()
    {
        return Setter;
    }

    public T? GetValue()
    {
        return Setter.Value;
    }

    public void SetValue(T? value)
    {
        Setter.Value = value;
    }

    public object? GetDefaultValue()
    {
        return Property.GetMetadata<ICorePropertyMetadata>(ImplementedType).GetDefaultValue();
    }

    public void Serialize(ICoreSerializationContext context)
    {
        context.SetValue(nameof(Property), Property.Name);
        context.SetValue("Target", TypeFormat.ToString(ImplementedType));

        context.SetValue(nameof(Setter), StyleSerializer.ToJson(Setter, ImplementedType, context).Item2);
    }

    public void Deserialize(ICoreSerializationContext context)
    {
        if (context.GetValue<JsonNode>(nameof(Setter)) is { } setterNode)
        {
            if (StyleSerializer.ToSetter(setterNode, Property.Name, ImplementedType, context) is Setter<T> setter)
            {
                if (setter.Animation != null)
                {
                    Setter.Animation = setter.Animation;
                }

                Setter.Value = setter.Value;
            }
        }
    }
}
