using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Framework;
using Beutl.Reactive;
using Beutl.Styling;

namespace Beutl.NodeTree;

public sealed class SetterPropertyImpl<T> : IAbstractAnimatableProperty<T>
{
    private sealed class AnimationObservable : LightweightObservableBase<IAnimation<T>?>
    {
        private readonly Setter<T> _setter;
        private IAnimation<T>? _prevAnimation;

        public AnimationObservable(Setter<T> setter)
        {
            _setter = setter;
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

    public SetterPropertyImpl(Setter<T> setter, Type implementedType)
    {
        Property = setter.Property;
        Setter = setter;
        ImplementedType = implementedType;
        ObserveAnimation = new AnimationObservable(setter);
    }

    public CoreProperty<T> Property { get; }

    public Setter<T> Setter { get; }

    public IAnimation<T>? Animation
    {
        get => Setter.Animation;
        set => Setter.Animation = value;
    }

    public IObservable<IAnimation<T>?> ObserveAnimation { get; }

    public Type ImplementedType { get; }

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

    public void WriteToJson(ref JsonNode json)
    {
        json[nameof(Property)] = Property.Name;
        json["Target"] = TypeFormat.ToString(ImplementedType);

        json[nameof(Setter)] = StyleSerializer.ToJson(Setter, ImplementedType).Item2;
    }

    public void ReadFromJson(JsonNode json)
    {
        if (json is JsonObject obj)
        {
            if (obj.TryGetPropertyValue(nameof(Setter), out JsonNode? setterNode)
                && setterNode != null)
            {
                if (StyleSerializer.ToSetter(setterNode, Property.Name, ImplementedType) is Setter<T> setter)
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
}
