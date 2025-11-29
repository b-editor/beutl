using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Beutl.Extensibility;
using Beutl.Media;
using Beutl.NodeTree.Nodes.Group;
using Beutl.Serialization;

namespace Beutl.NodeTree.Nodes;

public class LayerInputNode : Node, ISocketsCanBeAdded
{
    public SocketLocation PossibleLocation => SocketLocation.Right;

    public interface ILayerInputSocket : IOutputSocket, IAutomaticallyGeneratedSocket
    {
        void SetProperty(IPropertyAdapter property);

        void SetupProperty(string propertyName);

        IPropertyAdapter? GetProperty();
    }

    public class LayerInputSocket<T> : OutputSocket<T>, ILayerInputSocket, IGroupSocket
    {
        private NodePropertyAdapter<T>? _property;

        static LayerInputSocket()
        {
        }

        public string? AssociatedPropertyName { get; set; }

        public Type? AssociatedPropertyType { get; set; }

        public void SetProperty(NodePropertyAdapter<T> property)
        {
            _property = property;
            AssociatedPropertyName = property.Name;
            AssociatedPropertyType = typeof(T);

            property.Edited += OnSetterInvalidated;
        }

        void ILayerInputSocket.SetProperty(IPropertyAdapter property)
        {
            SetProperty((NodePropertyAdapter<T>)property);
        }

        IPropertyAdapter? ILayerInputSocket.GetProperty()
        {
            return _property;
        }

        public void SetupProperty(string propertyName)
        {
            SetProperty(new NodePropertyAdapter<T>(propertyName));
        }

        private void OnSetterInvalidated(object? sender, EventArgs e)
        {
            RaiseInvalidated(new RenderInvalidatedEventArgs(this));
        }

        public NodePropertyAdapter<T>? GetProperty()
        {
            return _property;
        }

        public override void PreEvaluate(EvaluationContext context)
        {
            if (GetProperty() is { } property)
            {
                if (property is IAnimatablePropertyAdapter<T> { Animation: { } animation })
                {
                    Value = animation.GetAnimatedValue(context.Renderer.Time);
                }
                else
                {
                    Value = property.GetValue();
                }
            }
        }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            GetProperty()?.Serialize(context);
        }

        public override void Deserialize(ICoreSerializationContext context)
        {
            base.Deserialize(context);
            string name = context.GetValue<string>("Property")!;

            if (name != null)
            {
                SetupProperty(name);

                GetProperty()?.Deserialize(context);
            }
        }
    }

    public bool AddSocket(ISocket socket, [NotNullWhen(true)] out Connection? connection)
    {
        connection = null;
        if (socket is IInputSocket { AssociatedType: { } valueType } inputSocket)
        {
            Type type = typeof(LayerInputSocket<>).MakeGenericType(valueType);

            if (Activator.CreateInstance(type) is ILayerInputSocket outputSocket)
            {
                ((NodeItem)outputSocket).LocalId = NextLocalId++;
                outputSocket.SetupProperty(inputSocket.Name);
                outputSocket.GetProperty()?.SetValue(inputSocket.Property?.GetValue());

                Items.Add(outputSocket);
                if (outputSocket.TryConnect(inputSocket))
                {
                    connection = inputSocket.Connection!;
                    return true;
                }
                else
                {
                    Items.Remove(outputSocket);
                }
            }
        }

        return false;
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<JsonArray>("Items") is { } itemsArray)
        {
            int index = 0;
            foreach (JsonObject itemJson in itemsArray.OfType<JsonObject>())
            {
                if (itemJson.TryGetDiscriminator(out Type? type)
                    && Activator.CreateInstance(type) is ILayerInputSocket socket)
                {
                    if (socket is ICoreSerializable serializable)
                    {
                        if (LocalSerializationErrorNotifier.Current is not { } notifier)
                        {
                            notifier = NullSerializationErrorNotifier.Instance;
                        }
                        ICoreSerializationContext? parent = ThreadLocalSerializationContext.Current;

                        var innerContext = new JsonSerializationContext(type, notifier, parent, itemJson);
                        using (ThreadLocalSerializationContext.Enter(innerContext))
                        {
                            serializable.Deserialize(innerContext);
                            innerContext.AfterDeserialized(serializable);
                        }
                    }

                    Items.Add(socket);
                    ((NodeItem)socket).LocalId = index;
                }

                index++;
            }

            NextLocalId = index;
        }
    }
}
