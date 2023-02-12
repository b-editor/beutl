using System.Reactive.Linq;
using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Collections;
using Beutl.Framework;
using Beutl.Media;
using Beutl.Reactive;
using Beutl.Styling;
using Beutl.Utilities;

using Reactive.Bindings.Extensions;

namespace Beutl.NodeTree;

internal static class SetterPropertyImplSerializeHelper
{
    public static JsonObject Serialize<T>(SetterPropertyImpl<T> property)
    {
        return new JsonObject
        {
            ["property"] = property.Property.Name,
            ["target"] = TypeFormat.ToString(property.ImplementedType),

            ["setter"] = StyleSerializer.ToJson(property.Setter, property.ImplementedType).Item2
        };
    }

    public static string? GetPropertyName(JsonNode jsonNode)
    {
        if (jsonNode is JsonObject obj
            && obj.TryGetPropertyValue("property", out var propNode)
            && propNode is JsonValue propValue
            && propValue.TryGetValue(out string? propName))
        {
            return propName;
        }

        return null;
    }
}
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
        json["property"] = Property.Name;
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

public abstract class Node : Element, INode
{
    public static readonly CoreProperty<bool> IsExpandedProperty;
    public static readonly CoreProperty<(double X, double Y)> PositionProperty;
    private readonly LogicalList<INodeItem> _items;
    private (double X, double Y) _position;
    private NodeTreeSpace? _nodeTree;

    static Node()
    {
        IsExpandedProperty = ConfigureProperty<bool, Node>(nameof(Position))
            .DefaultValue(true)
            .SerializeName("is-expanded")
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();

        PositionProperty = ConfigureProperty<(double X, double Y), Node>(nameof(Position))
            .Accessor(o => o.Position, (o, v) => o.Position = v)
            .DefaultValue((0, 0))
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();
    }

    public Node()
    {
        _items = new(this);

        _items.Attached += OnItemAttached;
        _items.Detached += OnItemDetached;
    }

    private void OnItemDetached(INodeItem obj)
    {
        obj.NodeTreeInvalidated -= OnItemNodeTreeInvalidated;
        obj.Invalidated -= OnItemInvalidated;
        if (_nodeTree != null)
        {
            obj.NotifyDetachedFromNodeTree(_nodeTree);
        }
    }

    private void OnItemAttached(INodeItem obj)
    {
        obj.NodeTreeInvalidated += OnItemNodeTreeInvalidated;
        obj.Invalidated += OnItemInvalidated;
        if (_nodeTree != null)
        {
            obj.NotifyAttachedToNodeTree(_nodeTree);
        }
    }

    private void OnItemInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        RaiseInvalidated(e);
    }

    public ICoreList<INodeItem> Items => _items;

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public (double X, double Y) Position
    {
        get => _position;
        set => SetAndRaise(PositionProperty, ref _position, value);
    }

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

    public virtual void PreEvaluate(EvaluationContext context)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            INodeItem item = Items[i];
            item.PreEvaluate(context);
        }
    }

    public virtual void PostEvaluate(EvaluationContext context)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            INodeItem item = Items[i];
            item.PostEvaluate(context);
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

    protected InputSocket<T> AsInput<T>(CoreProperty<T> property)
    {
        if (ContainsByName(property.Name))
            throw new InvalidOperationException("An item with the same name already exists.");

        var setter = new Setter<T>(property);
        var propImpl = new SetterPropertyImpl<T>(setter, property.OwnerType);
        var socket = new InputSocketImpl<T>();
        socket.SetProperty(propImpl);
        Items.Add(socket);
        return socket;
    }

    protected InputSocket<T> AsInput<T, TOwner>(CoreProperty<T> property)
    {
        if (ContainsByName(property.Name))
            throw new InvalidOperationException("An item with the same name already exists.");

        var setter = new Setter<T>(property);
        var propImpl = new SetterPropertyImpl<T>(setter, typeof(TOwner));
        var socket = new InputSocketImpl<T>();
        socket.SetProperty(propImpl);
        Items.Add(socket);
        return socket;
    }

    protected InputSocket<T> AsInput<T>(CoreProperty<T> property, T value)
    {
        if (ContainsByName(property.Name))
            throw new InvalidOperationException("An item with the same name already exists.");

        var setter = new Setter<T>(property, value);
        var propImpl = new SetterPropertyImpl<T>(setter, property.OwnerType);
        var socket = new InputSocketImpl<T>();
        socket.SetProperty(propImpl);
        Items.Add(socket);
        return socket;
    }

    protected InputSocket<T> AsInput<T, TOwner>(CoreProperty<T> property, T value)
    {
        if (ContainsByName(property.Name))
            throw new InvalidOperationException("An item with the same name already exists.");

        var setter = new Setter<T>(property, value);
        var propImpl = new SetterPropertyImpl<T>(setter, typeof(TOwner));
        var socket = new InputSocketImpl<T>();
        socket.SetProperty(propImpl);
        Items.Add(socket);
        return socket;
    }

    protected InputSocket<T> AsInput<T>(string name, string? display = null)
    {
        if (ContainsByName(name))
            throw new InvalidOperationException("An item with the same name already exists.");

        display ??= name;
        var socket = new InputSocketImpl<T>()
        {
            Name = display,
        };
        socket.SetName(name);
        Items.Add(socket);
        return socket;
    }

    protected OutputSocket<T> AsOutput<T>(string name, T value, string? display = null)
    {
        if (ContainsByName(name))
            throw new InvalidOperationException("An item with the same name already exists.");

        display ??= name;
        var socket = new OutputSocketImpl<T>(name)
        {
            Name = display,
            Value = value
        };
        Items.Add(socket);
        return socket;
    }

    protected OutputSocket<T> AsOutput<T>(string name, string? display = null)
    {
        if (ContainsByName(name))
            throw new InvalidOperationException("An item with the same name already exists.");

        display ??= name;
        var socket = new OutputSocketImpl<T>(name)
        {
            Name = display
        };
        Items.Add(socket);
        return socket;
    }

    protected NodeItem<T> AsProperty<T>(CoreProperty<T> property)
    {
        if (ContainsByName(property.Name))
            throw new InvalidOperationException("An item with the same name already exists.");

        var setter = new Setter<T>(property);
        var propImpl = new SetterPropertyImpl<T>(setter, property.OwnerType);
        var socket = new NodeItemImpl<T>();
        socket.SetProperty(propImpl);
        Items.Add(socket);
        return socket;
    }

    protected NodeItem<T> AsProperty<T>(CoreProperty<T> property, T value)
    {
        if (ContainsByName(property.Name))
            throw new InvalidOperationException("An item with the same name already exists.");

        var setter = new Setter<T>(property, value);
        var propImpl = new SetterPropertyImpl<T>(setter, property.OwnerType);
        var socket = new NodeItemImpl<T>();
        socket.SetProperty(propImpl);
        Items.Add(socket);
        return socket;
    }

    private bool ContainsByName(string name)
    {
        return Items.Any(x => x is INodeItemImpl impl ? impl.GetName() == name : x.Property?.Property.Name == name);
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("position", out JsonNode? posNode)
                && posNode is JsonValue posVal
                && posVal.TryGetValue(out string? posStr))
            {
                var tokenizer = new RefStringTokenizer(posStr);
                if (tokenizer.TryReadDouble(out double x)
                    && tokenizer.TryReadDouble(out double y))
                {
                    Position = (x, y);
                }
            }

            if (obj.TryGetPropertyValue("items", out var itemsNode)
                && itemsNode is JsonArray itemsArray)
            {
                foreach (JsonNode? item in itemsArray)
                {
                    if (item is JsonObject itemObj)
                    {
                        string? name = SetterPropertyImplSerializeHelper.GetPropertyName(itemObj);
                        if (name != null)
                        {
                            INodeItem? nodeItem = Items.FirstOrDefault(
                                x => x is INodeItemImpl impl
                                    ? impl.GetName() == name
                                    : x.Property?.Property.Name == name);

                            if (nodeItem is IJsonSerializable serializable)
                            {
                                serializable.ReadFromJson(itemObj);
                            }
                        }
                    }
                }
            }
        }
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);
        json["position"] = $"{Position.X},{Position.Y}";

        var array = new JsonArray();
        foreach (INodeItem item in Items)
        {
            JsonNode itemJson = new JsonObject();
            if (item is IJsonSerializable serializable)
            {
                serializable.WriteToJson(ref itemJson);
                itemJson["@type"] = TypeFormat.ToString(item.GetType());
            }
            array.Add(itemJson);
        }

        json["items"] = array;
    }

    protected override void OnAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnAttachedToLogicalTree(args);
        if (args.Parent is NodeTreeSpace nodeTree)
        {
            _nodeTree = nodeTree;
            foreach (INodeItem item in _items.GetMarshal().Value)
            {
                item.NotifyAttachedToNodeTree(nodeTree);
            }
        }
    }

    protected override void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnDetachedFromLogicalTree(args);
        if (args.Parent is NodeTreeSpace nodeTree)
        {
            _nodeTree = null;
            foreach (INodeItem item in _items.GetMarshal().Value)
            {
                item.NotifyDetachedFromNodeTree(nodeTree);
            }
        }
    }

    private void OnItemNodeTreeInvalidated(object? sender, EventArgs e)
    {
        InvalidateNodeTree();
    }

    private interface INodeItemImpl
    {
        string? GetName();
    }

    private interface IInputSocketImpl : ICoreObject, INodeItemImpl
    {
    }

    private sealed class NodeItemImpl<T> : NodeItem<T>, INodeItemImpl
    {
        private string? _name;

        public void SetName(string name)
        {
            _name = name;
        }

        public string? GetName()
        {
            return _name ?? Property?.Property?.Name;
        }

        public void SetProperty(SetterPropertyImpl<T> property)
        {
            Property = property;
            property.Setter.Invalidated += OnSetterInvalidated;
        }

        private void OnSetterInvalidated(object? sender, EventArgs e)
        {
            RaiseInvalidated(new RenderInvalidatedEventArgs(this));
        }

        public SetterPropertyImpl<T>? GetProperty()
        {
            return Property as SetterPropertyImpl<T>;
        }

        public override void ReadFromJson(JsonNode json)
        {
            base.ReadFromJson(json);
            GetProperty()?.ReadFromJson(json);
        }

        public override void WriteToJson(ref JsonNode json)
        {
            base.WriteToJson(ref json);
            if (_name != null)
            {
                json["property"] = _name;
            }
            else
            {
                GetProperty()?.WriteToJson(ref json);
            }
        }
    }

    private sealed class OutputSocketImpl<T> : OutputSocket<T>, INodeItemImpl
    {
        private readonly string _name;

        public OutputSocketImpl(string name)
        {
            _name = name;
        }

        public string? GetName()
        {
            return _name;
        }

        public override void ReadFromJson(JsonNode json)
        {
            base.ReadFromJson(json);
        }

        public override void WriteToJson(ref JsonNode json)
        {
            base.WriteToJson(ref json);
            json["property"] = _name;
        }
    }

    private sealed class InputSocketImpl<T> : InputSocket<T>, IInputSocketImpl
    {
        private string? _name;

        public void SetName(string name)
        {
            _name = name;
        }

        public string? GetName()
        {
            return _name ?? Property?.Property?.Name;
        }

        public void SetProperty(SetterPropertyImpl<T> property)
        {
            Property = property;
            property.Setter.Invalidated += OnSetterInvalidated;
        }

        private void OnSetterInvalidated(object? sender, EventArgs e)
        {
            RaiseInvalidated(new RenderInvalidatedEventArgs(this));
        }

        public SetterPropertyImpl<T>? GetProperty()
        {
            return Property as SetterPropertyImpl<T>;
        }

        public override void ReadFromJson(JsonNode json)
        {
            base.ReadFromJson(json);
            GetProperty()?.ReadFromJson(json);
        }

        public override void WriteToJson(ref JsonNode json)
        {
            base.WriteToJson(ref json);
            if (_name != null)
            {
                json["property"] = _name;
            }
            else
            {
                GetProperty()?.WriteToJson(ref json);
            }
        }
    }
}
