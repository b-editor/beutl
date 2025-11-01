using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Extensibility;
using Beutl.Reactive;
using Beutl.Serialization;
using Reactive.Bindings;

namespace Beutl.NodeTree;

public sealed class NodePropertyAdapter<T> : IAnimatablePropertyAdapter<T>
{
    private readonly ReactivePropertySlim<T?> _rxProperty = new();
    private IAnimation<T>? _animation;

    public NodePropertyAdapter(string name, T? value, IAnimation<T>? animation)
    {
        Name = name;    
        Animation = animation;
        ObserveAnimation = new AnimationObservable(this);
        _rxProperty.Value = value;
    }

    public NodePropertyAdapter(string name)
    {
        Name = name;
        ObserveAnimation = new AnimationObservable(this);
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
            adapter.Edited -= Setter_Edited;
        }

        protected override void Initialize()
        {
            adapter.Edited += Setter_Edited;
        }

        private void Setter_Edited(object? sender, EventArgs e)
        {
            if (_prevAnimation != adapter.Animation)
            {
                PublishNext(adapter.Animation);
                _prevAnimation = adapter.Animation;
            }
        }
    }

    public IAnimation<T>? Animation
    {
        get => _animation;
        set
        {
            if (_animation != value)
            {
                if (_animation != null)
                {
                    _animation.Edited -= Animation_Edited;
                }

                _animation = value;

                if (value != null)
                {
                    value.Edited += Animation_Edited;
                }

                Edited?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public IObservable<IAnimation<T>?> ObserveAnimation { get; }

    public Type ImplementedType => typeof(NodePropertyAdapter<T>);

    public Type PropertyType => typeof(T);

    public string Name { get; }

    public string DisplayName => Name;

    public string? Description => null;

    public bool IsReadOnly => false;

    public event EventHandler? Edited;

    private void Animation_Edited(object? sender, EventArgs e)
    {
        Edited?.Invoke(this, EventArgs.Empty);
    }

    private void Value_Edited(object? sender, EventArgs e)
    {
        Edited?.Invoke(this, EventArgs.Empty);
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
        if (!EqualityComparer<T>.Default.Equals(_rxProperty.Value, value))
        {
            if (_rxProperty.Value is INotifyEdited oldValue)
            {
                oldValue.Edited -= Value_Edited;
            }

            _rxProperty.Value = value;

            Edited?.Invoke(this, EventArgs.Empty);
            if (value is INotifyEdited newValue)
            {
                newValue.Edited += Value_Edited;
            }
        }
    }

    public object? GetDefaultValue()
    {
        return null;
    }

    public void Serialize(ICoreSerializationContext context)
    {
        context.SetValue("Property", Name);
        context.SetValue("Target", TypeFormat.ToString(ImplementedType));

        context.SetValue("Setter", PropertyEntrySerializer.ToJson(Name, _rxProperty.Value, Animation, PropertyType, ImplementedType, context).Item2);
    }

    public void Deserialize(ICoreSerializationContext context)
    {
        if (context.GetValue<JsonNode>("Setter") is not { } setterNode)
            return;

        (Optional<object?> value, IAnimation? animation) =
            PropertyEntrySerializer.ToTuple(setterNode, Name, PropertyType, ImplementedType, context);

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
