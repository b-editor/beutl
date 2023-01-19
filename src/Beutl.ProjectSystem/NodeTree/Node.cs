using System.Reactive.Linq;

using Beutl.Animation;
using Beutl.Framework;
using Beutl.Media;
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
}

public abstract class Node : Element, INode
{
    public abstract IReadOnlyList<INodeItem> Items { get; }

    public event EventHandler? NodeTreeInvalidated;

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    protected void InvalidateNodeTree()
    {
        NodeTreeInvalidated?.Invoke(this, EventArgs.Empty);
    }

    protected void RaiseInvalidated(RenderInvalidatedEventArgs args)
    {
        Invalidated?.Invoke(this, args);
    }

    // 1. ItemsのIInputSocket.Connection.Nodeを評価する。
    // 2. IOutputSocket.ConnectionsからIInputSocketにデータを送る (Receive)
    public virtual void Evaluate(EvaluationContext context)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            INodeItem item = Items[i];
            item.Evaluate(context);
        }
    }

    //public void ApplyTo(ICoreObject obj)
    //{
    //    Type objType = obj.GetType();
    //    for (int i = 0; i < Items.Count; i++)
    //    {
    //        INodeItem? item = Items[i];
    //        if (item.Property is { Property: { OwnerType: { } ownerType } property })
    //        {
    //            if (objType.IsAssignableTo(ownerType))
    //            {
    //                obj.SetValue(property, item.Value);
    //            }
    //        }
    //    }
    //}

    public static InputSocket<T> ToInput<T>(CoreProperty<T> property)
    {
        var setter = new Setter<T>(property);
        var propImpl = new SetterPropertyImpl<T>(setter, property.OwnerType);
        var socket = new _InputSocket<T>();
        socket.SetProperty(propImpl);
        return socket;
    }

    public static InputSocket<T> ToInput<T, TOwner>(CoreProperty<T> property)
    {
        var setter = new Setter<T>(property);
        var propImpl = new SetterPropertyImpl<T>(setter, typeof(TOwner));
        var socket = new _InputSocket<T>();
        socket.SetProperty(propImpl);
        return socket;
    }

    public static InputSocket<T> ToInput<T>(CoreProperty<T> property, T value)
    {
        var setter = new Setter<T>(property, value);
        var propImpl = new SetterPropertyImpl<T>(setter, property.OwnerType);
        var socket = new _InputSocket<T>();
        socket.SetProperty(propImpl);
        return socket;
    }

    public static InputSocket<T> ToInput<T, TOwner>(CoreProperty<T> property, T value)
    {
        var setter = new Setter<T>(property, value);
        var propImpl = new SetterPropertyImpl<T>(setter, typeof(TOwner));
        var socket = new _InputSocket<T>();
        socket.SetProperty(propImpl);
        return socket;
    }

    private sealed class _InputSocket<T> : InputSocket<T>
    {
        public void SetProperty(IAbstractProperty<T> property)
        {
            Property = property;
        }
    }
}
