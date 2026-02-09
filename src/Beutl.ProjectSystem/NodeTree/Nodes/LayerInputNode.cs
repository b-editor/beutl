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
        // TODO: 接続先のSocketのPropertyを直接扱うようにする
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
        var nodeTreeModel = this.FindRequiredHierarchicalParent<NodeTreeModel>();
        connection = null;
        if (socket is IInputSocket { AssociatedType: { } valueType } inputSocket)
        {
            Type type = typeof(LayerInputSocket<>).MakeGenericType(valueType);

            if (Activator.CreateInstance(type) is ILayerInputSocket outputSocket)
            {
                outputSocket.SetupProperty(inputSocket.Name);
                outputSocket.GetProperty()?.SetValue(inputSocket.Property?.GetValue());

                Items.Add(outputSocket);

                connection = nodeTreeModel.Connect(inputSocket, outputSocket);
                return true;
            }
        }

        return false;
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<JsonArray>("Items") is { } itemsArray)
        {
            foreach (JsonObject itemJson in itemsArray.OfType<JsonObject>())
            {
                if (CoreSerializer.DeserializeFromJsonObject(itemJson, typeof(ILayerInputSocket)) is ILayerInputSocket
                    socket)
                {
                    Items.Add(socket);
                }
            }
        }
    }
}
