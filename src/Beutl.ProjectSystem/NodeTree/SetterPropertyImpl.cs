using System.Reactive.Linq;
using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Framework;
using Beutl.Reactive;
using Beutl.Styling;

using Reactive.Bindings.Extensions;

namespace Beutl.NodeTree;

public sealed class SetterPropertyImpl<T> : IAbstractAnimatableProperty<T>
{
    private sealed class HasAnimationObservable : LightweightObservableBase<bool>
    {
        private IDisposable? _disposable;
        private readonly Setter<T> _setter;

        public HasAnimationObservable(Setter<T> setter)
        {
            _setter = setter;
        }

        protected override void Subscribed(IObserver<bool> observer, bool first)
        {
            base.Subscribed(observer, first);
            observer.OnNext(_setter.Animation is { Children.Count: > 0 });
        }

        protected override void Deinitialize()
        {
            _disposable?.Dispose();
            _disposable = null;

            _setter.Invalidated -= Setter_Invalidated;
        }

        protected override void Initialize()
        {
            _disposable?.Dispose();

            _setter.Invalidated += Setter_Invalidated;
        }

        private void Setter_Invalidated(object? sender, EventArgs e)
        {
            _disposable?.Dispose();
            if (_setter.Animation is { } animation)
            {
                _disposable = _setter.Animation.Children
                    .ObserveProperty(x => x.Count)
                    .Select(x => x > 0)
                    .Subscribe(PublishNext);
            }
        }
    }

    public SetterPropertyImpl(Setter<T> setter, Type implementedType)
    {
        Property = setter.Property;
        Setter = setter;
        ImplementedType = implementedType;
        HasAnimation = new HasAnimationObservable(setter);
    }

    public CoreProperty<T> Property { get; }

    public Setter<T> Setter { get; }

    public Animation<T> Animation
    {
        get
        {
            Setter.Animation ??= new Animation<T>(Property);
            return Setter.Animation;
        }
    }

    public IObservable<bool> HasAnimation { get; }

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
        CorePropertyMetadata? metadata = Property.GetMetadata<CorePropertyMetadata>(ImplementedType);
        json["property"] = metadata.SerializeName ?? Property.Name;
        json["target"] = TypeFormat.ToString(ImplementedType);

        json["setter"] = StyleSerializer.ToJson(Setter, ImplementedType).Item2;
    }

    public void ReadFromJson(JsonNode json)
    {
        if (json is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("setter", out var setterNode)
                && setterNode != null)
            {
                if (StyleSerializer.ToSetter(setterNode, Property.Name, ImplementedType) is Setter<T> setter)
                {
                    if (setter.Animation != null)
                    {
                        if (Setter.Animation == null)
                        {
                            Setter.Animation = setter.Animation;
                        }
                        else
                        {
                            Setter.Animation.Children.Clear();
                            Setter.Animation.Children.Replace(setter.Animation.Children);
                        }
                    }

                    Setter.Value = setter.Value;
                }
            }
        }
    }
}
