using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Framework;
using Beutl.Media;
using Beutl.Reactive;
using Beutl.Styling;

namespace Beutl.Operation;

public interface IStylingSetterPropertyImpl : IAbstractProperty
{
    ISetter Setter { get; }

    IStyle Style { get; }
}

public sealed class StylingSetterPropertyImpl<T> : IAbstractAnimatableProperty<T>, IStylingSetterPropertyImpl
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

    public StylingSetterPropertyImpl(Setter<T> setter, Style style)
    {
        Property = setter.Property;
        Setter = setter;
        Style = style;
        ObserveAnimation = new AnimationObservable(setter);
    }

    public CoreProperty<T> Property { get; }

    public Setter<T> Setter { get; }

    public Style Style { get; }

    public IAnimation<T>? Animation => Setter.Animation;

    public IObservable<IAnimation<T>?> ObserveAnimation { get; }

    public Type ImplementedType => Style.TargetType;

    ISetter IStylingSetterPropertyImpl.Setter => Setter;

    IStyle IStylingSetterPropertyImpl.Style => Style;

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

public abstract class StylingOperator : SourceOperator
{
    private bool _isSettersChanging;
    private Style _style;

    protected StylingOperator()
    {
        Style = OnInitializeStyle(() =>
        {
            var list = new List<ISetter>();
            OnInitializeSetters(list);
            return list;
        });
    }

    public Style Style
    {
        get => _style;

        [MemberNotNull("_style")]
        private set
        {
            if (!ReferenceEquals(value, _style))
            {
                Properties.CollectionChanged -= Properties_CollectionChanged;
                if (_style != null)
                {
                    _style.Invalidated -= OnInvalidated;
                    _style.Setters.CollectionChanged -= Setters_CollectionChanged;
                    Properties.Clear();
                }

                _style = value;

                if (value != null)
                {
                    value.Invalidated += OnInvalidated;
                    value.Setters.CollectionChanged += Setters_CollectionChanged;
                    Type propType = typeof(StylingSetterPropertyImpl<>);
                    Properties.AddRange(value.Setters.OfType<ISetter>()
                        .Select(x =>
                        {
                            Type type = propType.MakeGenericType(x.Property.PropertyType);
                            return (IAbstractProperty)Activator.CreateInstance(type, x, _style)!;
                        }));
                }

                Properties.CollectionChanged += Properties_CollectionChanged;
                //Instance = null;
            }
        }
    }

    //public IStyleInstance? Instance { get; protected set; }

    protected abstract Style OnInitializeStyle(Func<IList<ISetter>> setters);

    protected virtual void OnInitializeSetters(IList<ISetter> initializing)
    {
    }

    private void OnInvalidated(object? s, EventArgs e)
    {
        RaiseInvalidated(new RenderInvalidatedEventArgs(this, nameof(Style)));
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject obj
            && obj.TryGetPropertyValue("style", out JsonNode? styleNode)
            && styleNode is JsonObject styleObj)
        {
            var style = StyleSerializer.ToStyle(styleObj);
            if (style != null)
            {
                Style = style;

                RaiseInvalidated(new RenderInvalidatedEventArgs(this));
            }
        }
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);
        if (json is JsonObject obj)
        {
            obj["style"] = StyleSerializer.ToJson(Style);
        }
    }

    private void Properties_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_isSettersChanging)
        {
            throw new InvalidOperationException(
                "If you inherit from 'StylingOperator', you cannot change 'Properties' directly; you must do so from 'Style.Setters'.");
        }
    }

    private void Setters_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        void Add(int index, IList list)
        {
            Type propType = typeof(StylingSetterPropertyImpl<>);

            Properties.InsertRange(index, list.OfType<ISetter>()
                .Select(x =>
                {
                    Type type = propType.MakeGenericType(x.Property.PropertyType);
                    return (IAbstractProperty)Activator.CreateInstance(type, x, Style)!;
                }));
        }

        void Remove(int index, IList list)
        {
            Properties.RemoveRange(index, list.Count);
        }

        try
        {
            _isSettersChanging = true;
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    Add(e.NewStartingIndex, e.NewItems!);
                    break;

                case NotifyCollectionChangedAction.Remove:
                    Remove(e.OldStartingIndex, e.OldItems!);
                    break;

                case NotifyCollectionChangedAction.Replace:
                    Remove(e.OldStartingIndex, e.OldItems!);
                    int newIndex = e.NewStartingIndex;
                    if (newIndex > e.OldStartingIndex)
                    {
                        newIndex -= e.OldItems!.Count;
                    }
                    Add(newIndex, e.NewItems!);
                    break;

                case NotifyCollectionChangedAction.Move:
                    Properties.MoveRange(e.OldStartingIndex, e.NewItems!.Count, e.NewStartingIndex);
                    break;

                case NotifyCollectionChangedAction.Reset:
                    Properties.Clear();
                    break;

                default:
                    break;
            }

            if (sender is ICollection collection)
            {
                RaiseInvalidated(new RenderInvalidatedEventArgs(collection));
            }
        }
        finally
        {
            _isSettersChanging = false;
        }
    }
}
